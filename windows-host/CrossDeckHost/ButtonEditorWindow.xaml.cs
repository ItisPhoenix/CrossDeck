using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost;

public partial class ButtonEditorWindow : Window
{
    public ButtonModel Button { get; private set; }
    public bool IsDeleted { get; private set; } = false;
    public bool EnterFolderRequested { get; private set; } = false;

    // Favicon fetch debounce
    private CancellationTokenSource? _faviconCts;
    private static readonly HttpClient _faviconHttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };

    private readonly Actions.MacroRecorder _macroRecorder = new();

    private void DialogClose_Click(object sender, RoutedEventArgs e) => Close();

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        var label = (StepTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var value = StepValueInput.Text.Trim();
        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(value)) return;
        AppendMultiActionLine($"{label}: {value}");
        StepValueInput.Clear();
    }

    private void AddDelay_Click(object sender, RoutedEventArgs e) => AppendMultiActionLine("Delay (ms): 500");

    private void AppendMultiActionLine(string line)
    {
        MultiActionInput.Text = string.IsNullOrWhiteSpace(MultiActionInput.Text)
            ? line
            : MultiActionInput.Text.TrimEnd() + "\n" + line;
    }

    private void RecordMacro_Click(object sender, RoutedEventArgs e)
    {
        if (_macroRecorder.IsRecording)
        {
            var recorded = _macroRecorder.Stop();
            if (!string.IsNullOrWhiteSpace(recorded))
            {
                MultiActionInput.Text = string.IsNullOrWhiteSpace(MultiActionInput.Text)
                    ? recorded
                    : MultiActionInput.Text.TrimEnd() + "\n" + recorded;
            }
            RecordMacroButton.Content = "● Record Keystrokes";
            RecordMacroHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            _macroRecorder.Start();
            RecordMacroButton.Content = "■ Stop Recording";
            RecordMacroHint.Visibility = Visibility.Visible;
        }
    }

    public ButtonEditorWindow(ButtonModel button)
    {
        InitializeComponent();
        Button = button;
        
        // Apply styling theme after window loaded
        Loaded += (s, e) => ThemeManager.ApplyTheme(this);
        Closed += (s, e) => _macroRecorder.Dispose();

        // Load applications in combo
        _allApps = AppDiscovery.DiscoverApps();
        PathComboInput.ItemsSource = _allApps;
        Loaded += (s, e) => HookPathFilterTextBox();

        // Initialize values
        LabelInput.Text = button.Label;
        IconPathText.Text = button.Icon ?? "";

        // Set action type selection
        SetActionTypeSelection(button.Action.Type);

        // Initialize parameters depending on action type
        InitializeActionParameters(button.Action);

        // Populate inline built-in icons picker
        LoadBuiltinIcons();
    }

    private void SetActionTypeSelection(string type)
    {
        foreach (ComboBoxItem item in ActionTypeCombo.Items)
        {
            if (item.Tag?.ToString() == type)
            {
                ActionTypeCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void InitializeActionParameters(ActionModel action)
    {
        HotkeyInput.Text = action.Keys != null ? string.Join(",", action.Keys) : "";
        
        // Launch App path selection
        if (action.Type == "launch_app")
        {
            var matched = _allApps.FirstOrDefault(a => a.ExePath.Equals(action.Path, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                PathComboInput.SelectedItem = matched;
            }
            else
            {
                PathComboInput.Text = action.Path ?? "";
            }
        }

        // Media command selection
        string mediaCmd = action.MediaCommand ?? "PlayPause";
        foreach (ComboBoxItem item in MediaCommandCombo.Items)
        {
            if (item.Tag?.ToString() == mediaCmd)
            {
                MediaCommandCombo.SelectedItem = item;
                break;
            }
        }

        UrlInput.Text = action.Url ?? "";
        CommandInput.Text = action.Command ?? "";
        SnippetTextInput.Text = action.Text ?? "";
        TargetFolderIdInput.Text = action.TargetFolderId ?? "";
        MultiActionInput.Text = FormatMultiAction(action.Actions, action.Delays);

        // Long-press action renders in the same line format; a single non-chain action shows as one line.
        if (Button.LongPressAction != null)
        {
            var lp = Button.LongPressAction;
            LongPressInput.Text = lp.Type == "multi_action"
                ? FormatMultiAction(lp.Actions, lp.Delays)
                : FormatMultiAction(new System.Collections.Generic.List<ActionModel> { lp }, null);
        }

        // Dial target selection
        string dialTgt = action.DialTarget ?? "volume";
        foreach (ComboBoxItem item in DialTargetCombo.Items)
        {
            if (item.Tag?.ToString() == dialTgt)
            {
                DialTargetCombo.SelectedItem = item;
                break;
            }
        }
    }

    // User-facing labels for the Multi-Action editor — not the internal wire-protocol Type strings.
    private static readonly System.Collections.Generic.Dictionary<string, string> MultiActionLabels = new()
    {
        ["hotkey"] = "Keyboard Shortcut",
        ["launch_app"] = "Launch App",
        ["run_command"] = "Run Command",
        ["media_control"] = "Media Control",
        ["open_url"] = "Open Website",
        ["text_snippet"] = "Text Snippet",
    };

    private string FormatMultiAction(System.Collections.Generic.List<ActionModel>? actions, System.Collections.Generic.List<int>? delays)
    {
        if (actions == null) return "";
        var lines = new System.Collections.Generic.List<string>();
        for (int i = 0; i < actions.Count; i++)
        {
            var act = actions[i];
            if (MultiActionLabels.TryGetValue(act.Type, out var label))
            {
                var value = act.Type switch
                {
                    "hotkey" => act.Keys != null ? string.Join(",", act.Keys) : "",
                    "launch_app" => act.Path,
                    "run_command" => act.Command,
                    "media_control" => act.MediaCommand,
                    "open_url" => act.Url,
                    "text_snippet" => act.Text,
                    _ => ""
                };
                lines.Add($"{label}: {value}");
            }

            if (delays != null && i < delays.Count && delays[i] > 0)
            {
                lines.Add($"Delay (ms): {delays[i]}");
            }
        }
        return string.Join("\n", lines);
    }

    private (System.Collections.Generic.List<ActionModel> actions, System.Collections.Generic.List<int> delays) ParseMultiAction(string text)
    {
        var actions = new System.Collections.Generic.List<ActionModel>();
        var delays = new System.Collections.Generic.List<int>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var typeByLabel = MultiActionLabels.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var colonIdx = line.IndexOf(':');
            if (colonIdx == -1) continue;

            var actionLabel = line.Substring(0, colonIdx).Trim();
            var actionVal = line.Substring(colonIdx + 1).Trim();

            if (actionLabel.Equals("Delay (ms)", StringComparison.OrdinalIgnoreCase))
            {
                // A delay only means something relative to the step before it. Set (not append)
                // the slot for the most recent action so delays[i] always lines up with
                // actions[i] positionally — ExecuteMultiActionAsync indexes them together.
                if (int.TryParse(actionVal, out var delayVal) && actions.Count > 0)
                {
                    while (delays.Count < actions.Count) delays.Add(0);
                    delays[actions.Count - 1] = delayVal;
                }
            }
            else if (typeByLabel.TryGetValue(actionLabel, out var type))
            {
                while (delays.Count < actions.Count) delays.Add(0); // pad a skipped delay before adding the next action
                actions.Add(type switch
                {
                    "hotkey" => new ActionModel { Type = type, Keys = actionVal.Split(',').mapStringList() },
                    "launch_app" => new ActionModel { Type = type, Path = actionVal },
                    "run_command" => new ActionModel { Type = type, Command = actionVal },
                    "media_control" => new ActionModel { Type = type, MediaCommand = actionVal },
                    "open_url" => new ActionModel { Type = type, Url = actionVal },
                    "text_snippet" => new ActionModel { Type = type, Text = actionVal },
                    _ => new ActionModel { Type = type }
                });
            }
        }

        return (actions, delays);
    }

    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeyPanel == null) return; // UI not fully initialized

        // Hide all action panels
        HotkeyPanel.Visibility = Visibility.Collapsed;
        LaunchAppPanel.Visibility = Visibility.Collapsed;
        MediaControlPanel.Visibility = Visibility.Collapsed;
        OpenUrlPanel.Visibility = Visibility.Collapsed;
        RunCommandPanel.Visibility = Visibility.Collapsed;
        TextSnippetPanel.Visibility = Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Collapsed;
        MultiActionPanel.Visibility = Visibility.Collapsed;
        DialPanel.Visibility = Visibility.Collapsed;

        if (ActionTypeCombo.SelectedItem is ComboBoxItem selectedItem)
        {
            var type = selectedItem.Tag?.ToString();
            switch (type)
            {
                case "hotkey": HotkeyPanel.Visibility = Visibility.Visible; break;
                case "launch_app": LaunchAppPanel.Visibility = Visibility.Visible; break;
                case "media_control": MediaControlPanel.Visibility = Visibility.Visible; break;
                case "open_url": OpenUrlPanel.Visibility = Visibility.Visible; break;
                case "run_command": RunCommandPanel.Visibility = Visibility.Visible; break;
                case "text_snippet": TextSnippetPanel.Visibility = Visibility.Visible; break;
                case "open_folder": FolderPanel.Visibility = Visibility.Visible; break;
                case "multi_action": MultiActionPanel.Visibility = Visibility.Visible; break;
                case "dial": DialPanel.Visibility = Visibility.Visible; break;
            }
        }
    }

    private System.Collections.Generic.List<DiscoveredApp> _allApps = new();
    private bool _suppressFilter;

    /// <summary>
    /// IsEditable + TextSearch.TextPath only jumps to the first prefix match — it doesn't hide
    /// non-matching rows, so a long install list is still all there is to scroll through. Reaching
    /// into the ComboBox's template for its real TextBox is the only way to get live filter-as-
    /// you-type without replacing the control with something bigger.
    /// </summary>
    private void HookPathFilterTextBox()
    {
        if (PathComboInput.Template?.FindName("PART_EditableTextBox", PathComboInput) is System.Windows.Controls.TextBox tb)
        {
            tb.TextChanged += (s, e) =>
            {
                if (_suppressFilter) return;
                var query = tb.Text;
                PathComboInput.ItemsSource = string.IsNullOrWhiteSpace(query)
                    ? _allApps
                    : _allApps.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                        || a.ExePath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!PathComboInput.IsDropDownOpen) PathComboInput.IsDropDownOpen = true;
                // Filtering ItemsSource resets the TextBox — put the user's typed text back without re-triggering this handler.
                _suppressFilter = true;
                tb.Text = query;
                tb.CaretIndex = query.Length;
                _suppressFilter = false;
            };
        }
    }

    private void PathComboInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PathComboInput.SelectedItem is DiscoveredApp app)
        {
            PathComboInput.Text = app.ExePath;
            TryAutoExtractAppIcon(app.ExePath);
        }
    }

    // launch_app never had an icon source of its own — picking an app silently left Icon
    // unset unless the user separately browsed for an image. Mirror the favicon flow: extract
    // the exe's real icon and store it, same as FetchFavicon does for open_url.
    private void TryAutoExtractAppIcon(string exePath)
    {
        var hash = ProfileStoreService.ExtractAndSaveIcon(exePath);
        if (hash != null)
        {
            IconPathText.Text = hash;
        }
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            PathComboInput.Text = dlg.FileName;
            TryAutoExtractAppIcon(dlg.FileName);
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dlg.ShowDialog() == true)
        {
            IconPathText.Text = dlg.FileName;
        }
    }

    // --------------- FAVICON PREVIEW ---------------

    private void UrlInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        _faviconCts?.Cancel();
        _faviconCts = new CancellationTokenSource();
        var token = _faviconCts.Token;
        var url   = UrlInput.Text.Trim();

        // Debounce: wait 700ms before firing the fetch
        _ = System.Threading.Tasks.Task.Delay(700, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.BeginInvoke(new Action(async () => await FetchFavicon(url, token)));
        }, token);
    }

    private async System.Threading.Tasks.Task FetchFavicon(string url, CancellationToken token)
    {
        // Extract domain from the typed URL
        string domain;
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;
            domain = new Uri(url).Host;
        }
        catch
        {
            FaviconPreview.Opacity = 0;
            FaviconStatusText.Text = "";
            return;
        }

        FaviconStatusText.Text = "Fetching icon…";
        FaviconPreview.Opacity = 0;

        try
        {
            // sz=256 asks Google for its largest cached favicon variant — verified this actually
            // returns a real higher-res image (not just server-side upscaling of a 32px source),
            // so the 144x144 PNG we save from it looks sharp instead of blurry.
            var faviconUrl = $"https://www.google.com/s2/favicons?sz=256&domain={domain}";
            var bytes = await _faviconHttpClient.GetByteArrayAsync(faviconUrl, token);

            if (token.IsCancellationRequested) return;

            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            FaviconPreview.Source = bmp;
            FaviconStatusText.Text = domain;

            // Persist it as the button's icon, not just a preview (Save() only reads IconPathText.Text).
            IconPathText.Text = ProfileStoreService.SaveIconFromBytes(bytes);

            // Fade in the preview
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
            FaviconPreview.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        catch (OperationCanceledException) { /* user kept typing */ }
        catch
        {
            FaviconPreview.Opacity = 0;
            FaviconStatusText.Text = "Could not load icon";
        }
    }

    private void ToggleBuiltinIcons_Click(object sender, RoutedEventArgs e)
    {
        BuiltinIconsDrawer.Visibility = BuiltinIconsDrawer.Visibility == Visibility.Visible 
            ? Visibility.Collapsed 
            : Visibility.Visible;
    }

    private void LoadBuiltinIcons()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.png").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var img = new System.Windows.Controls.Image
            {
                Width = 24,
                Height = 24,
                Source = new BitmapImage(new Uri(file))
            };
            var btn = new System.Windows.Controls.Button
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(3),
                Content = img,
                Background = ThemeManager.Brush("Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(1),
                ToolTip = name,
            };
            btn.Click += (s, e) =>
            {
                IconPathText.Text = "builtin:" + name;
                BuiltinIconsDrawer.Visibility = Visibility.Collapsed;
            };
            IconWrapPanel.Children.Add(btn);
        }
    }

    private void EnterFolder_Click(object sender, RoutedEventArgs e)
    {
        EnterFolderRequested = true;
        SaveButton_Click(sender, e);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var activeItem = ActionTypeCombo.SelectedItem as ComboBoxItem;
        var actionType = activeItem?.Tag?.ToString() ?? "hotkey";

        var action = new ActionModel { Type = actionType };
        switch (actionType)
        {
            case "hotkey":
                action.Keys = HotkeyInput.Text.Split(',').mapStringList();
                break;
            case "launch_app":
                action.Path = string.IsNullOrEmpty(PathComboInput.Text) 
                    ? (PathComboInput.SelectedItem as DiscoveredApp)?.ExePath 
                    : PathComboInput.Text;
                break;
            case "media_control":
                action.MediaCommand = (MediaCommandCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PlayPause";
                break;
            case "open_url":
                action.Url = UrlInput.Text;
                break;
            case "run_command":
                action.Command = CommandInput.Text;
                break;
            case "text_snippet":
                action.Text = SnippetTextInput.Text;
                break;
            case "open_folder":
                action.TargetFolderId = string.IsNullOrEmpty(TargetFolderIdInput.Text)
                    ? $"f_{Guid.NewGuid().ToString().Substring(0, 8)}"
                    : TargetFolderIdInput.Text;
                break;
            case "multi_action":
                var parsed = ParseMultiAction(MultiActionInput.Text);
                action.Actions = parsed.actions;
                action.Delays = parsed.delays;
                break;
            case "dial":
                action.DialTarget = (DialTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "volume";
                break;
        }

        Button.Label = LabelInput.Text;
        Button.Icon = string.IsNullOrEmpty(IconPathText.Text) ? null : IconPathText.Text;
        Button.Action = action;

        // Long-press: one parsed step = that action directly; several = a multi_action wrapper.
        var (lpActions, lpDelays) = ParseMultiAction(LongPressInput.Text);
        Button.LongPressAction = lpActions.Count switch
        {
            0 => null,
            1 => lpActions[0],
            _ => new ActionModel { Type = "multi_action", Actions = lpActions, Delays = lpDelays }
        };

        DialogResult = true;
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        IsDeleted = true;
        DialogResult = true;
        Close();
    }
}

public static class StringListExtensions
{
    public static System.Collections.Generic.List<string> mapStringList(this string[] source)
    {
        return source.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
    }
}
