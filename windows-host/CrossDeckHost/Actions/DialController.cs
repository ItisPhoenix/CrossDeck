using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace CrossDeckHost.Actions;

public static class DialController
{
    public static int SetVolume(int value)
    {
        try
        {
            var volume = GetVolumeObject();
            if (volume == null) return 50;

            float newVolume = Math.Clamp(value / 100f, 0f, 1f);
            volume.SetMasterVolumeLevelScalar(newVolume, Guid.Empty);
            
            return (int)Math.Round(newVolume * 100f);
        }
        catch
        {
            return 50;
        }
    }

    public static int GetVolume()
    {
        try
        {
            var volume = GetVolumeObject();
            if (volume == null) return 50;

            volume.GetMasterVolumeLevelScalar(out float currentVolume);
            return (int)Math.Round(currentVolume * 100f);
        }
        catch
        {
            return 50;
        }
    }

    public static bool? IsMuted()
    {
        try
        {
            var volume = GetVolumeObject();
            if (volume == null) return null;

            volume.GetMute(out bool muted);
            return muted;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the applied level, or -1 if neither DDC/CI nor WMI could actually set
    /// it — callers must not report success on -1; some displays support neither backend.</summary>
    public static int SetBrightness(int value)
    {
        int target = Math.Clamp(value, 0, 100);
        if (SetDdcciBrightness((uint)target)) return target;
        if (SetWmiBrightness(target)) return target;
        return -1;
    }

    public static int GetBrightness()
    {
        int ddcciValue = GetDdcciBrightness();
        if (ddcciValue >= 0) return ddcciValue;
        return GetWmiBrightness();
    }

    private static int GetDdcciBrightness()
    {
        int level = -1;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
        {
            uint count = 0;
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count) && count > 0)
            {
                var monitors = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                {
                    foreach (var m in monitors)
                    {
                        uint min = 0, cur = 0, max = 0;
                        if (GetMonitorBrightness(m.hPhysicalMonitor, ref min, ref cur, ref max))
                        {
                            level = (int)cur;
                        }
                        DestroyPhysicalMonitor(m.hPhysicalMonitor);
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        return level;
    }

    private static bool SetDdcciBrightness(uint value)
    {
        bool success = false;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
        {
            uint count = 0;
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count) && count > 0)
            {
                var monitors = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                {
                    foreach (var m in monitors)
                    {
                        if (SetMonitorBrightness(m.hPhysicalMonitor, value))
                        {
                            success = true;
                        }
                        DestroyPhysicalMonitor(m.hPhysicalMonitor);
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        return success;
    }

    // In-process WMI (not a spawned powershell.exe per call, which is what made brightness
    // laggy compared to volume's direct COM call — process startup alone is ~200ms+ per tick).
    private static int GetWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                    return Convert.ToInt32(mo["CurrentBrightness"]);
            }
        }
        catch { }
        return -1;
    }

    private static bool SetWmiBrightness(int value)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            bool found = false;
            foreach (ManagementObject mo in searcher.Get())
            {
                found = true;
                using (mo)
                    mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, (byte)value });
            }
            return found;
        }
        catch { return false; }
    }

    // DDC/CI Imports
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("dxva2.dll", EntryPoint = "GetNumberOfPhysicalMonitorsFromHMONITOR", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitor", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr hPhysicalMonitor);

    [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, ref uint pdwMinimumBrightness, ref uint pdwCurrentBrightness, ref uint pdwMaximumBrightness);

    [DllImport("dxva2.dll", EntryPoint = "SetMonitorBrightness", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    // WASAPI COM Definitions
    private static IAudioEndpointVolume? GetVolumeObject()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            device.Activate(ref iid, 23, IntPtr.Zero, out var volumeObj);
            return (IAudioEndpointVolume)volumeObj;
        }
        catch
        {
            return null;
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsContext, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    // Vtable order must match the real IAudioEndpointVolume exactly (COM interop calls by slot
    // index, not by name) — the unused Channel*/SetMute members below exist only to keep GetMute
    // at its real slot; they're never called.
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr client);
        int UnregisterControlChangeNotify(IntPtr client);
        int GetChannelCount(out uint channelCount);
        int SetMasterVolumeLevel(float levelDB, Guid eventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        int GetMasterVolumeLevel(out float levelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDB, Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDB);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, Guid eventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
    }

    // Per-app (session) volume control for the multi-volume feature — separate COM path from
    // the master IAudioEndpointVolume above. Fresh lookup every call, same reasoning as
    // GetVolumeObject(): sessions are transient, appearing/disappearing as apps play/stop audio.
    private static IAudioSessionManager2? GetSessionManager()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            var iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            device.Activate(ref iid, 23, IntPtr.Zero, out var mgrObj);
            return (IAudioSessionManager2)mgrObj;
        }
        catch { return null; }
    }

    /// <summary>Enumerates every real (non-system-sounds) audio session's control + owning
    /// process name once — the shared loop behind every per-app query below, so the same
    /// COM enumeration isn't duplicated per call site.</summary>
    private static IEnumerable<(string ProcessName, uint Pid, IAudioSessionControl2 Control)> EnumerateAudioSessions()
    {
        var mgr = GetSessionManager();
        if (mgr == null) yield break;
        if (mgr.GetSessionEnumerator(out var sessionEnum) != 0) yield break;
        if (sessionEnum.GetCount(out int count) != 0) yield break;

        for (int i = 0; i < count; i++)
        {
            string? processName = null;
            uint pid = 0;
            IAudioSessionControl2? session = null;
            try
            {
                // GetSession's QueryInterface can throw per-session, must be inside the guard.
                if (sessionEnum.GetSession(i, out session) != 0) continue;
                if (session.IsSystemSoundsSession() == 0) continue; // S_OK (0) = is system sounds, skip
                if (session.GetProcessId(out pid) != 0) continue;
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch { }
            if (processName != null && session != null) yield return (processName, pid, session);
        }
    }

    private static IAudioSessionControl2? FindSessionControl(string processName) =>
        EnumerateAudioSessions().FirstOrDefault(s => string.Equals(s.ProcessName, processName, StringComparison.OrdinalIgnoreCase)).Control;

    /// <summary>Returns the applied level 0-100, or -1 if the app has no active audio session.</summary>
    public static int SetAppVolume(string processName, int value)
    {
        var control = FindSessionControl(processName);
        if (control is not ISimpleAudioVolume vol) return -1;
        float target = Math.Clamp(value / 100f, 0f, 1f);
        if (vol.SetMasterVolume(target, Guid.Empty) != 0) return -1;
        return (int)Math.Round(target * 100f);
    }

    /// <summary>Returns the current level 0-100, or -1 if the app has no active audio session.</summary>
    public static int GetAppVolume(string processName)
    {
        var control = FindSessionControl(processName);
        if (control is not ISimpleAudioVolume vol) return -1;
        if (vol.GetMasterVolume(out float level) != 0) return -1;
        return (int)Math.Round(level * 100f);
    }

    /// <summary>Returns the applied mute state, or null if the app has no active audio session.</summary>
    public static bool? SetAppMuted(string processName, bool muted)
    {
        var control = FindSessionControl(processName);
        if (control is not ISimpleAudioVolume vol) return null;
        if (vol.SetMute(muted, Guid.Empty) != 0) return null;
        return muted;
    }

    /// <summary>One row per distinct process currently holding a real audio session — the live
    /// app-volume mixer's data source. A process with several sessions is represented by
    /// whichever session <see cref="EnumerateAudioSessions"/> reaches first, matching
    /// Get/SetAppVolume's own single-session-per-process behavior.</summary>
    public record AudioMixerEntry(string ProcessName, int Level, bool Muted, string? Icon);

    // exe path -> icon hash, same reasoning as RunningApps.IconCache: don't re-extract every poll
    // tick. Only successes are cached (unlike RunningApps' own cache) — extraction genuinely fails
    // for some real apps (packaged/Store installs with no extractable icon resource on their exe),
    // and caching that failure would permanently blank an app's row instead of retrying next tick.
    // ConcurrentDictionary since a second subscribed phone runs its own push loop on another thread.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> MixerIconCache = new();

    public static List<AudioMixerEntry> GetAudioMixerSnapshot()
    {
        var result = new List<AudioMixerEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (processName, pid, control) in EnumerateAudioSessions())
        {
            if (!seen.Add(processName)) continue;
            if (control is not ISimpleAudioVolume vol) continue;
            if (vol.GetMasterVolume(out float level) != 0) continue;
            bool muted = vol.GetMute(out bool m) == 0 && m;

            string? icon = null;
            try
            {
                var exePath = Server.RunningApps.GetProcessImagePath(pid);
                if (exePath != null)
                {
                    if (!MixerIconCache.TryGetValue(exePath, out icon))
                    {
                        icon = ProfileStore.ProfileStoreService.ExtractAndSaveIcon(exePath);
                        if (icon != null) MixerIconCache[exePath] = icon;
                    }
                }
            }
            catch { }

            result.Add(new AudioMixerEntry(processName, (int)Math.Round(level * 100f), muted, icon));
        }
        return result;
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int NotImpl_GetAudioSessionControl();
        int NotImpl_GetSimpleAudioVolume();
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int count);
        [PreserveSig]
        int GetSession(int index, out IAudioSessionControl2 session);
    }

    // IAudioSessionControl2 extends IAudioSessionControl (9 methods) with 4 more of its own —
    // vtable order below matches the real interface exactly; unused slots are NotImpl stubs
    // purely to hold position, same convention as IAudioEndpointVolume above.
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        int NotImpl_GetDisplayName();
        int NotImpl_SetDisplayName();
        int NotImpl_GetIconPath();
        int NotImpl_SetIconPath();
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        int NotImpl_SetGroupingParam();
        int NotImpl_RegisterAudioSessionNotification();
        int NotImpl_UnregisterAudioSessionNotification();
        int NotImpl_GetSessionIdentifier();
        int NotImpl_GetSessionInstanceIdentifier();
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
    }
}
