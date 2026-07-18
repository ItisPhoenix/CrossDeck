using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost;

public partial class ButtonEditorWindow : Window
{
    public ButtonModel Button { get; private set; }
    public bool IsDeleted { get; private set; } = false;
    public bool EnterFolderRequested { get; private set; } = false;

    private void DialogClose_Click(object sender, RoutedEventArgs e) => Close();

    public ButtonEditorWindow(ButtonModel button)
    {
        InitializeComponent();
        Button = button;

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
        LabelInput.Text = button.Label;
        IconPathText.Text = button.Icon ?? "";

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
    }

    private void UpdateLongPressSectionForMainType(string mainType)
    {
        bool isMultiAction = mainType == "multi_action";
        LongPressSection.Visibility = isMultiAction ? Visibility.Collapsed : Visibility.Visible;
        MultiActionLongPressNote.Visibility = isMultiAction ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LongPressEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        LongPressActionConfig.Visibility = LongPressEnabledCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
