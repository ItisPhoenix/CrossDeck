using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace CrossDeckHost.Actions;

public class ActionExecutor
{
    public async Task<(bool Success, string? Error)> ExecuteAsync(ProfileStore.ActionModel action)
    {
        try
        {
            switch (action.Type)
            {
                case "hotkey":
                    return ExecuteHotkey(action.Keys);
                case "mouse_click":
                    return ExecuteMouseClick(action.MouseX, action.MouseY, action.MouseButton);
                case "launch_app":
                    return ExecuteLaunchApp(action.Path);
                case "media_control":
                    return await ExecuteMediaControlAsync(action.MediaCommand);
                case "open_url":
                    return ExecuteOpenUrl(action.Url);
                case "run_command":
                    return ExecuteRunCommand(action.Command);
                case "text_snippet":
                    return ExecuteTextSnippet(action.Text);
                case "multi_action":
                    return await ExecuteMultiActionAsync(action);
                case "dial":
                    return (true, null); // dial actions only execute via dial_adjust, ignore tap/press
                case "open_folder":
                    return (true, null); // folder navigation is client-side only, nothing to run on the PC
                default:
                    return (false, $"Unknown action type '{action.Type}'");
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<(bool Success, string? Error)> ExecuteMultiActionAsync(ProfileStore.ActionModel action)
    {
        if (action.Actions == null || action.Actions.Count == 0)
            return (false, "multi_action has no sub-actions");

        for (int i = 0; i < action.Actions.Count; i++)
        {
            var subAction = action.Actions[i];
            var (success, error) = await ExecuteAsync(subAction);
            if (!success)
            {
                return (false, $"Action {i + 1} ({subAction.Type}) failed: {error}");
            }

            if (action.Delays != null && i < action.Delays.Count)
            {
                int delayMs = action.Delays[i];
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }
        }

        return (true, null);
    }

    private (bool, string?) ExecuteHotkey(List<string>? keyNames)
    {
        if (keyNames is null || keyNames.Count == 0)
            return (false, "hotkey action has no keys");

        var vks = new List<ushort>();
        foreach (var name in keyNames)
        {
            if (!VirtualKey.TryGetCode(name, out var vk))
                return (false, $"Unknown key name '{name}'");
            vks.Add(vk);
        }

        // Press all keys down in order, then release in reverse order (standard combo behavior).
        var downs = vks.Select(vk => KeyInput(vk, keyUp: false)).ToArray();
        var ups = ((IEnumerable<ushort>)vks).Reverse().Select(vk => KeyInput(vk, keyUp: true)).ToArray();

        var sentDown = SendInput((uint)downs.Length, downs, Marshal.SizeOf(typeof(INPUT)));
        if (sentDown != downs.Length)
            return (false, $"SendInput only sent {sentDown}/{downs.Length} key-down inputs (Win32 error {Marshal.GetLastWin32Error()}, foreground: {DescribeForegroundWindow()})");

        // Media/volume keys are picked up by a low-level hook that some apps debounce — a down+up
        // pair batched into the same instant can get coalesced and silently dropped. A short real
        // gap between them fixes the intermittent "media control doesn't work" reports.
        if (vks.Any(VirtualKey.IsExtended))
            Thread.Sleep(15);

        var sentUp = SendInput((uint)ups.Length, ups, Marshal.SizeOf(typeof(INPUT)));
        if (sentUp != ups.Length)
            return (false, $"SendInput only sent {sentUp}/{ups.Length} key-up inputs (Win32 error {Marshal.GetLastWin32Error()}, foreground: {DescribeForegroundWindow()})");

        return (true, null);
    }

    private (bool, string?) ExecuteMouseClick(int? x, int? y, string? button)
    {
        if (x is null || y is null || string.IsNullOrEmpty(button))
            return (false, "mouse_click action is missing coordinates or button");

        SetCursorPos(x.Value, y.Value);

        uint down = button == "right" ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        uint up = button == "right" ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = down } } },
            new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = up } } }
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (sent != inputs.Length)
            return (false, $"SendInput only sent {sent}/{inputs.Length} mouse inputs (Win32 error {Marshal.GetLastWin32Error()})");

        return (true, null);
    }

    /// <summary>
    /// Foreground process name at failure time, plus whether it's likely running elevated (we can't
    /// open its token from a non-elevated process, so ACCESS_DENIED there is itself the tell). Lets
    /// a UIPI-blocked SendInput ("elevated window stole focus") be confirmed from the error toast
    /// instead of needing someone to catch the PC screen at the exact moment it happens.
    /// </summary>
    private static string DescribeForegroundWindow()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
                return "none";

            GetWindowThreadProcessId(hWnd, out var pid);
            using var proc = Process.GetProcessById((int)pid);

            bool likelyElevated;
            try
            {
                _ = proc.Handle; // throws Win32Exception (ACCESS_DENIED) if the process outranks us
                likelyElevated = false;
            }
            catch
            {
                likelyElevated = true;
            }

            return likelyElevated ? $"{proc.ProcessName}.exe (likely elevated — UIPI would block SendInput)" : $"{proc.ProcessName}.exe";
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.Message})";
        }
    }

    private (bool, string?) ExecuteLaunchApp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "launch_app action has no path");

        string exe = path.Trim();
        string args = "";

        if (File.Exists(exe))
        {
            // The entire path exists as a file, run it directly without splitting on space
        }
        else if (exe.StartsWith("\""))
        {
            int nextQuote = exe.IndexOf("\"", 1);
            if (nextQuote > 0)
            {
                args = exe.Substring(nextQuote + 1).Trim();
                exe = exe.Substring(1, nextQuote - 1);
            }
        }
        else
        {
            // Check if any space-separated prefix exists as a file
            int searchStart = 0;
            bool foundFile = false;
            while (true)
            {
                int spaceIndex = exe.IndexOf(' ', searchStart);
                if (spaceIndex < 0) break;

                string prefix = exe.Substring(0, spaceIndex);
                if (File.Exists(prefix))
                {
                    args = exe.Substring(spaceIndex + 1).Trim();
                    exe = prefix;
                    foundFile = true;
                    break;
                }
                searchStart = spaceIndex + 1;
            }

            if (!foundFile)
            {
                // Fallback to original behavior: split on first space
                int firstSpace = exe.IndexOf(" ");
                if (firstSpace > 0)
                {
                    args = exe.Substring(firstSpace + 1).Trim();
                    exe = exe.Substring(0, firstSpace);
                }
            }
        }

        if (TryFocusExistingWindow(exe))
            return (true, null);

        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        return (true, null);
    }

    /// <summary>
    /// If a process matching this exe's name (without extension) is already running with a
    /// visible main window, brings it to front instead of starting a new instance — like clicking
    /// its taskbar icon rather than double-clicking a fresh shortcut. ponytail: exe-name matching
    /// misidentifies launcher-stub apps (e.g. Chrome/Electron processes whose name differs from
    /// the launcher exe) — acceptable v1 ceiling; falls through to a normal launch either way.
    /// </summary>
    private static bool TryFocusExistingWindow(string exePath)
    {
        var candidates = FindProcessesByExePath(exePath);
        foreach (var proc in candidates)
        {
            IntPtr hWnd = FindWindowForProcess(proc);
            if (hWnd == IntPtr.Zero) continue;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            ForceForegroundWindow(hWnd);
            return true;
        }
        return false;
    }

    /// <summary>Restore-if-minimized + bring to front. Used by the running-apps switcher.</summary>
    internal static void FocusWindow(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);
        ForceForegroundWindow(hWnd);
    }

    private static List<Process> FindProcessesByExePath(string exePath)
    {
        var result = new List<Process>();
        string processName = "";
        try
        {
            processName = Path.GetFileNameWithoutExtension(exePath);
        }
        catch { }

        if (string.IsNullOrEmpty(processName)) return result;

        string fullTarget = "";
        try
        {
            if (File.Exists(exePath))
            {
                fullTarget = Path.GetFullPath(exePath);
            }
        }
        catch { }

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                // 1. Safe process name match
                if (string.Equals(proc.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(proc);
                    continue;
                }

                // 2. Safe alias match
                if (string.Equals(processName, "calc", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(proc.ProcessName, "CalculatorApp", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(proc.ProcessName, "Calculator", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(proc);
                    continue;
                }

                // wt.exe is a launcher stub that hands off to the real WindowsTerminal.exe process
                // and exits — same launcher/real-process name mismatch as the class comment above.
                if (string.Equals(processName, "wt", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(proc.ProcessName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(proc);
                    continue;
                }

                // 3. Executable path match (risky MainModule access, wrapped in its own try-catch)
                if (!string.IsNullOrEmpty(fullTarget))
                {
                    string? procPath = null;
                    try
                    {
                        procPath = proc.MainModule?.FileName;
                    }
                    catch
                    {
                        // Ignore access denied for MainModule
                    }

                    if (procPath != null && string.Equals(Path.GetFullPath(procPath), fullTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(proc);
                        continue;
                    }
                }
            }
            catch
            {
                // Ignore other exceptions for this process
            }
        }
        return result;
    }

    private static IntPtr FindWindowForProcess(Process proc)
    {
        if (proc.MainWindowHandle != IntPtr.Zero)
            return proc.MainWindowHandle;

        IntPtr foundWindow = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == proc.Id && IsWindowVisible(hWnd))
            {
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                if (sb.Length > 0)
                {
                    foundWindow = hWnd;
                    return false; // Stop enumeration
                }
            }

            // UWP ApplicationFrameHost Fallback:
            // If the process we are looking for is Calculator, and we see a visible window owned by 
            // ApplicationFrameHost whose title is "Calculator", focus that window handle.
            if (IsWindowVisible(hWnd) && (string.Equals(proc.ProcessName, "CalculatorApp", StringComparison.OrdinalIgnoreCase) || 
                                          string.Equals(proc.ProcessName, "Calculator", StringComparison.OrdinalIgnoreCase)))
            {
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                if (string.Equals(sb.ToString(), "Calculator", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var winProc = Process.GetProcessById((int)processId);
                        if (string.Equals(winProc.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                        {
                            foundWindow = hWnd;
                            return false; // Stop enumeration
                        }
                    }
                    catch { }
                }
            }

            // Console-host fallback: powershell.exe/cmd.exe/pwsh.exe don't own their visible
            // window. On older Windows a per-instance conhost.exe does (ConsoleWindowClass check
            // below). On Windows 11 with the default "Windows Terminal" setting, the OS silently
            // hosts it inside WindowsTerminal.exe instead — a completely different process from
            // the one that was actually launched. Match either.
            if (IsWindowVisible(hWnd) && foundWindow == IntPtr.Zero &&
                (string.Equals(proc.ProcessName, "powershell", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(proc.ProcessName, "pwsh", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(proc.ProcessName, "cmd", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var winProc = Process.GetProcessById((int)processId);
                    if (string.Equals(winProc.ProcessName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                    {
                        foundWindow = hWnd;
                        return false; // Stop enumeration
                    }
                }
                catch { }

                var cls = new System.Text.StringBuilder(256);
                GetClassName(hWnd, cls, 256);
                if (string.Equals(cls.ToString(), "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase))
                {
                    foundWindow = hWnd;
                    return false; // Stop enumeration
                }
            }

            return true; // Continue
        }, IntPtr.Zero);

        return foundWindow;
    }

    // AttachThreadInput alone doesn't always beat Windows' "recent real input" gate on
    // SetForegroundWindow (taskbar icon just blinks) — a synthetic Alt tap satisfies it too.
    private static void ForceForegroundWindow(IntPtr hWnd)
    {
        uint currentThreadId = GetCurrentThreadId();
        IntPtr foregroundWindow = GetForegroundWindow();
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);

        bool attached = false;
        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (VirtualKey.TryGetCode("Alt", out var altVk))
            {
                var altTap = new[] { KeyInput(altVk, keyUp: false), KeyInput(altVk, keyUp: true) };
                SendInput((uint)altTap.Length, altTap, Marshal.SizeOf(typeof(INPUT)));
            }

            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }

    private async Task<(bool, string?)> ExecuteMediaControlAsync(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return (false, "media_control action has no command");

        // Volume/mute are OS-level (master volume, no app involved) — the legacy hardware key
        // already lands reliably, no session to target.
        if (command is "VolumeUp" or "VolumeDown" or "VolumeMute")
            return ExecuteHotkey(new List<string> { command });

        // Play/Pause/Next/Prev route through whichever app currently owns the media session —
        // same as a physical keyboard's media keys. SendInput reports "sent" even when Windows
        // silently drops the key because the wrong (or no) app is targeted, so there's no way to
        // tell it failed. The Media Control API talks to a specific session directly and returns
        // a real success/failure result instead of a guess.
        try
        {
            var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            // GetCurrentSession() can point at a stale session — e.g. a browser tab that just
            // entered Picture-in-Picture stops being "current" from Windows' point of view even
            // though it's still playing. Prefer whichever session is actually reporting
            // Playing/Paused right now; only fall back to "current" if none are.
            var activeSession = sessionManager.GetSessions().FirstOrDefault(s =>
            {
                var status = s.GetPlaybackInfo()?.PlaybackStatus;
                return status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    || status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            }) ?? sessionManager.GetCurrentSession();

            if (activeSession is not null)
            {
                bool ok = command switch
                {
                    "PlayPause" => await activeSession.TryTogglePlayPauseAsync(),
                    "NextTrack" => await activeSession.TrySkipNextAsync(),
                    "PrevTrack" => await activeSession.TrySkipPreviousAsync(),
                    _ => false
                };
                if (ok)
                    return (true, null);
            }
        }
        catch
        {
            // No active session / API unsupported on this Windows build — fall through to the
            // legacy hardware key below rather than failing outright.
        }

        string keyName = command switch
        {
            "PlayPause" => "MediaPlayPause",
            "NextTrack" => "MediaNextTrack",
            "PrevTrack" => "MediaPrevTrack",
            _ => command
        };
        return ExecuteHotkey(new List<string> { keyName });
    }

    private (bool, string?) ExecuteOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "open_url action has no URL");

        // Sanitize protocol
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return (true, null);
    }

    private (bool, string?) ExecuteRunCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return (false, "run_command action has no command");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
        return (true, null);
    }

    private (bool, string?) ExecuteTextSnippet(string? text)
    {
        if (text is null)
            return (false, "text_snippet has no text");

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            string? oldText = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            System.Windows.Clipboard.SetText(text);

            // Control (0x11) + V (0x56)
            var inputs = new INPUT[]
            {
                KeyInput(0x11, keyUp: false),
                KeyInput(0x56, keyUp: false),
                KeyInput(0x56, keyUp: true),
                KeyInput(0x11, keyUp: true)
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

            Task.Delay(100).ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (oldText != null)
                        System.Windows.Clipboard.SetText(oldText);
                    else
                        System.Windows.Clipboard.Clear();
                });
            });
        });

        return (true, null);
    }

    private static INPUT KeyInput(ushort vk, bool keyUp)
    {
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        if (VirtualKey.IsExtended(vk))
            flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    // ---- P/Invoke boilerplate for SendInput ----

    private const int INPUT_KEYBOARD = 1;
    private const int INPUT_MOUSE = 0;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    // ---- P/Invoke boilerplate for focus-existing-window (TryFocusExistingWindow) ----

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
