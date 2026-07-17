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
}
