using System.IO;
using System.Text.Json;
using System.Windows;

namespace CrossDeckHost;

/// <summary>
/// Saves and restores the CrossDeck editor window geometry (position + size) between sessions.
/// Persists to %AppData%\CrossDeck\window.json so it survives app restarts.
/// </summary>
public static class WindowSettings
{
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CrossDeckHost",
        "window.json");

    private record WindowGeometry(double Left, double Top, double Width, double Height);

    /// <summary>
    /// Persists the window's current Left/Top/Width/Height to disk.
    /// Called on LocationChanged and SizeChanged.
    /// </summary>
    public static void Save(Window window)
    {
        // Only save when in a valid normal state (not minimized/maximized off-screen)
        if (window.WindowState != WindowState.Normal) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var geo = new WindowGeometry(window.Left, window.Top, window.Width, window.Height);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(geo));
        }
        catch { /* Non-critical — silently ignore */ }
    }

    /// <summary>
    /// Applies the saved geometry to the window before it is shown.
    /// Clamps to the primary screen bounds so the window can never open off-screen.
    /// </summary>
    public static void Restore(Window window)
    {
        if (!File.Exists(_settingsPath)) return;
        try
        {
            var json = File.ReadAllText(_settingsPath);
            var geo = JsonSerializer.Deserialize<WindowGeometry>(json);
            if (geo == null) return;

            // Screen bounds clamping so the window can't open fully off-screen
            var screenW = SystemParameters.PrimaryScreenWidth;
            var screenH = SystemParameters.PrimaryScreenHeight;

            double left  = Math.Clamp(geo.Left,   0, screenW - 100);
            double top   = Math.Clamp(geo.Top,    0, screenH - 60);
            double width = Math.Clamp(geo.Width,  400, screenW);
            double height = Math.Clamp(geo.Height, 300, screenH);

            window.Left   = left;
            window.Top    = top;
            window.Width  = width;
            window.Height = height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
        }
        catch { /* Non-critical — silently ignore */ }
    }
}
