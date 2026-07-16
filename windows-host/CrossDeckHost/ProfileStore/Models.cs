using System.Text.Json.Serialization;

namespace CrossDeckHost.ProfileStore;

public class ProfileSet
{
    [JsonPropertyName("activeProfileId")]
    public string ActiveProfileId { get; set; } = "p_default";

    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = new();

    [JsonPropertyName("presetSelected")]
    public bool PresetSelected { get; set; } = false;

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#00d4ff";
}

public class Profile
{
    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = "p_default";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    [JsonPropertyName("triggerProcess")]
    public string? TriggerProcess { get; set; }

    [JsonPropertyName("buttons")]
    public List<ButtonModel> Buttons { get; set; } = new();

    [JsonPropertyName("rows")]
    public int Rows { get; set; } = 3;

    [JsonPropertyName("columns")]
    public int Columns { get; set; } = 5;
}

public class ButtonModel
{
    [JsonPropertyName("buttonId")]
    public string ButtonId { get; set; } = "";

    [JsonPropertyName("position")]
    public Position Position { get; set; } = new();

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("action")]
    public ActionModel Action { get; set; } = new();

    [JsonPropertyName("parentFolderId")]
    public string? ParentFolderId { get; set; }
}

public class Position
{
    [JsonPropertyName("row")]
    public int Row { get; set; }

    [JsonPropertyName("col")]
    public int Col { get; set; }
}

/// <summary>
/// Milestone 1 supports "hotkey" and "launch_app" only. Rather than full polymorphic JSON
/// (which needs a custom System.Text.Json converter), this flattens all possible fields onto
/// one class, discriminated by Type. Simpler for now — revisit with a proper converter in
/// Milestone 2 when action types multiply (media_control, open_url, run_command, text_snippet,
/// multi_action).
/// </summary>
public class ActionModel
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "hotkey";

    /// <summary>Used when Type == "hotkey". Key names matching CrossDeckHost.Actions.VirtualKey.</summary>
    [JsonPropertyName("keys")]
    public List<string>? Keys { get; set; }

    /// <summary>Used when Type == "launch_app".</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("mediaCommand")]
    public string? MediaCommand { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("targetFolderId")]
    public string? TargetFolderId { get; set; }

    [JsonPropertyName("actions")]
    public List<ActionModel>? Actions { get; set; }

    [JsonPropertyName("delays")]
    public List<int>? Delays { get; set; }

    [JsonPropertyName("dialTarget")]
    public string? DialTarget { get; set; }
}
