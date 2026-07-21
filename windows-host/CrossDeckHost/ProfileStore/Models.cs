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
    public string AccentColor { get; set; } = "#00E5FF"; // matches Resources/Colors.xaml Brush.SignalCyan

    [JsonPropertyName("runOnBoot")]
    public bool RunOnBoot { get; set; } = false;
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
}

/// <summary>A button's position is its index within Profile.Buttons (filtered to its
/// ParentFolderId) — no explicit coordinates; the deck auto-wraps at a fixed column count.</summary>
public class ButtonModel
{
    [JsonPropertyName("buttonId")]
    public string ButtonId { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("action")]
    public ActionModel Action { get; set; } = new();

    /// <summary>Optional second action fired by long-pressing the button on the phone.</summary>
    [JsonPropertyName("longPressAction")]
    public ActionModel? LongPressAction { get; set; }

    [JsonPropertyName("parentFolderId")]
    public string? ParentFolderId { get; set; }
}

/// <summary>
/// Rather than full polymorphic JSON (which needs a custom System.Text.Json converter), this
/// flattens all possible action fields onto one class, discriminated by Type.
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

    [JsonPropertyName("mouseX")]
    public int? MouseX { get; set; }

    [JsonPropertyName("mouseY")]
    public int? MouseY { get; set; }

    [JsonPropertyName("mouseButton")]
    public string? MouseButton { get; set; }

    /// <summary>Optional override icon for a long-press action or a multi-action step.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>Optional override label for a long-press action or a multi-action step.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }
}
