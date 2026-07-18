using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CrossDeckHost.Actions;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Controls;

public partial class ActionStepListControl : System.Windows.Controls.UserControl
{
    public ObservableCollection<ActionStep> Steps { get; } = new();

    private readonly MacroRecorder _macroRecorder = new();

    // Only one panel can record at a time — a second Record press elsewhere is a no-op while
    // one is already running, so the two hooks never race to append into two different lists.
    private static ActionStepListControl? _activeRecorder;

    public ActionStepListControl()
    {
        InitializeComponent();
        Steps.CollectionChanged += (s, e) => RebuildRows();
    }

    public void Dispose()
    {
        _macroRecorder.Dispose();
        if (_activeRecorder == this) _activeRecorder = null;
    }

    private void RebuildRows()
    {
        StepsPanel.Children.Clear();
        for (int i = 0; i < Steps.Count; i++)
        {
            int index = i;
            var step = Steps[i];

            var row = new Border
            {
                Background = ThemeManager.Brush("Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var summary = new TextBlock
            {
                Text = DescribeStep(step.Action),
                Foreground = ThemeManager.Brush("Brush.Paper"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(summary, 0);
            grid.Children.Add(summary);

            var upBtn = new Button { Content = "↑", Width = 28, Height = 28, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), IsEnabled = index > 0, ToolTip = "Move up" };
            upBtn.Click += (s, e) => Steps.Move(index, index - 1);
            Grid.SetColumn(upBtn, 1);
            grid.Children.Add(upBtn);

            var downBtn = new Button { Content = "↓", Width = 28, Height = 28, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), IsEnabled = index < Steps.Count - 1, ToolTip = "Move down" };
            downBtn.Click += (s, e) => Steps.Move(index, index + 1);
            Grid.SetColumn(downBtn, 2);
            grid.Children.Add(downBtn);

            var removeBtn = new Button { Content = "✕", Width = 28, Height = 28, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove step", Style = (Style)FindResource("DangerButton") };
            removeBtn.Click += (s, e) => Steps.RemoveAt(index);
            Grid.SetColumn(removeBtn, 3);
            grid.Children.Add(removeBtn);

            row.Child = grid;
            StepsPanel.Children.Add(row);
        }
    }

    private static string DescribeStep(ActionModel act) => act.Type switch
    {
        "hotkey" => $"Keyboard Shortcut: {(act.Keys != null ? string.Join(",", act.Keys) : "")}",
        "launch_app" => $"Launch App: {act.Path}",
        "media_control" => $"Media Control: {act.MediaCommand}",
        "open_url" => $"Open Website: {act.Url}",
        "run_command" => $"Run Command: {act.Command}",
        "text_snippet" => $"Text Snippet: {act.Text}",
        "mouse_click" => $"Mouse Click ({act.MouseButton}) at {act.MouseX},{act.MouseY}",
        _ => act.Type
    };

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        var type = (StepTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var value = StepValueInput.Text.Trim();
        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(value)) return;

        var action = type switch
        {
            "hotkey" => new ActionModel { Type = type, Keys = value.Split(',').mapStringList() },
            "launch_app" => new ActionModel { Type = type, Path = value },
            "media_control" => new ActionModel { Type = type, MediaCommand = value },
            "open_url" => new ActionModel { Type = type, Url = value },
            "run_command" => new ActionModel { Type = type, Command = value },
            "text_snippet" => new ActionModel { Type = type, Text = value },
            _ => new ActionModel { Type = type }
        };

        Steps.Add(new ActionStep { Action = action, DelayAfterMs = 0 });
        StepValueInput.Clear();
    }

    private void RecordMacro_Click(object sender, RoutedEventArgs e)
    {
        if (_macroRecorder.IsRecording)
        {
            var recorded = _macroRecorder.Stop();
            foreach (var step in recorded) Steps.Add(step);
            _activeRecorder = null;
            RecordMacroIcon.Text = "●";
            RecordMacroText.Text = "Record Keystrokes & Clicks";
            RecordMacroButton.Style = (Style)FindResource("StandardButton");
            RecordMacroHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (_activeRecorder != null) return; // another panel is already recording
            _activeRecorder = this;
            _macroRecorder.Start();
            RecordMacroIcon.Text = "■";
            RecordMacroText.Text = "Stop Recording";
            RecordMacroButton.Style = (Style)FindResource("DangerButton");
            RecordMacroHint.Visibility = Visibility.Visible;
        }
    }
}
