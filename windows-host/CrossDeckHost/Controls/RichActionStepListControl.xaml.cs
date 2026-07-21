using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Controls;

/// <summary>
/// Long-press's "one card per sub-button" editor — each entry is a full nested ActionConfigControl
/// (icon, label, type dropdown, parameters), not a light type+value row. Mirrors the Android
/// RichStepListEditor: recursion is safe here because each sub-editor has AllowChaining=false, so
/// it can never itself become Multiple Actions or Macro (no nesting).
/// </summary>
public partial class RichActionStepListControl : System.Windows.Controls.UserControl
{
    private readonly List<ActionConfigControl> _subEditors = new();

    public List<DiscoveredApp> AppList { get; set; } = new();

    /// <summary>Raised whenever any sub-button's action or type changes, so Save's enabled state stays live.</summary>
    public event Action? ActionChanged;

    public RichActionStepListControl()
    {
        InitializeComponent();
    }

    public void SetSubActions(List<ActionModel> actions)
    {
        _subEditors.Clear();
        foreach (var action in actions) AddSubEditor(action);
        RebuildRows();
    }

    public List<ActionModel> GetSubActions() => _subEditors.Select(e => e.GetAction()).ToList();

    private void AddSubEditor(ActionModel? initial = null)
    {
        var editor = new ActionConfigControl { AllowChaining = false, ShowIconPicker = true, ExtractIconOnSelect = true, AppList = AppList };
        editor.ActionChanged += () => ActionChanged?.Invoke();
        // A brand-new card must land on a real selection immediately — ActionTypeCombo has no
        // XAML-declared SelectedIndex, so skipping this left it blank with every parameter panel
        // collapsed until the user manually opened the dropdown themselves.
        editor.SetAction(initial ?? new ActionModel { Type = "hotkey" });
        _subEditors.Add(editor);
    }

    private void RebuildRows()
    {
        StepsPanel.Children.Clear();
        for (int i = 0; i < _subEditors.Count; i++)
        {
            int index = i;
            var editor = _subEditors[i];
            // Reorders reuse the same live editor instances (they hold in-progress form state, not
            // just a data model), so each one must be detached from its previous card before being
            // reparented into the freshly built one below — WPF forbids double-parenting.
            if (editor.Parent is Panel oldParent) oldParent.Children.Remove(editor);

            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = $"Long-Press Button {index + 1}",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = ThemeManager.Brush("Brush.Mist"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var upBtn = new Button { Content = "↑", Width = 28, Height = 28, Padding = new Thickness(0), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), IsEnabled = index > 0, ToolTip = "Move up" };
            upBtn.Click += (s, e) => { var m = _subEditors[index]; _subEditors.RemoveAt(index); _subEditors.Insert(index - 1, m); RebuildRows(); ActionChanged?.Invoke(); };
            var downBtn = new Button { Content = "↓", Width = 28, Height = 28, Padding = new Thickness(0), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), IsEnabled = index < _subEditors.Count - 1, ToolTip = "Move down" };
            downBtn.Click += (s, e) => { var m = _subEditors[index]; _subEditors.RemoveAt(index); _subEditors.Insert(index + 1, m); RebuildRows(); ActionChanged?.Invoke(); };
            var removeBtn = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 12 },
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Remove long-press button",
                Style = (Style)FindResource("DangerButton")
            };
            removeBtn.Click += (s, e) => { _subEditors.RemoveAt(index); RebuildRows(); ActionChanged?.Invoke(); };
            btnPanel.Children.Add(upBtn);
            btnPanel.Children.Add(downBtn);
            btnPanel.Children.Add(removeBtn);
            Grid.SetColumn(btnPanel, 1);
            header.Children.Add(btnPanel);

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(editor);

            var card = new Border
            {
                Background = ThemeManager.Brush("Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = content
            };
            StepsPanel.Children.Add(card);
        }

        var addBtn = new Button
        {
            Content = "+ Add Another Action",
            Style = (Style)FindResource("StandardButton"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8)
        };
        addBtn.Click += (s, e) => { AddSubEditor(); RebuildRows(); ActionChanged?.Invoke(); };
        StepsPanel.Children.Add(addBtn);
    }
}
