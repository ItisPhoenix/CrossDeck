using System.IO;
using System.Text.Json;

namespace CrossDeckHost.ProfileStore;

public class ProfileStoreService
{
    private readonly string _filePath;
    private readonly string _oldFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

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
                    Save();
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
        Save();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Set, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void SwitchProfile(string profileId)
    {
        if (Set.Profiles.Any(p => p.ProfileId == profileId))
        {
            Set.ActiveProfileId = profileId;
            Save();
            NotifyChanged();
        }
    }

    public void CreateProfile(string name)
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
        Save();
        NotifyChanged();
    }

    public void CreateProfileFromPreset(string name, string preset)
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
        Save();
        NotifyChanged();
    }

    public void DeleteProfile(string profileId)
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
            Save();
            NotifyChanged();
        }
    }

    public void RenameProfile(string profileId, string newName)
    {
        var profile = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (profile != null)
        {
            profile.Name = newName;
            Save();
            NotifyChanged();
        }
    }

    public void UpdateButton(string profileId, ButtonModel updatedButton)
    {
        var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (target == null) return;

        var existing = target.Buttons.FirstOrDefault(b => b.ButtonId == updatedButton.ButtonId);
        if (existing != null)
        {
            target.Buttons.Remove(existing);
        }
        target.Buttons.Add(updatedButton);
        Save();
        NotifyChanged();
    }

    public void DeleteButton(string profileId, string buttonId)
    {
        var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (target == null) return;

        var existing = target.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
        if (existing != null)
        {
            target.Buttons.Remove(existing);
            Save();
            NotifyChanged();
        }
    }

    public void NotifyChanged()
    {
        ProfileChanged?.Invoke(Current);
        ProfileSetChanged?.Invoke(Set);
    }

    public void ResetProfileToPreset(string profileId, string preset)
    {
        var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (target == null) return;

        var template = CreatePresetProfile(preset);
        target.Buttons = template.Buttons;
        Save();
        NotifyChanged();
    }

    public void SetPresetPicked(string preset)
    {
        var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == Set.ActiveProfileId);
        if (target != null)
        {
            var template = CreatePresetProfile(preset);
            target.Buttons = template.Buttons;
        }
        Set.PresetSelected = true;
        Save();
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
                Action = new ActionModel { Type = "open_url", Url = "https://google.com" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_002",
                Position = new Position { Row = 0, Col = 1 },
                Label = "Lock PC",
                Action = new ActionModel { Type = "run_command", Command = "rundll32.exe user32.dll,LockWorkStation" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_003",
                Position = new Position { Row = 0, Col = 2 },
                Label = "Volume",
                Action = new ActionModel { Type = "dial", DialTarget = "volume" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_004",
                Position = new Position { Row = 0, Col = 3 },
                Label = "Brightness",
                Action = new ActionModel { Type = "dial", DialTarget = "brightness" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_005",
                Position = new Position { Row = 0, Col = 4 },
                Label = "Play/Pause",
                Action = new ActionModel { Type = "media_control", MediaCommand = "PlayPause" }
            });
            // Row 2 additions
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_006",
                Position = new Position { Row = 1, Col = 0 },
                Label = "Mute Meetings",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F1" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_007",
                Position = new Position { Row = 1, Col = 1 },
                Label = "Task Manager",
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
                Action = new ActionModel { Type = "media_control", MediaCommand = "VolumeMute" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_002",
                Position = new Position { Row = 0, Col = 1 },
                Label = "Volume",
                Action = new ActionModel { Type = "dial", DialTarget = "volume" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_003",
                Position = new Position { Row = 0, Col = 2 },
                Label = "Notepad",
                Action = new ActionModel { Type = "launch_app", Path = "notepad.exe" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_004",
                Position = new Position { Row = 0, Col = 3 },
                Label = "Snip Tool",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Win", "Shift", "S" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_005",
                Position = new Position { Row = 0, Col = 4 },
                Label = "Play/Pause",
                Action = new ActionModel { Type = "media_control", MediaCommand = "PlayPause" }
            });
            // Row 2 additions
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_006",
                Position = new Position { Row = 1, Col = 0 },
                Label = "OBS Record",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F9" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_007",
                Position = new Position { Row = 1, Col = 1 },
                Label = "OBS Stream",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F10" } }
            });
        }

        return profile;
    }
}
