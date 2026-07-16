using System.Diagnostics;
using System.Runtime.InteropServices;

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
                case "launch_app":
                    return ExecuteLaunchApp(action.Path);
                case "media_control":
                    return ExecuteMediaControl(action.MediaCommand);
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
        var inputs = new List<INPUT>();
        foreach (var vk in vks)
            inputs.Add(KeyInput(vk, keyUp: false));
        for (int i = vks.Count - 1; i >= 0; i--)
            inputs.Add(KeyInput(vks[i], keyUp: true));

        var arr = inputs.ToArray();
        var sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
        if (sent != arr.Length)
            return (false, $"SendInput only sent {sent}/{arr.Length} inputs");

        return (true, null);
    }

    private (bool, string?) ExecuteLaunchApp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "launch_app action has no path");

        string exe = path.Trim();
        string args = "";

        if (exe.StartsWith("\""))
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
            int firstSpace = exe.IndexOf(" ");
            if (firstSpace > 0)
            {
                args = exe.Substring(firstSpace + 1).Trim();
                exe = exe.Substring(0, firstSpace);
            }
        }

        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        return (true, null);
    }

    private (bool, string?) ExecuteMediaControl(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return (false, "media_control action has no command");

        // Map command names to VirtualKey mapping keys
        string keyName = command switch
        {
            "PlayPause" => "MediaPlayPause",
            "NextTrack" => "MediaNextTrack",
            "PrevTrack" => "MediaPrevTrack",
            "VolumeUp" => "VolumeUp",
            "VolumeDown" => "VolumeDown",
            "VolumeMute" => "VolumeMute",
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
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
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
