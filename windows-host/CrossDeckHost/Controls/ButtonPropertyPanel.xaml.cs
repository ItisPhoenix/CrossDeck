using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrossDeckHost;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Controls;

public partial class ButtonPropertyPanel : System.Windows.Controls.UserControl
{
    /// <summary>Fired with the mutated model every time an edit becomes valid enough to commit.
    /// The caller (EditorWindow) owns persisting it (add-if-new / UpdateButton / Save / refresh).</summary>
    public event Action<ButtonModel>? Applied;
    public event Action? DeleteRequested;
    public event Action? EnterFolderRequested;

    private ButtonModel? _button;
    private bool _isNew;
    private bool _isDialButton;
    private bool _labelUserEdited;
    private bool _suppressLabelEdit;
    private bool _handlersWired;
    // True for the duration of LoadButton's own programmatic setup (SetAction, checkbox init, etc.)
    // — those calls fire the exact same ActionChanged/ActionTypeChanged events a real user edit
    // does, so without this guard, merely SELECTING an existing button would spuriously auto-apply
    // its own unchanged config and pop the Undo toast before any real edit happened.
    private bool _isLoading;
    // Real backing value (a content hash or "builtin:name") — IconPathText only ever shows the
    // friendly label derived from this, never the raw reference.
    private string _iconRef = "";

    public ButtonPropertyPanel()
    {
        InitializeComponent();

        var deleteIcon = ThemeManager.GetChromeIcon("trash-2");
        // CaptionButton's Content is plain text ("🗑") by default; swap in the themed glyph if one exists,
        // same fallback pattern ButtonEditorWindow used for its Save/Delete buttons.
        if (deleteIcon != null)
        {
            var img = new System.Windows.Controls.Image { Source = deleteIcon, Width = 14, Height = 14 };
            DeleteIconBtn.Content = img;
        }
    }

    /// <summary>Called once per selection change (a different button/dial clicked, or a blank cell
    /// for "new"). Wires the ActionConfigControl handlers exactly once (on first load), then resets
    /// per-button state for every subsequent call.</summary>
    public void LoadButton(ButtonModel button, bool isNew, bool isDial, System.Collections.Generic.List<DiscoveredApp> appList)
    {
        _isLoading = true;
        _button = button;
        _isNew = isNew;
        _isDialButton = isDial;

        EmptyStateText.Visibility = Visibility.Collapsed;
        HeaderRow.Visibility = Visibility.Visible;
        ContentScroller.Visibility = Visibility.Visible;
        DeleteIconBtn.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

        if (!_handlersWired)
        {
            _handlersWired = true;
            MainActionConfig.AppList = appList;
            LongPressActionConfig.AppList = appList;
            LongPressActionConfig.IsLongPress = true;
            MainActionConfig.ExtractIconOnSelect = true;
            LongPressActionConfig.ExtractIconOnSelect = true;
            MainActionConfig.IconExtracted += hash => { SetIconRef(hash); TryApply(); };
            MainActionConfig.ShowEnterFolderShortcut();
            MainActionConfig.EnterFolderShortcutClicked += () => { TryApply(); EnterFolderRequested?.Invoke(); };
            MainActionConfig.ActionChanged += () =>
            {
                if (!_labelUserEdited)
                {
                    var suggested = ActionConfigControl.SuggestLabel(MainActionConfig.GetAction());
                    if (suggested != null)
                    {
                        _suppressLabelEdit = true;
                        LabelInput.Text = suggested;
                        _suppressLabelEdit = false;
                    }
                }
                // Stack toggling doesn't change the type ("dial" throughout), so ActionTypeChanged
                // never refires for it — re-check the icon/label visibility on every change instead.
                if (_isDialButton) UpdateSecondaryActionSectionsForMainType(MainActionConfig.GetAction().Type);
                else TryApply();
            };
            MainActionConfig.ActionTypeChanged += UpdateSecondaryActionSectionsForMainType;
            LongPressActionConfig.ActionTypeChanged += UpdateLongPressButton1Chrome;
        }

        _suppressLabelEdit = true;
        LabelInput.Text = button.Label;
        _suppressLabelEdit = false;
        _labelUserEdited = !string.IsNullOrWhiteSpace(button.Label);
        SetIconRef(button.Icon);

        MainActionConfig.ForceDialMode = isDial;
        MainActionConfig.SetAction(button.Action);
        UpdateSecondaryActionSectionsForMainType(button.Action.Type);

        LongPressEnabledCheck.Checked -= LongPressEnabledCheck_Changed;
        LongPressEnabledCheck.Unchecked -= LongPressEnabledCheck_Changed;
        if (button.LongPressAction != null)
        {
            LongPressEnabledCheck.IsChecked = true;
            LongPressButton1Card.Visibility = Visibility.Visible;
            LongPressActionConfig.SetAction(button.LongPressAction);
        }
        else
        {
            LongPressEnabledCheck.IsChecked = false;
            LongPressButton1Card.Visibility = Visibility.Collapsed;
            LongPressActionConfig.SetAction(new ActionModel { Type = "hotkey" });
        }
        LongPressEnabledCheck.Checked += LongPressEnabledCheck_Changed;
        LongPressEnabledCheck.Unchecked += LongPressEnabledCheck_Changed;
        UpdateLongPressButton1Chrome(button.LongPressAction?.Type ?? "hotkey");

        LoadBuiltinIcons();
        UpdatePreview();
        UpdateValidation();
        _isLoading = false;
    }

