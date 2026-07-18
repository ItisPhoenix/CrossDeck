using System.Windows;
using System.Windows.Media;

namespace CrossDeckHost;

/// <summary>Live per-profile accent color, propagated via the "Brush.Accent" DynamicResource.</summary>
public static class ThemeManager
{
    private const string DefaultAccentHex = "#00E5FF"; // matches Resources/Colors.xaml Brush.SignalCyan

    private static string _accentColor = DefaultAccentHex;

    public static string AccentColor
    {
        get => _accentColor;
        set
        {
            _accentColor = string.IsNullOrWhiteSpace(value) ? DefaultAccentHex : value;
            UpdateAccentResource();
        }
    }

    public static void ApplyTheme(Window window)
    {
        UpdateAccentResource();
    }

    /// <summary>Looks up a design token brush (e.g. "Brush.Void") from Resources/Colors.xaml.</summary>
    public static SolidColorBrush Brush(string key) => (SolidColorBrush)System.Windows.Application.Current.Resources[key];

    private static void UpdateAccentResource()
    {
        // Fully qualified: UseWindowsForms=true (tray icon) + ImplicitUsings puts System.Drawing's
        // Color/ColorConverter/Application in scope alongside the WPF ones of the same name.
        SolidColorBrush brush;
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_accentColor)!;
            brush = new SolidColorBrush(color);
        }
        catch
        {
            brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(DefaultAccentHex)!);
        }

        brush.Freeze();
        System.Windows.Application.Current.Resources["Brush.Accent"] = brush;
    }

    /// <summary>
    /// Looks up a chrome icon (Save, Delete, etc.) from the same bundled Lucide pack button
    /// icons use (Assets/Builtin/{name}.png). Returns null if that icon isn't in the pack —
    /// callers must fall back to their current text/emoji rather than break.
    /// </summary>
    public static System.Windows.Media.Imaging.BitmapImage? GetChromeIcon(string name)
    {
        var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin", $"{name}.png");
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            return new System.Windows.Media.Imaging.BitmapImage(new Uri(path));
        }
        catch
        {
            return null;
        }
    }
}
