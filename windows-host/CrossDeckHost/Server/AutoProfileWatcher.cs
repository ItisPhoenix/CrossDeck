using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;
using CrossDeckHost.ProfileStore;

namespace CrossDeckHost.Server;

public class AutoProfileWatcher
{
    private readonly ProfileStoreService _profileStore;
    private readonly System.Timers.Timer _timer;
    private string? _lastProcessName;
    private bool _isManuallyLocked;
    private string? _lastProfileId;
    private bool _editorOpen;

    public AutoProfileWatcher(ProfileStoreService profileStore)
    {
        _profileStore = profileStore;
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnTimerElapsed;

        // Listen for profile changes to detect manual user overrides
        _profileStore.ProfileChanged += OnProfileChanged;
        _lastProfileId = _profileStore.Set.ActiveProfileId;
    }

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>While the editor is open, the user is actively managing profiles/buttons by hand
    /// — auto-switching out from under them (e.g. because a screenshot tool or notification
    /// briefly stole foreground focus) silently yanks the active profile away mid-edit, so every
    /// click on a button from the profile they just selected looks up against the wrong profile
    /// and opens blank. Auto-switching resumes once the editor closes.</summary>
    public void SetEditorOpen(bool isOpen) => _editorOpen = isOpen;

    private void OnProfileChanged(Profile profile)
    {
        // If the profile changed but the active process did not change,
        // it means the user manually selected a profile. Lock it!
        if (profile.ProfileId != _lastProfileId)
        {
            _lastProfileId = profile.ProfileId;
            _isManuallyLocked = true;
        }
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_editorOpen) return;

        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string processName;
            using (var proc = Process.GetProcessById((int)pid))
            {
                processName = proc.ProcessName.ToLower();
            }

            // Standardize format to process.exe
            if (!processName.EndsWith(".exe"))
            {
                processName += ".exe";
            }

            // If active process changes, unlock auto-switching
            if (processName != _lastProcessName)
            {
                _lastProcessName = processName;
                _isManuallyLocked = false;
            }

            if (_isManuallyLocked) return;

            // Find matching profile rule
            var matchedProfile = _profileStore.Set.Profiles.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.TriggerProcess) &&
                p.TriggerProcess.Trim().ToLower() == processName);

            if (matchedProfile != null)
            {
                if (_profileStore.Set.ActiveProfileId != matchedProfile.ProfileId)
                {
                    _lastProfileId = matchedProfile.ProfileId;
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        _profileStore.SwitchProfile(matchedProfile.ProfileId);
                    });
                }
            }
            else
            {
                // Fallback to p_default if no rule matches
                if (_profileStore.Set.ActiveProfileId != "p_default" &&
                    _profileStore.Set.Profiles.Any(p => p.ProfileId == "p_default"))
                {
                    _lastProfileId = "p_default";
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        _profileStore.SwitchProfile("p_default");
                    });
                }
            }
        }
        catch
        {
            // Ignore process read / query access errors (e.g. system idle process)
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
