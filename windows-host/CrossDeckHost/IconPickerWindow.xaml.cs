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
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.png").OrderBy(f => f))
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
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0E0E10")),
                BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F1F23")),
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

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            this.DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
