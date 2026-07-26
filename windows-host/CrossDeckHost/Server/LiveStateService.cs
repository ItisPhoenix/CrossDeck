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

    // Unbound app_volume (no DialProcess) has no single per-button level — it opens the live
    // multi-app mixer instead (see WebSocketServer's audio_mixer_subscribe/AudioMixerPushLoopAsync).
    // A bound app_volume dial (DialProcess set) or a mic dial both behave like volume/brightness.
    private static int DialLevel(string? target, string? process) => target switch
    {
        "volume" => DialController.GetVolume(),
        "mic" => DialController.GetMicVolume(),
        "brightness" => DialController.GetBrightness(),
        "app_volume" when !string.IsNullOrEmpty(process) => DialController.GetAppVolume(process),
        _ => -1
    };

    // Multiple buttons commonly share the same dialTarget (e.g. several volume/brightness
    // buttons across profiles) — DDC/CI brightness queries go over I2C, and hammering a monitor
    // with several of these every second can wedge its DDC/CI controller entirely. Query each
    // distinct (target, process) pair at most once per tick and fan the same value out.
    private void PollDialLevels()
    {
        var cache = new Dictionary<string, int>();
        int LevelFor(string? target, string? process)
        {
            var key = $"{target}:{process}";
            if (!cache.TryGetValue(key, out var level))
            {
                level = DialLevel(target, process);
                cache[key] = level;
            }
            return level;
        }

        foreach (var b in CurrentDials())
        {
            PollDialSlotLayers(b.ButtonId, "main", b.Action, LevelFor);
            if (b.LongPressAction != null) PollDialSlotLayers(b.ButtonId, "longPress", b.LongPressAction, LevelFor);
        }
    }

    /// <summary>Polls every layer of a stacked dial (not just whichever the client currently has
    /// open) so a layer's badge is already fresh the moment the user cycles to it — same reason
    /// PollFocusedLaunchApp doesn't wait for a tap. An unstacked dial (Actions null/empty) is
    /// just a one-layer "stack" of itself.</summary>
    private void PollDialSlotLayers(string buttonId, string slot, ActionModel action, Func<string?, string?, int> levelFor)
    {
        if (action.Type != "dial") return;
        var layers = action.Actions is { Count: > 0 } stack ? stack : new List<ActionModel> { action };
        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            int level = levelFor(layer.DialTarget, layer.DialProcess);
            if (level < 0) continue;

            // Layer 0 keeps the plain "main"/"longPress" slot string — identical to an unstacked
            // dial — so nothing downstream needs to special-case the common single-layer case.
            var wireSlot = i == 0 ? slot : $"{slot}:{i}";
            var key = $"{buttonId}:{wireSlot}";
            if (_lastDialLevels.TryGetValue(key, out var last) && last == level) continue;
            _lastDialLevels[key] = level;
            StateChanged?.Invoke(buttonId, null, level, wireSlot);
        }
    }

    private IEnumerable<ButtonModel> CurrentButtons() => _profileStore.Current.Buttons ?? Enumerable.Empty<ButtonModel>();
    private IEnumerable<ButtonModel> CurrentDials() => _profileStore.Current.Dials ?? Enumerable.Empty<ButtonModel>();

    /// <summary>Full current-state snapshot for every live-state-capable button, sent when a phone connects.</summary>
    public IEnumerable<(string ButtonId, bool? Active, int? Level, string? DialSlot)> GetSnapshot()
    {
        var cache = new Dictionary<string, int>();
        int LevelFor(string? target, string? process)
        {
            var key = $"{target}:{process}";
            if (!cache.TryGetValue(key, out var level))
            {
                level = DialLevel(target, process);
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
        }

        foreach (var b in CurrentDials())
        {
            if (b.Action.Type == "dial")
            {
                var layers = b.Action.Actions is { Count: > 0 } stack ? stack : new List<ActionModel> { b.Action };
                for (int i = 0; i < layers.Count; i++)
                {
                    int level = LevelFor(layers[i].DialTarget, layers[i].DialProcess);
                    if (level >= 0) yield return (b.ButtonId, null, level, i == 0 ? "main" : $"main:{i}");
                }
            }
            if (b.LongPressAction?.Type == "dial")
            {
                int level = LevelFor(b.LongPressAction.DialTarget, b.LongPressAction.DialProcess);
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
