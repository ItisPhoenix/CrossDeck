using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrossDeckHost;

public partial class IconPickerWindow : Window
{
    public string? SelectedIcon { get; private set; }

    public IconPickerWindow()
    {
        InitializeComponent();
        // ApplyTheme must run after layout (VisualTreeHelper walk is a no-op pre-layout) —
        // Loaded, not the constructor. See EditorWindow's constructor for the same pattern.
        Loaded += (s, e) => ThemeManager.ApplyTheme(this);
        LoadIcons();
    }

    private void LoadIcons()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png") : Array.Empty<string>();
        if (files.Length == 0)
        {
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        foreach (var file in files.OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var img = new System.Windows.Controls.Image
            {
                Width = 32,
                Height = 32,
                Source = new BitmapImage(new Uri(file))
            };
            var btn = new System.Windows.Controls.Button
            {
                Width = 52,
                Height = 52,
                Margin = new Thickness(4),
                Content = img,
                Background = ThemeManager.Brush("Brush.Void"),
                BorderBrush = ThemeManager.Brush("Brush.Hairline"),
                BorderThickness = new Thickness(1),
                ToolTip = name,
            };
            btn.Click += (s, e) =>
            {
                SelectedIcon = "builtin:" + name;
                DialogResult = true;
                Close();
            };
            IconWrapPanel.Children.Add(btn);
        }
    }
}
