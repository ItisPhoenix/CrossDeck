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

    public event Action<string, bool?, int?>? StateChanged;

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
                StateChanged?.Invoke(b.ButtonId, playing, null);
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
                StateChanged?.Invoke(b.ButtonId, muted, null);
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
            StateChanged?.Invoke(_lastFocusedButtonId, false, null);
        if (focusedButtonId != null)
            StateChanged?.Invoke(focusedButtonId, true, null);

        _lastFocusedButtonId = focusedButtonId;
    }

    private void PollDialLevels()
    {
        foreach (var b in CurrentButtons())
        {
            if (b.Action.Type != "dial") continue;

            int level = b.Action.DialTarget switch
            {
                "volume" => DialController.GetVolume(),
                "brightness" => DialController.GetBrightness(),
                _ => -1
            };
            if (level < 0) continue;

            if (_lastDialLevels.TryGetValue(b.ButtonId, out var last) && last == level) continue;
            _lastDialLevels[b.ButtonId] = level;
            StateChanged?.Invoke(b.ButtonId, null, level);
        }
    }

    private IEnumerable<ButtonModel> CurrentButtons() => _profileStore.Current.Buttons ?? Enumerable.Empty<ButtonModel>();

    /// <summary>Full current-state snapshot for every live-state-capable button, sent when a phone connects.</summary>
    public IEnumerable<(string ButtonId, bool? Active, int? Level)> GetSnapshot()
    {
        foreach (var b in CurrentButtons())
        {
            if (b.Action.Type == "media_control" && b.Action.MediaCommand == "PlayPause")
                yield return (b.ButtonId, _lastPlaying, null);
            else if (b.Action.Type == "media_control" && b.Action.MediaCommand == "VolumeMute")
                yield return (b.ButtonId, _lastMuted, null);
            else if (b.Action.Type == "launch_app" && !string.IsNullOrWhiteSpace(b.Action.Path))
                yield return (b.ButtonId, b.ButtonId == _lastFocusedButtonId, null);
            else if (b.Action.Type == "dial")
            {
                int level = b.Action.DialTarget switch
                {
                    "volume" => DialController.GetVolume(),
                    "brightness" => DialController.GetBrightness(),
                    _ => -1
                };
                if (level >= 0) yield return (b.ButtonId, null, level);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
