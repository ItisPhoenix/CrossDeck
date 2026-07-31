using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
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
    // The app PathComboInput.Text's friendly name currently refers to, if any — GetAction() uses
    // its real ExePath instead of the displayed name. Cleared whenever the user types (a custom
    // path), so a hand-edited path never gets silently replaced by a stale selection's ExePath.
    private DiscoveredApp? _pathSelectedApp;
    // Real backing value for this action's own icon — ActionIconText only shows FriendlyIconLabel(this).
    private string _actionIconRef = "";
    private CancellationTokenSource? _faviconCts;
    private static readonly System.Net.Http.HttpClient _faviconHttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };

    // Only a brand-new (blank-label) instance gets live autofill from the action's parameters —
    // an existing custom label is never touched, and typing in the field stops it permanently.
    // Same rule ButtonEditorWindow applies to the button's own top-level label.
    private bool _labelUserEdited;
    private bool _suppressLabelEdit;

    // Working copy of a dial's stack layers while StackDialCheck is on — each layer is its own
    // dial config (target/process/step-keys/label). Edited in place by BuildDialLayerCard's
    // per-row handlers; GetAction() reads this list directly when stacked.
    private readonly System.Collections.Generic.List<ActionModel> _dialStackLayers = new();

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
        "dial" => action.Actions is { Count: > 0 } ? "Dial Stack" : action.DialTarget switch
        {
            "brightness" => "Brightness",
            "mic" => "Microphone",
            "app_volume" => action.DialProcess ?? "App Volume",
            "keystroke_step" => "Keystroke Step",
            _ => "Volume"
        },
        "open_folder" => "Open Folder",
        "macro" => "Macro",
        _ => null
    };

    /// <summary>Human-readable stand-in for an icon reference — raw values are either a
    /// "builtin:name" tag or a content hash, neither meaningful to show as literal text.</summary>
    public static string FriendlyIconLabel(string? iconRef) => iconRef switch
    {
        null or "" => "No icon set",
        var s when s.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase) => "Built-in: " + s["builtin:".Length..],
        _ => "Custom icon"
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
                "button_group" => !_allowChaining || _isLongPress,
                "macro" => !_allowChaining,
                "open_folder" => _isLongPress || !_allowChaining,
                _ => false
            };
            item.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>False for rich sub-button cards — matches the main button's blank-until-set icon box.</summary>
    public bool ShowIconGlyphFallback { get; set; } = true;

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

    private string? _forcedType;

    // Two-way toggle (not a latch) since the docked panel reuses one instance across buttons.
    public bool ForceDialMode
    {
        set
        {
            _forcedType = value ? "dial" : null;
            ActionTypeCombo.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            if (value)
            {
                CollapseAllTypePanels();
                DialPanel.Visibility = Visibility.Visible;
                DialProcessCombo.ItemsSource = DialController.GetAudioMixerSnapshot().Select(a => a.ProcessName).ToList();
            }
            else
            {
                DialPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    // Shared by ForceDialMode and ActionTypeCombo_SelectionChanged, which bypasses each other.
    private void CollapseAllTypePanels()
    {
        HotkeyPanel.Visibility = Visibility.Collapsed;
        LaunchAppPanel.Visibility = Visibility.Collapsed;
        MediaControlPanel.Visibility = Visibility.Collapsed;
        OpenUrlPanel.Visibility = Visibility.Collapsed;
        RunCommandPanel.Visibility = Visibility.Collapsed;
        TextSnippetPanel.Visibility = Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Collapsed;
        MultiActionPanel.Visibility = Visibility.Collapsed;
        DialPanel.Visibility = Visibility.Collapsed;
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
        MultiActionStepList.ActionChanged += () => ActionChanged?.Invoke();
        // Was never wired — rich sub-button edits never reached the docked panel's auto-apply.
        RichStepList.ActionChanged += () => ActionChanged?.Invoke();
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

        SetActionIconRef(action.Icon);
        _suppressLabelEdit = true;
        ActionLabelText.Text = action.Label ?? "";
        _suppressLabelEdit = false;
        _labelUserEdited = !string.IsNullOrWhiteSpace(action.Label);
        HotkeyInput.Text = action.Keys != null ? string.Join(",", action.Keys) : "";

        if (action.Type == "launch_app")
        {
            var matched = _allApps.FirstOrDefault(a => a.ExePath.Equals(action.Path, StringComparison.OrdinalIgnoreCase));
            _pathSelectedApp = matched;
            PathComboInput.SelectedItem = matched;
            PathComboInput.Text = matched?.Name ?? action.Path ?? "";
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
        if ((action.Type == "multi_action" && IsLongPress) || action.Type == "button_group")
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
            MultiActionStepList.MacroSpeed = action.MacroSpeed ?? 1.0;
        }

        string dialTgt = action.DialTarget ?? "volume";
        foreach (ComboBoxItem item in DialTargetCombo.Items)
        {
            if (item.Tag?.ToString() == dialTgt) { DialTargetCombo.SelectedItem = item; break; }
        }
        DialProcessCombo.Text = action.DialProcess ?? "";
        StepUpKeysInput.Text = action.DialStepUpKeys != null ? string.Join(",", action.DialStepUpKeys) : "";
        StepDownKeysInput.Text = action.DialStepDownKeys != null ? string.Join(",", action.DialStepDownKeys) : "";
        UpdateDialTargetPanels(dialTgt);

        bool isStack = action.Type == "dial" && action.Actions is { Count: > 0 };
        StackDialCheck.IsChecked = isStack;
        _dialStackLayers.Clear();
        if (isStack) _dialStackLayers.AddRange(action.Actions!.Select(CloneDialLayer));
        UpdateDialStackVisibility(isStack);
        if (isStack) RebuildDialStackUi();
    }

    private static ActionModel CloneDialLayer(ActionModel a) => new ActionModel
    {
        Type = "dial",
        DialTarget = a.DialTarget,
        DialProcess = a.DialProcess,
        DialStepUpKeys = a.DialStepUpKeys,
        DialStepDownKeys = a.DialStepDownKeys,
        Label = a.Label
    };

    public ActionModel GetAction()
    {
        var activeItem = ActionTypeCombo.SelectedItem as ComboBoxItem;
        var actionType = _forcedType ?? activeItem?.Tag?.ToString() ?? "hotkey";

        var action = new ActionModel
        {
            Type = actionType,
            Icon = string.IsNullOrEmpty(_actionIconRef) ? null : _actionIconRef,
            Label = string.IsNullOrEmpty(ActionLabelText.Text) ? null : ActionLabelText.Text
        };
        switch (actionType)
        {
            case "hotkey":
                action.Keys = HotkeyInput.Text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
                break;
            case "launch_app":
                action.Path = _pathSelectedApp?.ExePath
                    ?? (string.IsNullOrEmpty(PathComboInput.Text) ? null : PathComboInput.Text);
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
            case "button_group":
                action.Actions = RichStepList.GetSubActions();
                break;
            case "macro":
                action.Actions = MultiActionStepList.Steps.Select(s => s.Action).ToList();
                action.Delays = MultiActionStepList.Steps.Select(s => s.DelayAfterMs).ToList();
                action.MacroSpeed = MultiActionStepList.MacroSpeed;
                break;
            case "dial":
                if (StackDialCheck.IsChecked == true && _dialStackLayers.Count > 0)
                {
                    // The stack container itself carries no single target — whichever layer is
                    // active resolves it (ActionModel.ResolveDialLayer), same on both host and client.
                    action.Actions = _dialStackLayers;
                }
                else
                {
                    var dialTarget = (DialTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "volume";
                    action.DialTarget = dialTarget;
                    action.DialProcess = dialTarget == "app_volume" && !string.IsNullOrWhiteSpace(DialProcessCombo.Text)
                        ? DialProcessCombo.Text.Trim() : null;
                    action.DialStepUpKeys = dialTarget == "keystroke_step" ? StepUpKeysInput.Text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList() : null;
                    action.DialStepDownKeys = dialTarget == "keystroke_step" ? StepDownKeysInput.Text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList() : null;
                }
                break;
        }
        return action;
    }

    private void ParamField_Changed(object sender, TextChangedEventArgs e) => ActionChanged?.Invoke();

    private void ParamField_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActionChanged?.Invoke();

    private void DialTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DialTargetCombo.SelectedItem is ComboBoxItem item)
            UpdateDialTargetPanels(item.Tag?.ToString() ?? "volume");
        ActionChanged?.Invoke();
    }

    /// <summary>Only App Volume needs the process picker; only unbound App Volume shows the
    /// "opens a live mixer" hint (a bound dial behaves like any other single-value dial); only
    /// Keystroke Step needs the up/down hotkey fields.</summary>
    private void UpdateDialTargetPanels(string dialTarget)
    {
        bool isAppVolume = dialTarget == "app_volume";
        AppVolumeProcessPanel.Visibility = isAppVolume ? Visibility.Visible : Visibility.Collapsed;
        DialTargetHintText.Visibility = (isAppVolume && string.IsNullOrWhiteSpace(DialProcessCombo.Text)) ? Visibility.Visible : Visibility.Collapsed;
        StepKeysPanel.Visibility = dialTarget == "keystroke_step" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StackDialCheck_Changed(object sender, RoutedEventArgs e)
    {
        bool isStack = StackDialCheck.IsChecked == true;
        if (isStack && _dialStackLayers.Count == 0)
        {
            // Seed the stack with whatever's currently configured in the single-target view, so
            // checking the box doesn't silently discard it.
            var target = (DialTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "volume";
            _dialStackLayers.Add(new ActionModel
            {
                Type = "dial",
                DialTarget = target,
                DialProcess = target == "app_volume" && !string.IsNullOrWhiteSpace(DialProcessCombo.Text) ? DialProcessCombo.Text.Trim() : null,
                DialStepUpKeys = target == "keystroke_step" ? StepUpKeysInput.Text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList() : null,
                DialStepDownKeys = target == "keystroke_step" ? StepDownKeysInput.Text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList() : null
            });
        }
        UpdateDialStackVisibility(isStack);
        if (isStack) RebuildDialStackUi();
        ActionChanged?.Invoke();
    }

    /// <summary>Whether this instance's own dial is currently stacked — used by the docked panel
    /// to hide the separate Tap Action section, since a stacked dial's tap always cycles layers.</summary>
    public bool IsDialStack => StackDialCheck.IsChecked == true;

    private void UpdateDialStackVisibility(bool isStack)
    {
        DialSingleTargetPanel.Visibility = isStack ? Visibility.Collapsed : Visibility.Visible;
        DialStackPanel.Visibility = isStack ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddDialLayer_Click(object sender, RoutedEventArgs e)
    {
        _dialStackLayers.Add(new ActionModel { Type = "dial", DialTarget = "volume" });
        RebuildDialStackUi();
        ActionChanged?.Invoke();
    }

    private void RebuildDialStackUi()
    {
        DialStackLayersHost.Children.Clear();
        for (int i = 0; i < _dialStackLayers.Count; i++)
            DialStackLayersHost.Children.Add(BuildDialLayerCard(i));
    }

    private static readonly (string Tag, string Label)[] DialLayerTargets =
    {
        ("volume", "Master Volume"), ("mic", "Microphone Volume"), ("brightness", "Brightness"),
        ("app_volume", "App Volume"), ("keystroke_step", "Keystroke Step")
    };

    /// <summary>One stack-layer card — target picker + whichever param fields that target needs,
    /// editing _dialStackLayers[index] in place. index is a method parameter, not a captured
    /// loop variable, so each card's closures stay correctly bound to its own layer even after
    /// cards are added/removed and the list re-rendered.</summary>
    private Border BuildDialLayerCard(int index)
    {
        var layer = _dialStackLayers[index];
        var stack = new StackPanel();
        var card = new Border
        {
            Background = ThemeManager.Brush("Brush.Void"),
            BorderBrush = ThemeManager.Brush("Brush.Hairline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = stack
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };

        var iconBtn = new Button { Width = 32, Height = 32, Padding = new Thickness(2), Background = ThemeManager.Brush("Brush.Void"), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Choose a built-in icon for this layer" };
        void SetLayerIconContent()
        {
            var path = ProfileStoreService.ResolveIconFilePath(layer.Icon);
            if (path != null) { iconBtn.Content = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(path)) }; return; }
            iconBtn.Content = new TextBlock { Text = "+", FontSize = 14, Foreground = ThemeManager.Brush("Brush.Mist"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        }
        SetLayerIconContent();
        iconBtn.Click += (s, e) => ShowLayerIconPopup(iconBtn, name => { layer.Icon = "builtin:" + name; SetLayerIconContent(); ActionChanged?.Invoke(); });
        iconPanel.Children.Add(iconBtn);

        var browseIconBtn = new Button { Content = "📂", Width = 32, Height = 32, Padding = new Thickness(2), Style = System.Windows.Application.Current.Resources["StandardButton"] as Style, ToolTip = "Upload a custom image for this layer" };
        browseIconBtn.Click += (s, e) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Image Files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                layer.Icon = ProfileStoreService.SaveIconFromBytes(File.ReadAllBytes(dlg.FileName));
                SetLayerIconContent();
                ActionChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Couldn't load that image: {ex.Message}", "Icon Upload Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        iconPanel.Children.Add(browseIconBtn);

        Grid.SetColumn(iconPanel, 0);
        header.Children.Add(iconPanel);

        var headerText = new TextBlock
        {
            Text = $"Layer {index + 1}",
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeManager.Brush("Brush.Paper"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerText);

        var removeBtn = new Button { Content = "✕", Style = System.Windows.Application.Current.Resources["StandardButton"] as Style, Padding = new Thickness(6, 3, 6, 3) };
        Grid.SetColumn(removeBtn, 2);
        removeBtn.Click += (s, e) => { _dialStackLayers.RemoveAt(index); RebuildDialStackUi(); ActionChanged?.Invoke(); };
        header.Children.Add(removeBtn);
        stack.Children.Add(header);

        var labelHint = new TextBlock { Text = "Label shown while this layer is active", FontSize = 11, Foreground = ThemeManager.Brush("Brush.Mist"), Margin = new Thickness(0, 6, 0, 3) };
        var labelBox = new TextBox { Padding = new Thickness(6, 4, 6, 4), Text = layer.Label ?? "" };
        labelBox.TextChanged += (s, e) => { layer.Label = string.IsNullOrWhiteSpace(labelBox.Text) ? null : labelBox.Text; ActionChanged?.Invoke(); };
        stack.Children.Add(labelHint);
        stack.Children.Add(labelBox);

        var targetCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(6, 4, 6, 4) };
        foreach (var (tag, label) in DialLayerTargets) targetCombo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        targetCombo.SelectedItem = targetCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == (layer.DialTarget ?? "volume"));
        stack.Children.Add(targetCombo);

        var processHint = new TextBlock { Text = "App process — pick one currently playing audio, or type a name (blank = live mixer)", FontSize = 11, Foreground = ThemeManager.Brush("Brush.Mist"), Margin = new Thickness(0, 6, 0, 3) };
        var processBox = new ComboBox
        {
            Padding = new Thickness(6, 4, 6, 4),
            IsEditable = true,
            Text = layer.DialProcess ?? "",
            ItemsSource = DialController.GetAudioMixerSnapshot().Select(a => a.ProcessName).ToList()
        };
        var upHint = new TextBlock { Text = "Step up keys", FontSize = 11, Foreground = ThemeManager.Brush("Brush.Mist"), Margin = new Thickness(0, 6, 0, 3) };
        var upKeysBox = new TextBox { Padding = new Thickness(6, 4, 6, 4), Text = layer.DialStepUpKeys != null ? string.Join(",", layer.DialStepUpKeys) : "" };
        var downHint = new TextBlock { Text = "Step down keys", FontSize = 11, Foreground = ThemeManager.Brush("Brush.Mist"), Margin = new Thickness(0, 6, 0, 3) };
        var downKeysBox = new TextBox { Padding = new Thickness(6, 4, 6, 4), Text = layer.DialStepDownKeys != null ? string.Join(",", layer.DialStepDownKeys) : "" };

        void UpdateFieldVisibility()
        {
            var t = layer.DialTarget ?? "volume";
            processHint.Visibility = processBox.Visibility = t == "app_volume" ? Visibility.Visible : Visibility.Collapsed;
            upHint.Visibility = upKeysBox.Visibility = downHint.Visibility = downKeysBox.Visibility = t == "keystroke_step" ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateFieldVisibility();

        targetCombo.SelectionChanged += (s, e) =>
        {
            layer.DialTarget = (targetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "volume";
            UpdateFieldVisibility();
            ActionChanged?.Invoke();
        };
        processBox.SelectionChanged += (s, e) => { layer.DialProcess = string.IsNullOrWhiteSpace(processBox.Text) ? null : processBox.Text.Trim(); ActionChanged?.Invoke(); };
        processBox.LostFocus += (s, e) => { layer.DialProcess = string.IsNullOrWhiteSpace(processBox.Text) ? null : processBox.Text.Trim(); ActionChanged?.Invoke(); };
        upKeysBox.TextChanged += (s, e) => { layer.DialStepUpKeys = upKeysBox.Text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList(); ActionChanged?.Invoke(); };
        downKeysBox.TextChanged += (s, e) => { layer.DialStepDownKeys = downKeysBox.Text.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList(); ActionChanged?.Invoke(); };

        stack.Children.Add(processHint);
        stack.Children.Add(processBox);
        stack.Children.Add(upHint);
        stack.Children.Add(upKeysBox);
        stack.Children.Add(downHint);
        stack.Children.Add(downKeysBox);

        return card;
    }

    // A small popup anchored to a dial layer's icon button — same lightweight pattern as
    // RichActionStepListControl's per-step icon picker, for this class's own layer cards.
    private void ShowLayerIconPopup(Button anchor, Action<string> onSelect)
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

    private void BrowseActionIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Image Files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            SetActionIconRef(ProfileStoreService.SaveIconFromBytes(File.ReadAllBytes(dlg.FileName)));
            ActionChanged?.Invoke();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Couldn't load that image: {ex.Message}", "Icon Upload Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Sets the real per-action icon reference and refreshes ActionIconText's friendly
    /// label plus its thumbnail — the one place ActionIconText's displayed text gets written.</summary>
    private void SetActionIconRef(string? value)
    {
        _actionIconRef = value ?? "";
        ActionIconText.Text = FriendlyIconLabel(_actionIconRef);
        var iconPath = ProfileStoreService.ResolveIconFilePath(_actionIconRef);
        if (iconPath != null && File.Exists(iconPath))
        {
            try
            {
                ActionIconThumbnail.Source = new BitmapImage(new Uri(iconPath));
                ActionIconThumbnail.Visibility = Visibility.Visible;
                ActionIconGlyphFallback.Visibility = Visibility.Collapsed;
                return;
            }
            catch { /* fall through to blank */ }
        }
        ActionIconThumbnail.Source = null;
        ActionIconThumbnail.Visibility = Visibility.Collapsed;
        if (!ShowIconGlyphFallback) { ActionIconGlyphFallback.Visibility = Visibility.Collapsed; return; }
        ActionIconGlyphFallback.Visibility = Visibility.Visible;
        UpdateActionIconGlyphFallback();
    }

    private void ClearActionIcon_Click(object sender, RoutedEventArgs e)
    {
        SetActionIconRef(null);
        ActionChanged?.Invoke();
    }

    /// <summary>Shows a type-based glyph in the icon box instead of leaving it blank when no icon is set.</summary>
    private void UpdateActionIconGlyphFallback()
    {
        var activeItem = ActionTypeCombo.SelectedItem as ComboBoxItem;
        var actionType = _forcedType ?? activeItem?.Tag?.ToString() ?? "hotkey";
        var mediaCmd = (MediaCommandCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PlayPause";
        ActionIconGlyphFallback.Text = EditorWindow.GetActionGlyph(new ActionModel { Type = actionType, MediaCommand = mediaCmd });
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
                SetActionIconRef("builtin:" + name);
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

        CollapseAllTypePanels();

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
                    RichStepList.MaxSteps = null;
                    break;
                case "button_group":
                    MultiActionPanel.Visibility = Visibility.Visible;
                    MultiActionStepList.Visibility = Visibility.Collapsed;
                    RichStepList.Visibility = Visibility.Visible;
                    RichStepList.MaxSteps = 9;
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
            ActionIconOnlySection.Visibility = (tag == "multi_action" || tag == "macro" || tag == "button_group") ? Visibility.Collapsed : Visibility.Visible;
            if (ActionIconThumbnail.Source == null) UpdateActionIconGlyphFallback();
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
                _pathSelectedApp = null;
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
            _pathSelectedApp = app;
            _suppressFilter = true;
            PathComboInput.Text = app.Name;
            _suppressFilter = false;
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
        var hash = exePath.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase)
            ? ProfileStoreService.ExtractAndSaveUwpIcon(exePath.Substring(4))
            : ProfileStoreService.ExtractAndSaveIcon(exePath);
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
            SetActionIconRef(hash);
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
            _pathSelectedApp = null;
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
