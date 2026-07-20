using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Timers;
using CrossDeckHost.Actions;
using CrossDeckHost.ProfileStore;
using Windows.Media.Control;

namespace CrossDeckHost.Server;

/// <summary>
/// Tracks PC-side state (mute, media playback, which launch_app button is focused, dial levels)
/// and raises StateChanged so WebSocketServer can push it to the phone — closing the feedback
/// loop that action execution alone doesn't give (tap Mute, but did it actually mute?).
/// Media uses a real WinRT event (instant); everything else piggybacks one lightweight 1s poll.
/// </summary>
public class LiveStateService
{
    private readonly ProfileStoreService _profileStore;
    private readonly System.Timers.Timer _pollTimer;

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    private bool? _lastMuted;
    private bool? _lastPlaying;
    private string? _lastFocusedButtonId;
    private readonly Dictionary<string, int> _lastDialLevels = new();

    public event Action<string, bool?, int?, string?>? StateChanged;

    public LiveStateService(ProfileStoreService profileStore)
    {
        _profileStore = profileStore;
        _pollTimer = new System.Timers.Timer(1000);
        _pollTimer.Elapsed += OnPollTick;
    }

    public async void Start()
    {
        _pollTimer.Start();
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _sessionManager.CurrentSessionChanged += (_, _) => HookCurrentSession();
            HookCurrentSession();
        }
        catch
        {
            // SMTC unavailable on this Windows build — media state just won't update live.
        }
    }

    public void Stop() => _pollTimer.Stop();

    private void HookCurrentSession()
    {
        if (_currentSession != null)
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;

        try
        {
            _currentSession = _sessionManager?.GetCurrentSession();
        }
        catch
        {
            _currentSession = null;
        }

        if (_currentSession != null)
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;

        BroadcastMediaPlaying();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => BroadcastMediaPlaying();

    private void BroadcastMediaPlaying()
    {
        bool? playing = null;
        try
        {
            playing = _currentSession?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { }

        if (playing == _lastPlaying) return;
        _lastPlaying = playing;

        foreach (var b in CurrentButtons())
        {
            if (b.Action.Type == "media_control" && b.Action.MediaCommand == "PlayPause")
                StateChanged?.Invoke(b.ButtonId, playing, null, null);
        }
    }

    private void OnPollTick(object? sender, ElapsedEventArgs e)
    {
        try { PollMute(); } catch { }
        try { PollFocusedLaunchApp(); } catch { }
        try { PollDialLevels(); } catch { }
    }

    private void PollMute()
    {
        bool? muted = DialController.IsMuted();
        if (muted == _lastMuted) return;
        _lastMuted = muted;

        foreach (var b in CurrentButtons())
        {
            if (b.Action.Type == "media_control" && b.Action.MediaCommand == "VolumeMute")
                StateChanged?.Invoke(b.ButtonId, muted, null, null);
        }
    }

    private void PollFocusedLaunchApp()
    {
        string? foregroundProcessName = null;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    using var proc = Process.GetProcessById((int)pid);
                    foregroundProcessName = proc.ProcessName;

                    // UWP apps run inside a shared ApplicationFrameHost.exe container window.
                    if (string.Equals(foregroundProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                    {
                        uint realPid = 0;
                        EnumChildWindows(hwnd, (childHwnd, _) =>
                        {
                            GetWindowThreadProcessId(childHwnd, out uint childPid);
                            if (childPid != 0 && childPid != pid)
                            {
                                realPid = childPid;
                                return false; // found it, stop enumerating
                            }
                            return true;
                        }, IntPtr.Zero);

                        if (realPid != 0)
                        {
                            using var realProc = Process.GetProcessById((int)realPid);
                            foregroundProcessName = realProc.ProcessName;
                        }
                    }
                }
            }
        }
        catch { }

        string? focusedButtonId = null;
        foreach (var b in CurrentButtons())
        {
            if (b.Action.Type != "launch_app" || string.IsNullOrWhiteSpace(b.Action.Path)) continue;

            string exeName;
            try { exeName = Path.GetFileNameWithoutExtension(b.Action.Path); }
            catch { continue; }

            if (string.Equals(exeName, foregroundProcessName, StringComparison.OrdinalIgnoreCase))
            {
                focusedButtonId = b.ButtonId;
                break; // "focused" is binary — only one app is ever foreground at a time.
            }
        }

        if (focusedButtonId == _lastFocusedButtonId) return;

        // Un-glow whichever launch_app button lost focus, glow whichever gained it.
        if (_lastFocusedButtonId != null)
            StateChanged?.Invoke(_lastFocusedButtonId, false, null, null);
        if (focusedButtonId != null)
            StateChanged?.Invoke(focusedButtonId, true, null, null);

        _lastFocusedButtonId = focusedButtonId;
    }

    private static int DialLevel(string? target) => target switch
    {
        "volume" => DialController.GetVolume(),
        "brightness" => DialController.GetBrightness(),
        _ => -1
    };

    // Multiple buttons commonly share the same dialTarget (e.g. several volume/brightness
    // buttons across profiles) — DDC/CI brightness queries go over I2C, and hammering a monitor
    // with several of these every second can wedge its DDC/CI controller entirely. Query each
    // distinct target at most once per tick and fan the same value out to every button using it.
    private void PollDialLevels()
    {
        var cache = new Dictionary<string, int>();
        int LevelFor(string? target)
        {
            var key = target ?? "";
            if (!cache.TryGetValue(key, out var level))
            {
                level = DialLevel(target);
                cache[key] = level;
            }
            return level;
        }

        foreach (var b in CurrentButtons())
        {
            PollOneDialSlot(b.ButtonId, "main", b.Action, LevelFor);
            if (b.LongPressAction != null) PollOneDialSlot(b.ButtonId, "longPress", b.LongPressAction, LevelFor);
        }
    }

    private void PollOneDialSlot(string buttonId, string slot, ActionModel action, Func<string?, int> levelFor)
    {
        if (action.Type != "dial") return;
        int level = levelFor(action.DialTarget);
        if (level < 0) return;

        var key = $"{buttonId}:{slot}";
        if (_lastDialLevels.TryGetValue(key, out var last) && last == level) return;
        _lastDialLevels[key] = level;
        StateChanged?.Invoke(buttonId, null, level, slot);
    }

    private IEnumerable<ButtonModel> CurrentButtons() => _profileStore.Current.Buttons ?? Enumerable.Empty<ButtonModel>();

    /// <summary>Full current-state snapshot for every live-state-capable button, sent when a phone connects.</summary>
    public IEnumerable<(string ButtonId, bool? Active, int? Level, string? DialSlot)> GetSnapshot()
    {
        var cache = new Dictionary<string, int>();
        int LevelFor(string? target)
        {
            var key = target ?? "";
            if (!cache.TryGetValue(key, out var level))
            {
                level = DialLevel(target);
                cache[key] = level;
            }
            return level;
        }

        foreach (var b in CurrentButtons())
        {
            if (b.Action.Type == "media_control" && b.Action.MediaCommand == "PlayPause")
                yield return (b.ButtonId, _lastPlaying, null, null);
            else if (b.Action.Type == "media_control" && b.Action.MediaCommand == "VolumeMute")
                yield return (b.ButtonId, _lastMuted, null, null);
            else if (b.Action.Type == "launch_app" && !string.IsNullOrWhiteSpace(b.Action.Path))
                yield return (b.ButtonId, b.ButtonId == _lastFocusedButtonId, null, null);

            if (b.Action.Type == "dial")
            {
                int level = LevelFor(b.Action.DialTarget);
                if (level >= 0) yield return (b.ButtonId, null, level, "main");
            }
            if (b.LongPressAction?.Type == "dial")
            {
                int level = LevelFor(b.LongPressAction.DialTarget);
                if (level >= 0) yield return (b.ButtonId, null, level, "longPress");
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
}
