using System.Diagnostics;
using System.Runtime.InteropServices;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Server;

/// <summary>
/// Enumerates taskbar-eligible top-level windows (same filter Alt-Tab uses) for the phone's
/// running-apps switcher.
/// </summary>
public static class RunningApps
{
    public record WindowInfo(long Hwnd, string Title, string ProcessName, string? Icon, bool Focused);

    // exe path -> icon hash, so we don't re-extract every poll tick. Concurrent since a second
    // subscribed phone runs its own push loop on another thread.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> IconCache = new();

    public static List<WindowInfo> GetWindows()
    {
        var result = new List<WindowInfo>();
        int ownPid = Environment.ProcessId;
        IntPtr foreground = GetForegroundWindow();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
            if (((long)GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;

            // DWM-cloaked = invisible system windows (Windows Input Experience, suspended UWP
            // shells) that Alt-Tab also hides.
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            if (sb.Length == 0) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == ownPid) return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (string.Equals(proc.ProcessName, "TextInputHost", StringComparison.OrdinalIgnoreCase)) return true;
                string? icon = null;
                try
                {
                    // MainModule.FileName throws Access Denied across elevation boundaries (most
                    // elevated apps, since this host doesn't run as admin) — QueryFullProcessImageName
                    // only needs PROCESS_QUERY_LIMITED_INFORMATION, which works regardless.
                    var exePath = GetProcessImagePath((uint)pid);
                    if (exePath != null)
                    {
                        if (!IconCache.TryGetValue(exePath, out icon))
                        {
                            icon = ProfileStoreService.ExtractAndSaveIcon(exePath);
                            // Only cache successes — extraction genuinely fails for some real exes
                            // (packaged/Store installs with no extractable icon resource); caching
                            // that failure would permanently blank this window's icon instead of
                            // retrying next poll tick.
                            if (icon != null) IconCache[exePath] = icon;
                        }
                    }
                }
                catch { }

                result.Add(new WindowInfo(hWnd.ToInt64(), sb.ToString(), proc.ProcessName, icon, hWnd == foreground));
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        // Stable order — EnumWindows returns Z-order, which would make tiles shuffle on the
        // phone every time focus changes.
        return result.OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }

    public static void CloseWindow(long hwnd) => PostMessage(new IntPtr(hwnd), WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    internal static string? GetProcessImagePath(uint pid)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint WM_CLOSE = 0x0010;
    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
