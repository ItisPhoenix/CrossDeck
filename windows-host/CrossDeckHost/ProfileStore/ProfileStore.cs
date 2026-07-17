using System.IO;
using System.Text.Json;

namespace CrossDeckHost.ProfileStore;

public class ProfileStoreService
{
    private readonly string _filePath;
    private readonly string _oldFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // Guards Set + profiles.json against the WebSocket receive loop, the editor UI thread, and
    // AutoProfileWatcher's timer all calling mutators concurrently. Held only around
    // mutate+serialize+write; NotifyChanged() fires after release so event handlers never run
    // with the lock held.
    private readonly object _lock = new();

    public ProfileSet Set { get; private set; } = new();

    public Profile Current => Set.Profiles.FirstOrDefault(p => p.ProfileId == Set.ActiveProfileId) ?? Set.Profiles.First();

    public event Action<Profile>? ProfileChanged;
    public event Action<ProfileSet>? ProfileSetChanged;

    public ProfileStoreService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CrossDeckHost");
        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, "profiles.json");
        _oldFilePath = Path.Combine(appDataDir, "profile.json");
    }

    public void LoadOrCreateDefault(string preset = "Blank")
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<ProfileSet>(json, _jsonOptions);
                if (loaded is not null && loaded.Profiles.Count > 0)
                {
                    Set = loaded;
                    return;
                }
            }

            // Migrate from old profile.json if exists
            if (File.Exists(_oldFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_oldFilePath);
                    var oldProfile = JsonSerializer.Deserialize<Profile>(json, _jsonOptions);
                    if (oldProfile is not null)
                    {
                        Set = new ProfileSet
                        {
                            ActiveProfileId = oldProfile.ProfileId,
                            Profiles = new List<Profile> { oldProfile }
                        };
                        SaveLocked();
                        File.Delete(_oldFilePath);
                        return;
                    }
                }
                catch { }
            }

            // Fresh initialization using selected preset
            Set = new ProfileSet
            {
                ActiveProfileId = "p_default",
                Profiles = new List<Profile> { CreatePresetProfile(preset) }
            };
            SaveLocked();
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(Set, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void SwitchProfile(string profileId)
    {
        bool changed;
        lock (_lock)
        {
            changed = Set.Profiles.Any(p => p.ProfileId == profileId);
            if (changed)
            {
                Set.ActiveProfileId = profileId;
                SaveLocked();
            }
        }
        if (changed) NotifyChanged();
    }

    public void CreateProfile(string name)
    {
        lock (_lock)
        {
            var newId = $"p_{Guid.NewGuid().ToString().Substring(0, 8)}";
            var newProfile = new Profile
            {
                ProfileId = newId,
                Name = name,
                Buttons = new List<ButtonModel>()
            };
            Set.Profiles.Add(newProfile);
            Set.ActiveProfileId = newId;
            SaveLocked();
        }
        NotifyChanged();
    }

    public void CreateProfileFromPreset(string name, string preset)
    {
        lock (_lock)
        {
            var newId = $"p_{Guid.NewGuid().ToString().Substring(0, 8)}";
            var template = CreatePresetProfile(preset);
            var newProfile = new Profile
            {
                ProfileId = newId,
                Name = name,
                Buttons = template.Buttons
            };
            Set.Profiles.Add(newProfile);
            Set.ActiveProfileId = newId;
            SaveLocked();
        }
        NotifyChanged();
    }

    public void DeleteProfile(string profileId)
    {
        bool changed = false;
        lock (_lock)
        {
            if (Set.Profiles.Count <= 1) return; // Keep at least one profile

            var profile = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (profile != null)
            {
                Set.Profiles.Remove(profile);
                if (Set.ActiveProfileId == profileId)
                {
                    Set.ActiveProfileId = Set.Profiles.First().ProfileId;
                }
                SaveLocked();
                changed = true;
            }
        }
        if (changed) NotifyChanged();
    }

    public void RenameProfile(string profileId, string newName)
    {
        bool changed = false;
        lock (_lock)
        {
            var profile = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (profile != null)
            {
                profile.Name = newName;
                SaveLocked();
                changed = true;
            }
        }
        if (changed) NotifyChanged();
    }

    public void UpdateButton(string profileId, ButtonModel updatedButton)
    {
        lock (_lock)
        {
            var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (target == null) return;

            var existing = target.Buttons.FirstOrDefault(b => b.ButtonId == updatedButton.ButtonId);
            if (existing != null)
            {
                target.Buttons.Remove(existing);
            }
            target.Buttons.Add(updatedButton);
            SaveLocked();
        }
        NotifyChanged();
    }

    public void DeleteButton(string profileId, string buttonId)
    {
        bool changed = false;
        lock (_lock)
        {
            var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (target == null) return;

            var existing = target.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
            if (existing != null)
            {
                target.Buttons.Remove(existing);
                SaveLocked();
                changed = true;
            }
        }
        if (changed) NotifyChanged();
    }

    public void NotifyChanged()
    {
        ProfileChanged?.Invoke(Current);
        ProfileSetChanged?.Invoke(Set);
    }

    public void ResetProfileToPreset(string profileId, string preset)
    {
        lock (_lock)
        {
            var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (target == null) return;

            var template = CreatePresetProfile(preset);
            target.Buttons = template.Buttons;
            SaveLocked();
        }
        NotifyChanged();
    }

    public void SetPresetPicked(string preset)
    {
        lock (_lock)
        {
            var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == Set.ActiveProfileId);
            if (target != null)
            {
                var template = CreatePresetProfile(preset);
                target.Buttons = template.Buttons;
            }
            Set.PresetSelected = true;
            SaveLocked();
        }
        NotifyChanged();
    }

    private static Profile CreatePresetProfile(string preset)
    {
        var profile = new Profile
        {
            ProfileId = "p_default",
            Name = "Default",
            Buttons = new List<ButtonModel>()
        };

        if (preset == "Productivity")
        {
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_001",
                Position = new Position { Row = 0, Col = 0 },
                Label = "Google",
                Icon = ExtractAndSaveIcon("chrome.exe") ?? "builtin:globe",
                Action = new ActionModel { Type = "open_url", Url = "https://google.com" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_002",
                Position = new Position { Row = 0, Col = 1 },
                Label = "Lock PC",
                Icon = "builtin:lock",
                Action = new ActionModel { Type = "run_command", Command = "rundll32.exe user32.dll,LockWorkStation" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_003",
                Position = new Position { Row = 0, Col = 2 },
                Label = "Volume",
                Icon = "builtin:volume-2",
                Action = new ActionModel { Type = "dial", DialTarget = "volume" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_004",
                Position = new Position { Row = 0, Col = 3 },
                Label = "Brightness",
                Icon = "builtin:sun",
                Action = new ActionModel { Type = "dial", DialTarget = "brightness" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_005",
                Position = new Position { Row = 0, Col = 4 },
                Label = "Play/Pause",
                Icon = "builtin:play",
                Action = new ActionModel { Type = "media_control", MediaCommand = "PlayPause" }
            });
            // Row 2 additions
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_006",
                Position = new Position { Row = 1, Col = 0 },
                Label = "Mute Meetings",
                Icon = "builtin:mic-off",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F1" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_007",
                Position = new Position { Row = 1, Col = 1 },
                Label = "Task Manager",
                Icon = ExtractAndSaveIcon("taskmgr.exe") ?? "builtin:cpu",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "Escape" } }
            });
        }
        else if (preset == "Streaming")
        {
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_001",
                Position = new Position { Row = 0, Col = 0 },
                Label = "Mute",
                Icon = "builtin:volume-x",
                Action = new ActionModel { Type = "media_control", MediaCommand = "VolumeMute" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_002",
                Position = new Position { Row = 0, Col = 1 },
                Label = "Volume",
                Icon = "builtin:volume-2",
                Action = new ActionModel { Type = "dial", DialTarget = "volume" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_003",
                Position = new Position { Row = 0, Col = 2 },
                Label = "Notepad",
                Icon = ExtractAndSaveIcon("notepad.exe") ?? "builtin:file-text",
                Action = new ActionModel { Type = "launch_app", Path = "notepad.exe" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_004",
                Position = new Position { Row = 0, Col = 3 },
                Label = "Snip Tool",
                Icon = ExtractAndSaveIcon("SnippingTool.exe") ?? "builtin:camera",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Win", "Shift", "S" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_005",
                Position = new Position { Row = 0, Col = 4 },
                Label = "Play/Pause",
                Icon = "builtin:play",
                Action = new ActionModel { Type = "media_control", MediaCommand = "PlayPause" }
            });
            // Row 2 additions
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_006",
                Position = new Position { Row = 1, Col = 0 },
                Label = "OBS Record",
                Icon = ExtractAndSaveIcon("obs64.exe") ?? "builtin:disc",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F9" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_007",
                Position = new Position { Row = 1, Col = 1 },
                Label = "OBS Stream",
                Icon = ExtractAndSaveIcon("obs64.exe") ?? "builtin:video",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F10" } }
            });
        }

        return profile;
    }

    public static string? ExtractAndSaveIcon(string exeNameOrPath)
    {
        try
        {
            string fullPath = ResolveExecutablePath(exeNameOrPath);
            if (!File.Exists(fullPath)) return null;

            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(fullPath))
            {
                if (icon == null) return null;
                using (var bitmap = icon.ToBitmap())
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return SaveIconFromBytes(ms.ToArray());
                }
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a ButtonModel.Icon value ("builtin:&lt;name&gt;", a custom-upload hash, or
    /// null) to an absolute file path on disk, or null if unset/missing.
    /// </summary>
    public static string? ResolveIconFilePath(string? icon)
    {
        if (string.IsNullOrEmpty(icon)) return null;

        if (icon.StartsWith("builtin:", StringComparison.Ordinal))
        {
            var name = icon["builtin:".Length..];
            var builtinPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Builtin", name + ".png");
            return File.Exists(builtinPath) ? builtinPath : null;
        }

        var assetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrossDeckHost", "Assets");
        var iconPath = Path.Combine(assetsDir, icon + ".png");
        return File.Exists(iconPath) ? iconPath : null;
    }

    /// <summary>
    /// Resizes raw image bytes (any format System.Drawing can decode) to a
    /// square, transparent-padded PNG of the given size. Used so PC uploads,
    /// Android uploads, and extracted exe icons all hash identically for the
    /// same source image.
    /// </summary>
    public static byte[] ResizeToIconPng(byte[] src, int size = 144)
    {
        using var inMs = new MemoryStream(src);
        using var original = System.Drawing.Image.FromStream(inMs);
        using var resized = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(resized))
        {
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.Clear(System.Drawing.Color.Transparent);
            g.DrawImage(original, 0, 0, size, size);
        }
        using var outMs = new MemoryStream();
        resized.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
        return outMs.ToArray();
    }

    /// <summary>
    /// Resizes to 144x144, hashes, and saves under the Assets dir. Returns the
    /// hash filename (no extension) to store in ButtonModel.Icon.
    /// </summary>
    public static string SaveIconFromBytes(byte[] rawBytes)
    {
        byte[] resized = ResizeToIconPng(rawBytes);
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(resized);
        string hash = Convert.ToHexString(hashBytes).ToLower();

        string assetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrossDeckHost", "Assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllBytes(Path.Combine(assetsDir, hash + ".png"), resized);

        return hash;
    }

    private static string ResolveExecutablePath(string exe)
    {
        if (Path.IsPathRooted(exe)) return exe;

        string[] searchDirs = {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"C:\Program Files\Google\Chrome\Application",
            @"C:\Program Files (x86)\Google\Chrome\Application",
            @"C:\Program Files\obs-studio\bin\64bit",
            @"C:\Program Files (x86)\obs-studio\bin\64bit",
        };

        foreach (var dir in searchDirs)
        {
            string testPath = Path.Combine(dir, exe);
            if (File.Exists(testPath)) return testPath;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            string testPath = Path.Combine(dir, exe);
            if (File.Exists(testPath)) return testPath;
        }

        return exe;
    }

    private static readonly System.Net.Http.HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<string?> FetchFaviconIconAsync(string url)
    {
        string host;
        try
        {
            host = new Uri(url).Host;
        }
        catch
        {
            return null;
        }

        string[] candidates =
        {
            $"https://{host}/favicon.ico",
            $"https://www.google.com/s2/favicons?domain={host}&sz=144"
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(candidate);
                if (bytes.Length > 0)
                {
                    return SaveIconFromBytes(bytes);
                }
            }
            catch
            {
                // try the next candidate
            }
        }
        return null;
    }
}
