using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CrossDeckHost;
using CrossDeckHost.Actions;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Controls;

/// <summary>
/// Full action-type + parameter editor for a single ActionModel — the same rich picker set
/// (app combo, media dropdown, url+favicon, dial target, etc.) used for both the main tap
/// action and the long-press action, so long-press isn't limited to a generic type+value row.
/// </summary>
public partial class ActionConfigControl : System.Windows.Controls.UserControl
{
    private System.Collections.Generic.List<DiscoveredApp> _allApps = new();
    private bool _suppressFilter;
    private CancellationTokenSource? _faviconCts;
    private static readonly System.Net.Http.HttpClient _faviconHttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };

    // Only a brand-new (blank-label) instance gets live autofill from the action's parameters —
    // an existing custom label is never touched, and typing in the field stops it permanently.
    // Same rule ButtonEditorWindow applies to the button's own top-level label.
    private bool _labelUserEdited;
    private bool _suppressLabelEdit;

    private static readonly System.Collections.Generic.Dictionary<string, string> MediaCommandLabels = new()
    {
        ["PlayPause"] = "Play / Pause",
        ["NextTrack"] = "Next Track",
        ["PrevTrack"] = "Previous Track",
        ["VolumeUp"] = "Volume Up",
        ["VolumeDown"] = "Volume Down",
        ["VolumeMute"] = "Mute"
    };

    /// <summary>Best-guess label derived from the action being configured — shared by the button's
    /// own top-level label (ButtonEditorWindow) and every instance of this control's own
    /// ActionLabelText (main action has none, long-press/chain cards do).</summary>
    public static string? SuggestLabel(ActionModel action) => action.Type switch
    {
        "hotkey" => action.Keys is { Count: > 0 } ? string.Join("+", action.Keys) : null,
        "launch_app" => string.IsNullOrWhiteSpace(action.Path)
            ? null
            : System.IO.Path.GetFileNameWithoutExtension(action.Path),
        "media_control" => action.MediaCommand != null ? MediaCommandLabels.GetValueOrDefault(action.MediaCommand, action.MediaCommand) : null,
        "open_url" => string.IsNullOrWhiteSpace(action.Url)
            ? null
            : action.Url.Trim().Replace("https://", "").Replace("http://", "").Split('/')[0],
        "run_command" => string.IsNullOrWhiteSpace(action.Command)
            ? null
            : action.Command.Trim().Split(' ')[0].Split('\\', '/')[^1],
        "text_snippet" => string.IsNullOrWhiteSpace(action.Text) ? null : action.Text.Trim()[..Math.Min(20, action.Text.Trim().Length)],
        "dial" => action.DialTarget == "brightness" ? "Brightness" : "Volume",
        "open_folder" => "Open Folder",
        "macro" => "Macro",
        _ => null
    };

    /// <summary>Raised with the extracted/fetched icon hash — only fires when ExtractIconOnSelect is true.</summary>
    public event Action<string>? IconExtracted;

    /// <summary>Raised when the folder-panel's "Enter Folder in Editor Grid" shortcut is clicked.</summary>
    public event Action? EnterFolderShortcutClicked;

    /// <summary>Raised whenever the Action Type dropdown selection changes, with the new type's tag.</summary>
    public event Action<string>? ActionTypeChanged;

    /// <summary>Raised whenever the action's type or parameters change — lets the label autofill live.</summary>
    public event Action? ActionChanged;

    /// <summary>Only the primary action instance should overwrite the button's shared icon and persist fetched icons to disk.</summary>
    public bool ExtractIconOnSelect { get; set; }

    private bool _isLongPress;
    /// <summary>True only for the long-press slot's own instance — changes multi_action's editor from
    /// the light step-chain (MultiActionStepList) to the rich per-sub-button picker (RichStepList).</summary>
    public bool IsLongPress
    {
        get => _isLongPress;
        set { _isLongPress = value; UpdateActionTypeAvailability(); }
    }

    private bool _allowChaining = true;
    /// <summary>False for a rich sub-button's own nested instance — hides Multiple Actions/Macro from
    /// its Action Type dropdown so a sub-button can never itself be a chain (no nesting).</summary>
    public bool AllowChaining
    {
        get => _allowChaining;
        set { _allowChaining = value; UpdateActionTypeAvailability(); }
    }

    /// <summary>
    /// Long-press (top-level slot or a chain's own sub-button card) never offers Multiple
    /// Actions/Open Folder directly — a long-press chain is only ever built via "+ Add Another
    /// Action" (never nested inside itself either, no chain-of-chains), and folder navigation is
    /// client-side-only paging that doesn't make sense as something you hold a button to run.
    /// Macro stays available at the top-level long-press slot — only chain sub-buttons exclude it,
    /// same no-nesting rule as Multiple Actions.
    /// </summary>
    private void UpdateActionTypeAvailability()
    {
        if (ActionTypeCombo == null) return; // UI not fully initialized
        foreach (ComboBoxItem item in ActionTypeCombo.Items)
        {
            bool hide = item.Tag?.ToString() switch
            {
                "multi_action" => !_allowChaining || _isLongPress,
                "macro" => !_allowChaining,
                "open_folder" => _isLongPress || !_allowChaining,
                _ => false
            };
            item.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Only the long-press instance offers its own icon — the main action's icon lives on the button itself.</summary>
    public bool ShowIconPicker
    {
        get => IconSection.Visibility == Visibility.Visible;
        set
        {
            IconSection.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) LoadActionBuiltinIcons();
        }
    }

    public System.Collections.Generic.List<DiscoveredApp> AppList
    {
        get => _allApps;
        set { _allApps = value; PathComboInput.ItemsSource = _allApps; RichStepList.AppList = value; }
    }

    public ActionConfigControl()
    {
        InitializeComponent();
        Loaded += (s, e) => HookPathFilterTextBox();
        // Add Step, Remove, reorder, and the macro recorder all mutate this collection directly —
        // none of them go through a field-level TextChanged/SelectionChanged handler, so without
        // this, Save's enabled state and its hint text go stale the moment a chain's step count
        // changes (confirmed: hint kept saying "add at least one step" with 3 steps already listed).
        MultiActionStepList.Steps.CollectionChanged += (s, e) => ActionChanged?.Invoke();
        ActionChanged += UpdateSuggestedLabel;
    }

    /// <summary>Live label autofill for this instance's own ActionLabelText — only relevant when
    /// ShowIconPicker is true (the field only exists then); a no-op otherwise.</summary>
    private void UpdateSuggestedLabel()
    {
        if (!ShowIconPicker || _labelUserEdited) return;
        var suggested = SuggestLabel(GetAction());
        if (suggested == null) return;
        _suppressLabelEdit = true;
        ActionLabelText.Text = suggested;
        _suppressLabelEdit = false;
    }

    private void ActionLabelText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressLabelEdit) _labelUserEdited = true;
        ActionChanged?.Invoke();
    }

    public void ShowEnterFolderShortcut() => EnterFolderButton.Visibility = Visibility.Visible;

    public void Dispose() => MultiActionStepList.Dispose();

    public void SetAction(ActionModel action)
    {
        // mouse_click only exists as a recorded step inside a chain — there's no "Mouse Click"
        // entry in ActionTypeCombo, so a bare mouse_click action (the shape a single recorded
        // click long-press used to be saved as, before long-press got this same editor) would
        // match no dropdown item and silently revert to a blank hotkey on the next save. Wrap it
        // as a one-step chain instead so it round-trips correctly.
        if (action.Type == "mouse_click")
        {
            SetAction(new ActionModel
            {
                Type = "multi_action",
                Actions = new System.Collections.Generic.List<ActionModel> { action },
                Delays = new System.Collections.Generic.List<int> { 0 }
            });
            return;
        }

        SetActionTypeSelection(action.Type);

        ActionIconText.Text = action.Icon ?? "";
        _suppressLabelEdit = true;
        ActionLabelText.Text = action.Label ?? "";
        _suppressLabelEdit = false;
        _labelUserEdited = !string.IsNullOrWhiteSpace(action.Label);
        HotkeyInput.Text = action.Keys != null ? string.Join(",", action.Keys) : "";

        if (action.Type == "launch_app")
        {
            var matched = _allApps.FirstOrDefault(a => a.ExePath.Equals(action.Path, StringComparison.OrdinalIgnoreCase));
            PathComboInput.SelectedItem = matched;
            if (matched == null) PathComboInput.Text = action.Path ?? "";
        }

        string mediaCmd = action.MediaCommand ?? "PlayPause";
        foreach (ComboBoxItem item in MediaCommandCombo.Items)
        {
            if (item.Tag?.ToString() == mediaCmd) { MediaCommandCombo.SelectedItem = item; break; }
        }

        UrlInput.Text = action.Url ?? "";
        CommandInput.Text = action.Command ?? "";
        SnippetTextInput.Text = action.Text ?? "";
        TargetFolderIdInput.Text = action.TargetFolderId ?? "";

        MultiActionStepList.Steps.Clear();
        if (action.Type == "multi_action" && IsLongPress)
        {
            RichStepList.SetSubActions(action.Actions ?? new System.Collections.Generic.List<ActionModel>());
        }
        else
        {
            RichStepList.SetSubActions(new System.Collections.Generic.List<ActionModel>());
            if (action.Actions != null)
            {
                for (int i = 0; i < action.Actions.Count; i++)
                {
                    MultiActionStepList.Steps.Add(new ActionStep
                    {
                        Action = action.Actions[i],
                        DelayAfterMs = action.Delays != null && i < action.Delays.Count ? action.Delays[i] : 0
                    });
                }
            }
        }

        string dialTgt = action.DialTarget ?? "volume";
        foreach (ComboBoxItem item in DialTargetCombo.Items)
        {
            if (item.Tag?.ToString() == dialTgt) { DialTargetCombo.SelectedItem = item; break; }
        }
    }

    public ActionModel GetAction()
    {
        var activeItem = ActionTypeCombo.SelectedItem as ComboBoxItem;
        var actionType = activeItem?.Tag?.ToString() ?? "hotkey";

        var action = new ActionModel
        {
            Type = actionType,
            Icon = string.IsNullOrEmpty(ActionIconText.Text) ? null : ActionIconText.Text,
            Label = string.IsNullOrEmpty(ActionLabelText.Text) ? null : ActionLabelText.Text
        };
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
                if (IsLongPress)
                {
                    action.Actions = RichStepList.GetSubActions();
                }
                else
                {
                    action.Actions = MultiActionStepList.Steps.Select(s => s.Action).ToList();
                    action.Delays = MultiActionStepList.Steps.Select(s => s.DelayAfterMs).ToList();
                }
                break;
            case "macro":
                action.Actions = MultiActionStepList.Steps.Select(s => s.Action).ToList();
                action.Delays = MultiActionStepList.Steps.Select(s => s.DelayAfterMs).ToList();
                break;
            case "dial":
                action.DialTarget = (DialTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "volume";
                break;
        }
        return action;
    }

    private void ParamField_Changed(object sender, TextChangedEventArgs e) => ActionChanged?.Invoke();

    private void ParamField_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActionChanged?.Invoke();

    private void BrowseActionIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Image Files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ActionIconText.Text = ProfileStoreService.SaveIconFromBytes(File.ReadAllBytes(dlg.FileName));
            ActionChanged?.Invoke();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Couldn't load that image: {ex.Message}", "Icon Upload Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleActionBuiltinIcons_Click(object sender, RoutedEventArgs e) =>
        ActionBuiltinIconsDrawer.Visibility = ActionBuiltinIconsDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void LoadActionBuiltinIcons()
    {
        if (ActionIconWrapPanel.Children.Count > 0) return;
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.png").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var img = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(file)) };
            var btn = new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(4),
                Margin = new Thickness(3),
                Content = img,
                Background = ThemeManager.Brush("Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(1),
                ToolTip = name
            };
            btn.Click += (s, e) =>
            {
                ActionIconText.Text = "builtin:" + name;
                ActionBuiltinIconsDrawer.Visibility = Visibility.Collapsed;
                ActionChanged?.Invoke();
            };
            ActionIconWrapPanel.Children.Add(btn);
        }
    }

    private void SetActionTypeSelection(string type)
    {
        foreach (ComboBoxItem item in ActionTypeCombo.Items)
        {
            if (item.Tag?.ToString() == type) { ActionTypeCombo.SelectedItem = item; break; }
        }
    }

    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeyPanel == null) return; // UI not fully initialized

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
            switch (selectedItem.Tag?.ToString())
            {
                case "hotkey": HotkeyPanel.Visibility = Visibility.Visible; break;
                case "launch_app": LaunchAppPanel.Visibility = Visibility.Visible; break;
                case "media_control": MediaControlPanel.Visibility = Visibility.Visible; break;
                case "open_url": OpenUrlPanel.Visibility = Visibility.Visible; break;
                case "run_command": RunCommandPanel.Visibility = Visibility.Visible; break;
                case "text_snippet": TextSnippetPanel.Visibility = Visibility.Visible; break;
                case "open_folder": FolderPanel.Visibility = Visibility.Visible; break;
                case "multi_action":
                    MultiActionPanel.Visibility = Visibility.Visible;
                    MultiActionStepList.Visibility = IsLongPress ? Visibility.Collapsed : Visibility.Visible;
                    MultiActionStepList.IsMacro = false;
                    RichStepList.Visibility = IsLongPress ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "macro":
                    MultiActionPanel.Visibility = Visibility.Visible;
                    MultiActionStepList.Visibility = Visibility.Visible;
                    MultiActionStepList.IsMacro = true;
                    RichStepList.Visibility = Visibility.Collapsed;
                    break;
                case "dial": DialPanel.Visibility = Visibility.Visible; break;
            }
            var tag = selectedItem.Tag?.ToString() ?? "";
            ActionIconOnlySection.Visibility = (tag == "multi_action" || tag == "macro") ? Visibility.Collapsed : Visibility.Visible;
            ActionTypeChanged?.Invoke(tag);
        }
        ActionChanged?.Invoke();
    }

    /// <summary>
    /// IsEditable + TextSearch.TextPath only jumps to the first prefix match — it doesn't hide
    /// non-matching rows, so a long install list is still all there is to scroll through. Reaching
    /// into the ComboBox's template for its real TextBox is the only way to get live filter-as-
    /// you-type without replacing the control with something bigger.
    /// </summary>
    private void HookPathFilterTextBox()
    {
        if (PathComboInput.Template?.FindName("PART_EditableTextBox", PathComboInput) is TextBox tb)
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
                ActionChanged?.Invoke();
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
        ActionChanged?.Invoke();
    }

    private void AppRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DiscoveredApp app } && app.Icon == null)
        {
            app.Icon = AppDiscovery.GetOrLoadIcon(app.ExePath);
        }
    }

    private void TryAutoExtractAppIcon(string exePath)
    {
        if (!ExtractIconOnSelect) return;
        var hash = ProfileStoreService.ExtractAndSaveIcon(exePath);
        if (hash != null) ApplyExtractedIcon(hash);
    }

    /// <summary>Routes a freshly-fetched/extracted icon hash to wherever this instance's icon
    /// actually lives. Every instance except the plain main action owns its own Action.Icon —
    /// written to ActionIconText even while IconSection is hidden (the long-press top-level slot),
    /// since GetAction() reads it regardless of visibility. Always also raised externally for the
    /// main action, whose icon lives on the button itself (ButtonEditorWindow.IconPathText).</summary>
    private void ApplyExtractedIcon(string hash)
    {
        if (IsLongPress || !AllowChaining)
        {
            ActionIconText.Text = hash;
            ActionChanged?.Invoke();
        }
        IconExtracted?.Invoke(hash);
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
            ActionChanged?.Invoke();
        }
    }

    private void UrlInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        ActionChanged?.Invoke();
        _faviconCts?.Cancel();
        _faviconCts = new CancellationTokenSource();
        var token = _faviconCts.Token;
        var url = UrlInput.Text.Trim();

        // Debounce: wait 700ms before firing the fetch
        _ = System.Threading.Tasks.Task.Delay(700, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.BeginInvoke(new Action(async () => await FetchFavicon(url, token)));
        }, token);
    }

    private async System.Threading.Tasks.Task FetchFavicon(string url, CancellationToken token)
    {
        string domain;
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
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

            if (ExtractIconOnSelect)
            {
                var hash = ProfileStoreService.SaveIconFromBytes(bytes);
                ApplyExtractedIcon(hash);
            }

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

    private void EnterFolder_Click(object sender, RoutedEventArgs e) => EnterFolderShortcutClicked?.Invoke();
}
