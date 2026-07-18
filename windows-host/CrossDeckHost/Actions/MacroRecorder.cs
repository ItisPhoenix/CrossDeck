using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CrossDeckHost.Actions;

/// <summary>
/// Global keyboard recorder for the macro feature: captures key combos + timing while active and
/// renders them as multi_action text lines ("Keyboard Shortcut: Ctrl,C" / "Delay (ms): 500") that
/// the ButtonEditor's existing parser already understands.
/// </summary>
public class MacroRecorder : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc; // held so the GC can't collect the hook delegate
    private readonly List<string> _lines = new();
    private readonly Stopwatch _sinceLastStep = new();

    public bool IsRecording => _hookId != IntPtr.Zero;

    public void Start()
    {
        if (IsRecording) return;
        _lines.Clear();
        _sinceLastStep.Reset();
        _proc = HookCallback;
        using var curModule = Process.GetCurrentProcess().MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    /// <summary>Stops and returns the recorded steps in multi_action text format.</summary>
    public string Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _proc = null;
        return string.Join("\n", _lines);
    }

    public void Dispose() => Stop();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            // Modifiers are captured as part of the combo when a normal key lands, not as steps.
            if (!IsModifier(vk) && VirtualKey.TryGetName((ushort)vk, out var keyName))
            {
                if (_lines.Count > 0)
                {
                    int delay = (int)_sinceLastStep.ElapsedMilliseconds;
                    if (delay >= 100) _lines.Add($"Delay (ms): {delay}");
                }
                _sinceLastStep.Restart();

                var combo = new List<string>();
                if ((GetKeyState(0x11) & 0x8000) != 0) combo.Add("Ctrl");
                if ((GetKeyState(0x12) & 0x8000) != 0) combo.Add("Alt");
                if ((GetKeyState(0x10) & 0x8000) != 0) combo.Add("Shift");
                if ((GetKeyState(0x5B) & 0x8000) != 0) combo.Add("Win");
                combo.Add(keyName);
                _lines.Add($"Keyboard Shortcut: {string.Join(",", combo)}");
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsModifier(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
