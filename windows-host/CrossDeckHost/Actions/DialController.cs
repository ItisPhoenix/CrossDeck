using System;
using System.Diagnostics;
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

    public static int SetBrightness(int value)
    {
        int target = Math.Clamp(value, 0, 100);
        if (SetDdcciBrightness((uint)target))
        {
            return target;
        }
        SetWmiBrightness(target);
        return target;
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

    private static int GetWmiBrightness()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness).CurrentBrightness\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return 50;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (int.TryParse(output.Trim(), out int val))
            {
                return val;
            }
            return 50;
        }
        catch
        {
            return 50;
        }
    }

    private static void SetWmiBrightness(int value)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods).WmiSetBrightness(0, {value})\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch { }
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
    }
}
