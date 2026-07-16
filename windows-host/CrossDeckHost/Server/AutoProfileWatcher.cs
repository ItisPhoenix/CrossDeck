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
