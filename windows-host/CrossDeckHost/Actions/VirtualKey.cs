namespace CrossDeckHost.Actions;

/// <summary>
/// Maps the key names used in the wire protocol (shared-schema/protocol.md) to Windows virtual
/// key codes. Extend this table as more hotkeys are needed — it's intentionally just a lookup,
/// no need to touch SendInput code when adding keys.
/// </summary>
public static class VirtualKey
{
    private static readonly Dictionary<string, ushort> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Media / volume
        ["VolumeMute"] = 0xAD,
        ["VolumeDown"] = 0xAE,
        ["VolumeUp"] = 0xAF,
        ["MediaNextTrack"] = 0xB0,
        ["MediaPrevTrack"] = 0xB1,
        ["MediaStop"] = 0xB2,
        ["MediaPlayPause"] = 0xB3,

        // Modifiers
        ["Ctrl"] = 0x11,
        ["Alt"] = 0x12,
        ["Shift"] = 0x10,
        ["Win"] = 0x5B,

        // Common
        ["Enter"] = 0x0D,
        ["Escape"] = 0x1B,
        ["Tab"] = 0x09,
        ["Space"] = 0x20,
        ["Backspace"] = 0x08,
        ["Delete"] = 0x2E,

        // Function keys
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
    };

    static VirtualKey()
    {
        // A-Z (VK codes for letters match their ASCII uppercase codes: 0x41-0x5A)
        for (char c = 'A'; c <= 'Z'; c++)
            Map[c.ToString()] = (ushort)c;

        // 0-9 (VK codes 0x30-0x39)
        for (char c = '0'; c <= '9'; c++)
            Map[c.ToString()] = (ushort)c;
    }

    public static bool TryGetCode(string keyName, out ushort code) => Map.TryGetValue(keyName, out code);

    private static readonly Dictionary<ushort, string> Reverse = new();

    /// <summary>Reverse lookup (VK code -> protocol key name) for the macro recorder.</summary>
    public static bool TryGetName(ushort code, out string name)
    {
        if (Reverse.Count == 0)
        {
            foreach (var kv in Map)
                Reverse.TryAdd(kv.Value, kv.Key);
        }
        return Reverse.TryGetValue(code, out name!);
    }

    /// <summary>
    /// Windows volume/media keys are "extended keys" — real hardware sends them as 0xE0-prefixed
    /// scan codes. SendInput needs KEYEVENTF_EXTENDEDKEY set for these or the system audio driver
    /// silently ignores the input (no error, no effect — this was the bug behind "hotkeys don't
    /// work" while launch_app worked fine). See ActionExecutor.KeyInput.
    /// </summary>
    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0xAD, // VolumeMute
        0xAE, // VolumeDown
        0xAF, // VolumeUp
        0xB0, // MediaNextTrack
        0xB1, // MediaPrevTrack
        0xB2, // MediaStop
        0xB3, // MediaPlayPause
    };

    public static bool IsExtended(ushort code) => ExtendedKeys.Contains(code);
}
