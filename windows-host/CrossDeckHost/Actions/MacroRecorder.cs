using System.Diagnostics;
using System.Runtime.InteropServices;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Actions;

/// <summary>
/// Global keyboard+mouse recorder for the macro feature: captures key combos and clean
/// left/right clicks with real inter-step timing, emitting them as structured ActionSteps
/// that the step-list editor appends directly — no text format round-trip.
/// </summary>
public class MacroRecorder : IDisposable
{
    private IntPtr _kbHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _kbProc; // held so the GC can't collect the hook delegate
    private LowLevelMouseProc? _mouseProc;
    private readonly List<ActionStep> _steps = new();
    private readonly Stopwatch _sinceLastStep = new();

    // Pending mouse-down state, used to tell a click from a drag.
    private string? _pendingButton;
    private POINT _pendingPos;
    private readonly Stopwatch _sinceMouseDown = new();

    private const int ClickMaxDurationMs = 400;
    private const int ClickMaxDragPx = 6;

    public bool IsRecording => _kbHookId != IntPtr.Zero;

    public void Start()
    {
        if (IsRecording) return;
        _steps.Clear();
        _sinceLastStep.Reset();
        _pendingButton = null;
        _kbProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
        using var curModule = Process.GetCurrentProcess().MainModule!;
        var hMod = GetModuleHandle(curModule.ModuleName);
        _kbHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
    }

    /// <summary>Stops and returns the recorded steps, each already carrying the delay before it.</summary>
    public List<ActionStep> Stop()
    {
        if (_kbHookId != IntPtr.Zero) { UnhookWindowsHookEx(_kbHookId); _kbHookId = IntPtr.Zero; }
        if (_mouseHookId != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHookId); _mouseHookId = IntPtr.Zero; }
        _kbProc = null;
        _mouseProc = null;
        return new List<ActionStep>(_steps);
    }

    public void Dispose() => Stop();

    private void EmitStep(ActionModel action)
    {
        // The delay we just waited belongs to whichever step preceded this one, not this one.
        if (_steps.Count > 0)
        {
            int delay = (int)_sinceLastStep.ElapsedMilliseconds;
            if (delay >= 100) _steps[_steps.Count - 1].DelayAfterMs = delay;
        }
        _sinceLastStep.Restart();
        _steps.Add(new ActionStep { Action = action, DelayAfterMs = 0 });
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            // Modifiers are captured as part of the combo when a normal key lands, not as steps.
            if (!IsModifier(vk) && VirtualKey.TryGetName((ushort)vk, out var keyName))
            {
                var combo = new List<string>();
                if ((GetKeyState(0x11) & 0x8000) != 0) combo.Add("Ctrl");
                if ((GetKeyState(0x12) & 0x8000) != 0) combo.Add("Alt");
                if ((GetKeyState(0x10) & 0x8000) != 0) combo.Add("Shift");
                if ((GetKeyState(0x5B) & 0x8000) != 0) combo.Add("Win");
                combo.Add(keyName);
                EmitStep(new ActionModel { Type = "hotkey", Keys = combo });
            }
        }
        return CallNextHookEx(_kbHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;

            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                _pendingButton = msg == WM_LBUTTONDOWN ? "left" : "right";
                _pendingPos = hookStruct.pt;
                _sinceMouseDown.Restart();
            }
            else if ((msg == WM_LBUTTONUP && _pendingButton == "left") ||
                     (msg == WM_RBUTTONUP && _pendingButton == "right"))
            {
                int dx = hookStruct.pt.X - _pendingPos.X;
                int dy = hookStruct.pt.Y - _pendingPos.Y;
                bool isClick = _sinceMouseDown.ElapsedMilliseconds <= ClickMaxDurationMs
                    && Math.Abs(dx) <= ClickMaxDragPx && Math.Abs(dy) <= ClickMaxDragPx;

                // ponytail: click-only capture, drags are silently ignored — add drag-capture
                // if a real macro workflow needs it.
                if (isClick)
                {
                    EmitStep(new ActionModel
                    {
                        Type = "mouse_click",
                        MouseX = _pendingPos.X,
                        MouseY = _pendingPos.Y,
                        MouseButton = _pendingButton
                    });
                }
                _pendingButton = null;
            }
        }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private static bool IsModifier(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
