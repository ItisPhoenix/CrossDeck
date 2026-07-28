using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CrossDeckHost.Actions;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Controls;

public partial class ActionStepListControl : System.Windows.Controls.UserControl
{
    public ObservableCollection<ActionStep> Steps { get; } = new();

    private bool _isMacro;
    /// <summary>A macro is captured by recording real input, not by hand-picking a type and typing
    /// a value — that manual row is Multiple Actions' own affordance, hidden here when true. The
    /// Record button is the reverse: only Record Macro captures live input, so Multiple Actions
    /// never shows it.</summary>
    public bool IsMacro
    {
        get => _isMacro;
        set
        {
            _isMacro = value;
            ManualAddRow.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            RecordMacroButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            RecordMacroHint.Visibility = value && _macroRecorder.IsRecording ? Visibility.Visible : Visibility.Collapsed;
            MacroSpeedRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Raised when the playback speed changes, so the docked panel's auto-apply picks it up.</summary>
    public event Action? ActionChanged;

    public double MacroSpeed
    {
        get => double.TryParse((MacroSpeedCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1.0;
        set
        {
            var target = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (ComboBoxItem item in MacroSpeedCombo.Items)
            {
                if (item.Tag?.ToString() == target) { MacroSpeedCombo.SelectedItem = item; return; }
            }
            MacroSpeedCombo.SelectedIndex = 0;
        }
    }

    private void MacroSpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActionChanged?.Invoke();

    private readonly MacroRecorder _macroRecorder = new();
    private MacroRecordingOverlayWindow? _overlay;

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
        _overlay?.Close();
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
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 30 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBtn = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(2),
                Background = ThemeManager.Brush("Brush.Void"),
                ToolTip = "Set an icon for this step"
            };
            SetStepIconContent(iconBtn, step.Action.Icon);
            iconBtn.Click += (s, e) => ShowIconPopup(iconBtn, name =>
            {
                step.Action.Icon = "builtin:" + name;
                SetStepIconContent(iconBtn, step.Action.Icon);
            });
            Grid.SetColumn(iconBtn, 0);
            grid.Children.Add(iconBtn);

            var summary = new TextBlock
            {
                Text = DescribeStep(step.Action),
                Foreground = ThemeManager.Brush("Brush.Paper"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(summary, 1);
            grid.Children.Add(summary);

            var labelCell = new Grid { Margin = new Thickness(6, 0, 0, 0) };
            var labelPlaceholder = new TextBlock
            {
                Text = "Label", FontSize = 11, Foreground = ThemeManager.Brush("Brush.Mist"),
                Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = string.IsNullOrEmpty(step.Action.Label) ? Visibility.Visible : Visibility.Collapsed
            };
            var labelBox = new TextBox
            {
                Text = step.Action.Label ?? "",
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(4, 4, 4, 4),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Optional label shown on this step's tile"
            };
            labelBox.TextChanged += (s, e) =>
            {
                step.Action.Label = string.IsNullOrWhiteSpace(labelBox.Text) ? null : labelBox.Text;
                labelPlaceholder.Visibility = string.IsNullOrEmpty(labelBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                summary.Text = DescribeStep(step.Action);
            };
            labelCell.Children.Add(labelPlaceholder);
            labelCell.Children.Add(labelBox);
            Grid.SetColumn(labelCell, 2);
            grid.Children.Add(labelCell);

            var upBtn = new Button { Content = "↑", Width = 24, Height = 24, Padding = new Thickness(0), FontSize = 11, Margin = new Thickness(4, 0, 0, 0), IsEnabled = index > 0, ToolTip = "Move up" };
            upBtn.Click += (s, e) => Steps.Move(index, index - 1);
            Grid.SetColumn(upBtn, 3);
            grid.Children.Add(upBtn);

            var downBtn = new Button { Content = "↓", Width = 24, Height = 24, Padding = new Thickness(0), FontSize = 11, Margin = new Thickness(4, 0, 0, 0), IsEnabled = index < Steps.Count - 1, ToolTip = "Move down" };
            downBtn.Click += (s, e) => Steps.Move(index, index + 1);
            Grid.SetColumn(downBtn, 4);
            grid.Children.Add(downBtn);

            // A raw "✕" (U+2715) string mis-rendered as a stray chevron on some font fallback
            // paths — Segoe MDL2 Assets' own glyph (matching Save/Delete/Add Step elsewhere in
            // this app) renders reliably since it's a bundled system icon font, not a symbol lookup.
            var removeBtn = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Remove step",
                Style = (Style)FindResource("DangerButton")
            };
            removeBtn.Click += (s, e) => Steps.RemoveAt(index);
            Grid.SetColumn(removeBtn, 5);
            grid.Children.Add(removeBtn);

            row.Child = grid;
            StepsPanel.Children.Add(row);
        }
    }

    private static string DescribeStep(ActionModel act) => !string.IsNullOrWhiteSpace(act.Label) ? act.Label : act.Type switch
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

    private void StepTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StepMediaCommandCombo == null) return; // UI not fully initialized
        var type = (StepTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        bool isMedia = type == "media_control";
        StepMediaCommandCombo.Visibility = isMedia ? Visibility.Visible : Visibility.Collapsed;
        StepValueInput.Visibility = isMedia ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        var type = (StepTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrEmpty(type)) return;

        var value = type == "media_control"
            ? (StepMediaCommandCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PlayPause"
            : StepValueInput.Text.Trim();
        if (string.IsNullOrEmpty(value)) return;

        var action = type switch
        {
            "hotkey" => new ActionModel { Type = type, Keys = value.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList() },
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
        if (_macroRecorder.IsRecording) StopRecording();
        else StartRecording();
    }

    private void StartRecording()
    {
        if (_activeRecorder != null) return; // another panel is already recording
        _activeRecorder = this;
        _macroRecorder.Start();
        RecordMacroIcon.Text = "■";
        RecordMacroText.Text = "Stop Recording";
        RecordMacroButton.Style = (Style)FindResource("DangerButton");
        RecordMacroHint.Visibility = Visibility.Visible;

        _overlay = new MacroRecordingOverlayWindow();
        _overlay.StopRequested += StopRecording;
        _overlay.CancelRequested += CancelRecording;
        _overlay.Show();
    }

    /// <summary>Finishes recording and keeps whatever was captured — the floating overlay's Stop
    /// button and the in-panel Stop button both land here.</summary>
    private void StopRecording()
    {
        var recorded = _macroRecorder.Stop();
        foreach (var step in recorded) Steps.Add(step);
        ResetRecordingUi();
    }

    /// <summary>Discards everything captured this session instead of keeping it — confirmed first
    /// since there's no undo once the recorded steps are gone.</summary>
    private void CancelRecording()
    {
        var owner = System.Windows.Window.GetWindow(this);
        var result = System.Windows.MessageBox.Show(owner,
            "Discard everything recorded in this session?", "Cancel Recording",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        _macroRecorder.Stop();
        ResetRecordingUi();
    }

    private void ResetRecordingUi()
    {
        _activeRecorder = null;
        RecordMacroIcon.Text = "●";
        RecordMacroText.Text = "Record Keystrokes & Clicks";
        RecordMacroButton.Style = (Style)FindResource("StandardButton");
        RecordMacroHint.Visibility = Visibility.Collapsed;

        if (_overlay != null)
        {
            _overlay.StopRequested -= StopRecording;
            _overlay.CancelRequested -= CancelRecording;
            _overlay.Close();
            _overlay = null;
        }
    }

    private static void SetStepIconContent(Button btn, string? icon)
    {
        if (!string.IsNullOrEmpty(icon) && icon.StartsWith("builtin:"))
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin", icon.Substring(8) + ".png");
            if (File.Exists(path))
            {
                btn.Content = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(path)) };
                return;
            }
        }
        btn.Content = new TextBlock { Text = "+", FontSize = 14, Foreground = ThemeManager.Brush("Brush.Mist"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
    }

    // A small popup anchored to the step's icon button — lighter weight than a full dialog for
    // picking one of the bundled built-in icons for just that step.
    private void ShowIconPopup(Button anchor, Action<string> onSelect)
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin");
        if (!Directory.Exists(dir)) return;

        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false };
        var border = new Border
        {
            Background = ThemeManager.Brush("Brush.Panel"),
            BorderBrush = ThemeManager.Brush("Brush.Hairline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8)
        };
        var scroller = new ScrollViewer { Height = 160, Width = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var wrap = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        foreach (var file in Directory.GetFiles(dir, "*.png").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var img = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(file)) };
            var swatch = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(3),
                Margin = new Thickness(2),
                Content = img,
                Background = ThemeManager.Brush("Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(1),
                ToolTip = name
            };
            swatch.Click += (s, e) => { onSelect(name); popup.IsOpen = false; };
            wrap.Children.Add(swatch);
        }

        scroller.Content = wrap;
        border.Child = scroller;
        popup.Child = border;
        popup.IsOpen = true;
    }
}