    /// <summary>Returns to the empty state — called when nothing is selected, or the selection
    /// (delete, folder navigation, profile switch) is no longer valid.</summary>
    public void Clear()
    {
        _button = null;
        EmptyStateText.Visibility = Visibility.Visible;
        HeaderRow.Visibility = Visibility.Collapsed;
        ContentScroller.Visibility = Visibility.Collapsed;
    }

    /// <summary>Names the specific empty field blocking commit, or null if the action is complete —
    /// identical rule set to ButtonEditorWindow's old Save-gating logic.</summary>
    private static string? MissingFieldHint(ActionModel action) => action.Type switch
    {
        "hotkey" => action.Keys is { Count: > 0 } ? null : "Enter a keyboard shortcut",
        "launch_app" => string.IsNullOrWhiteSpace(action.Path) ? "Choose or type an app" : null,
        "open_url" => string.IsNullOrWhiteSpace(action.Url) ? "Enter a website URL" : null,
        "run_command" => string.IsNullOrWhiteSpace(action.Command) ? "Enter a command" : null,
        "text_snippet" => string.IsNullOrWhiteSpace(action.Text) ? "Enter text to paste" : null,
        "multi_action" => action.Actions is { Count: > 0 } ? null : "Add at least one step to the chain",
        "macro" => action.Actions is { Count: > 0 } ? null : "Record at least one step",
        _ => null
    };

