using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost;

public partial class ButtonEditorWindow : Window
{
    public ButtonModel Button { get; private set; }
    public bool IsDeleted { get; private set; } = false;
    public bool EnterFolderRequested { get; private set; } = false;

    // Only a brand-new (blank-label) button gets live autofill from the action's parameters —
    // an existing custom label is never touched, and typing in the field stops it permanently.
    private bool _labelUserEdited;
    private bool _suppressLabelEdit;

    private void DialogClose_Click(object sender, RoutedEventArgs e) => Close();

    public ButtonEditorWindow(ButtonModel button)
    {
        InitializeComponent();
        Button = button;

        // SizeToContent="Height" grows the window to fit (a long Action Chain shouldn't be stuck
        // in a fixed-height scroll box) but needs a cap so it can't grow off-screen — the inner
        // ScrollViewer takes over once content exceeds this.
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;

        var saveIcon = ThemeManager.GetChromeIcon("save");
        if (saveIcon != null) { SaveButtonIcon.Source = saveIcon; SaveButtonGlyph.Visibility = Visibility.Collapsed; }
        else SaveButtonIcon.Visibility = Visibility.Collapsed;

        var deleteIcon = ThemeManager.GetChromeIcon("trash-2");
        if (deleteIcon != null) { DeleteButtonIcon.Source = deleteIcon; DeleteButtonGlyph.Visibility = Visibility.Collapsed; }
        else DeleteButtonIcon.Visibility = Visibility.Collapsed;

        // Apply styling theme after window loaded
        Loaded += (s, e) => ThemeManager.ApplyTheme(this);
        Closed += (s, e) => { MainActionConfig.Dispose(); LongPressActionConfig.Dispose(); };

        var allApps = AppDiscovery.DiscoverApps();
        MainActionConfig.AppList = allApps;
        LongPressActionConfig.AppList = allApps;
        LongPressActionConfig.AllowDial = false;
        LongPressActionConfig.ShowIconPicker = true;

        // Only the main action's icon pickers should overwrite the button's shared icon and
        // persist fetched icons to disk — long-press gets the same pickers for parity but
        // doesn't have an icon of its own to set.
        MainActionConfig.ExtractIconOnSelect = true;
        MainActionConfig.IconExtracted += hash => IconPathText.Text = hash;
        MainActionConfig.ShowEnterFolderShortcut();
        MainActionConfig.EnterFolderShortcutClicked += () =>
        {
            EnterFolderRequested = true;
            SaveButton_Click(this, new RoutedEventArgs());
        };

        // Initialize values
        _suppressLabelEdit = true;
        LabelInput.Text = button.Label;
        _suppressLabelEdit = false;
        _labelUserEdited = !string.IsNullOrWhiteSpace(button.Label);
        IconPathText.Text = button.Icon ?? "";

        MainActionConfig.ActionChanged += () =>
        {
            if (_labelUserEdited) return;
            var suggested = SuggestLabel(MainActionConfig.GetAction());
            if (suggested != null)
            {
                _suppressLabelEdit = true;
                LabelInput.Text = suggested;
                _suppressLabelEdit = false;
            }
        };
        MainActionConfig.ActionChanged += UpdateSaveEnabled;
        LongPressActionConfig.ActionChanged += UpdateSaveEnabled;

        MainActionConfig.SetAction(button.Action);

        if (button.LongPressAction != null)
        {
            LongPressEnabledCheck.IsChecked = true;
            LongPressActionConfig.Visibility = Visibility.Visible;
            LongPressActionConfig.SetAction(button.LongPressAction);
        }

        // Matches Android: a Multiple Actions button has no separate long-press action — holding
        // it is how the chain runs at all, so the section is replaced with an explanatory note.
        MainActionConfig.ActionTypeChanged += UpdateLongPressSectionForMainType;
        UpdateLongPressSectionForMainType(button.Action.Type);

        // Populate inline built-in icons picker
        LoadBuiltinIcons();

        UpdateSaveEnabled();
    }

    /// <summary>Names the specific empty field blocking Save, or null if the action is complete.</summary>
    private static string? MissingFieldHint(ActionModel action) => action.Type switch
    {
        "hotkey" => action.Keys is { Count: > 0 } ? null : "Enter a keyboard shortcut",
        "launch_app" => string.IsNullOrWhiteSpace(action.Path) ? "Choose or type an app" : null,
        "open_url" => string.IsNullOrWhiteSpace(action.Url) ? "Enter a website URL" : null,
        "run_command" => string.IsNullOrWhiteSpace(action.Command) ? "Enter a command" : null,
        "text_snippet" => string.IsNullOrWhiteSpace(action.Text) ? "Enter text to paste" : null,
        "multi_action" => action.Actions is { Count: > 0 } ? null : "Add at least one step to the chain",
        _ => null
    };

    private void UpdateSaveEnabled()
    {
        var mainAction = MainActionConfig.GetAction();
        string? hint = MissingFieldHint(mainAction);
        if (hint == null && LongPressEnabledCheck.IsChecked == true && mainAction.Type != "multi_action")
        {
            var lpHint = MissingFieldHint(LongPressActionConfig.GetAction());
            if (lpHint != null) hint = $"Long-press action: {lpHint}";
        }

        SaveButton.IsEnabled = hint == null;
        SaveHintText.Text = hint ?? "";
        SaveHintText.Visibility = hint == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LabelInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressLabelEdit) return;
        _labelUserEdited = true;
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> MediaCommandLabels = new()
    {
        ["PlayPause"] = "Play / Pause",
        ["NextTrack"] = "Next Track",
        ["PrevTrack"] = "Previous Track",
        ["VolumeUp"] = "Volume Up",
        ["VolumeDown"] = "Volume Down",
        ["VolumeMute"] = "Mute"
    };

    /// <summary>Best-guess button label derived from the action being configured.</summary>
    private static string? SuggestLabel(ActionModel action) => action.Type switch
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
        _ => null
    };

    private void UpdateLongPressSectionForMainType(string mainType)
    {
        bool isMultiAction = mainType == "multi_action";
        LongPressSection.Visibility = isMultiAction ? Visibility.Collapsed : Visibility.Visible;
        MultiActionLongPressNote.Visibility = isMultiAction ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LongPressEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        LongPressActionConfig.Visibility = LongPressEnabledCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateSaveEnabled();
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                byte[] rawBytes = File.ReadAllBytes(dlg.FileName);
                string hash = ProfileStoreService.SaveIconFromBytes(rawBytes);
                IconPathText.Text = hash;
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
        BuiltinIconsDrawer.Visibility = BuiltinIconsDrawer.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
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
            // No fixed icon size here — it fills whatever the button leaves it (Stretch=Uniform,
            // no crop). A hardcoded icon size next to a hardcoded button size only "fits" by
            // coincidence; the button's own Padding="0" is what actually guarantees no clipping.
            var img = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                Source = new BitmapImage(new Uri(file))
            };
            var btn = new System.Windows.Controls.Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(4),
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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Button.Label = LabelInput.Text;
        Button.Icon = string.IsNullOrEmpty(IconPathText.Text) ? null : IconPathText.Text;
        Button.Action = MainActionConfig.GetAction();
        Button.LongPressAction = (LongPressEnabledCheck.IsChecked == true && Button.Action.Type != "multi_action")
            ? LongPressActionConfig.GetAction() : null;

        DialogResult = true;
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(this, "Delete this button? This can't be undone.", "Delete Button",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

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
