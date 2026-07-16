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

    public void LoadOrCreateDefault()
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

        // Fresh initialization
        Set = new ProfileSet
        {
            ActiveProfileId = "p_default",
            Profiles = new List<Profile> { CreateSampleProfile() }
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

    public void DeleteProfile(string profileId)
    {
        if (profileId == "p_default") return; // Keep default safe

        var profile = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (profile != null)
        {
            Set.Profiles.Remove(profile);
            if (Set.ActiveProfileId == profileId)
            {
                Set.ActiveProfileId = "p_default";
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

    private static Profile CreateSampleProfile() => new()
    {
        ProfileId = "p_default",
        Name = "Default",
        Buttons = new List<ButtonModel>
        {
            new()
            {
                ButtonId = "b_001",
                Position = new Position { Row = 0, Col = 0 },
                Label = "Mute",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "VolumeMute" } }
            },
            new()
            {
                ButtonId = "b_002",
                Position = new Position { Row = 0, Col = 1 },
                Label = "Notepad",
                Action = new ActionModel { Type = "launch_app", Path = "notepad.exe" }
            },
            new()
            {
                ButtonId = "b_003",
                Position = new Position { Row = 0, Col = 2 },
                Label = "Volume Up",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "VolumeUp" } }
            }
        }
    };
}