    private void UpdateValidation()
    {
        var mainAction = MainActionConfig.GetAction();
        string? hint = MissingFieldHint(mainAction);
        if (hint == null && _isDialButton && LongPressEnabledCheck.IsChecked == true)
        {
            var lpHint = MissingFieldHint(LongPressActionConfig.GetAction());
            if (lpHint != null) hint = $"Tap action: {lpHint}";
        }
        ValidationHintText.Text = hint ?? "";
        ValidationHintText.Visibility = hint == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Auto-apply core: only commits (fires Applied) when the config is valid, matching
    /// the old Save-button's gating — invalid edits update the preview/hint but never touch the
    /// live grid button, so a half-typed field never breaks what's on the deck.</summary>
    private void TryApply()
    {
        if (_isLoading || _button == null) return;
        UpdatePreview();
        UpdateValidation();

        var mainAction = MainActionConfig.GetAction();
        if (MissingFieldHint(mainAction) != null) return;
        if (_isDialButton && LongPressEnabledCheck.IsChecked == true && MissingFieldHint(LongPressActionConfig.GetAction()) != null) return;

        _button.Label = LabelInput.Text;
        _button.Icon = string.IsNullOrEmpty(_iconRef) ? null : _iconRef;
        _button.Action = mainAction;
        // Tap Action removed for dials — stacking is the only way to get multiple behaviors now;
        // this also clears it for any dial saved before this change.
        _button.LongPressAction = null;

        Applied?.Invoke(_button);
    }

    /// <summary>Sets the real icon reference and refreshes both the friendly label and the
    /// thumbnail derived from it — the one place IconPathText's displayed text gets written.</summary>
    private void SetIconRef(string? value)
    {
        _iconRef = value ?? "";
        IconPathText.Text = ActionConfigControl.FriendlyIconLabel(_iconRef);
    }

    private void UpdatePreview()
    {
        PreviewLabel.Text = string.IsNullOrWhiteSpace(LabelInput.Text) ? "(no label)" : LabelInput.Text;
        var iconPath = ProfileStoreService.ResolveIconFilePath(_iconRef);
        if (iconPath != null && File.Exists(iconPath))
        {
            try { PreviewIcon.Source = new BitmapImage(new Uri(iconPath)); IconThumbnail.Source = PreviewIcon.Source; return; }
            catch { /* fall through to blank */ }
        }
        PreviewIcon.Source = null;
        IconThumbnail.Source = null;
    }

    private void LabelInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressLabelEdit) return;
        _labelUserEdited = true;
        TryApply();
    }

    private void UpdateSecondaryActionSectionsForMainType(string mainType)
    {
        bool isChainType = mainType == "multi_action" || mainType == "macro";
        MultiActionLongPressNote.Visibility = (!_isDialButton && isChainType) ? Visibility.Visible : Visibility.Collapsed;
        // A stacked dial's own icon/label are dead — each layer shows its own instead.
        bool isStackedDial = _isDialButton && MainActionConfig.IsDialStack;
        MainIconSection.Visibility = (mainType == "multi_action" || mainType == "button_group" || isStackedDial) ? Visibility.Collapsed : Visibility.Visible;
        LabelInput.Visibility = isStackedDial ? Visibility.Collapsed : Visibility.Visible;
        TryApply();
    }

    private void LongPressEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        LongPressButton1Card.Visibility = LongPressEnabledCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateLongPressButton1Chrome(LongPressActionConfig.GetAction().Type);
        TryApply();
    }

    private void UpdateLongPressButton1Chrome(string type)
    {
        bool isChain = type == "multi_action";
        LongPressButton1Header.Visibility = isChain ? Visibility.Collapsed : Visibility.Visible;
        AddAnotherLongPressActionButton.Visibility = (LongPressEnabledCheck.IsChecked == true && !isChain) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddAnotherLongPressAction_Click(object sender, RoutedEventArgs e)
    {
        var current = LongPressActionConfig.GetAction();
        if (current.Type == "multi_action" || current.Type == "macro") return;

        var wrapped = new ActionModel
        {
            Type = "multi_action",
            Actions = new System.Collections.Generic.List<ActionModel> { current },
            Delays = new System.Collections.Generic.List<int> { 0 }
        };
        LongPressActionConfig.SetAction(wrapped);
        TryApply();
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Image Files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                byte[] rawBytes = File.ReadAllBytes(dlg.FileName);
                string hash = ProfileStoreService.SaveIconFromBytes(rawBytes);
                SetIconRef(hash);
                TryApply();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Couldn't load that image: {ex.Message}", "Icon Upload Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ToggleBuiltinIcons_Click(object sender, RoutedEventArgs e)
    {
        BuiltinIconsDrawer.Visibility = BuiltinIconsDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void IconSearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => LoadBuiltinIcons();

    private void LoadBuiltinIcons()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin");
        if (!Directory.Exists(dir)) return;

        var query = IconSearchInput?.Text ?? "";
        IconWrapPanel.Children.Clear();
        foreach (var file in Directory.GetFiles(dir, "*.png").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(query) && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var img = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(file)) };
            var btn = new System.Windows.Controls.Button
            {
                Width = 40, Height = 40, Padding = new Thickness(4), Margin = new Thickness(3),
                Content = img,
                Background = ThemeManager.Brush("Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(1),
                ToolTip = name,
            };
            btn.Click += (s, e) =>
            {
                SetIconRef("builtin:" + name);
                BuiltinIconsDrawer.Visibility = Visibility.Collapsed;
                TryApply();
            };
            IconWrapPanel.Children.Add(btn);
        }
    }

    private void ClearIcon_Click(object sender, RoutedEventArgs e) { SetIconRef(null); TryApply(); }

    private void DeleteIconBtn_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();

    public void Dispose()
    {
        MainActionConfig.Dispose();
        LongPressActionConfig.Dispose();
    }
}
