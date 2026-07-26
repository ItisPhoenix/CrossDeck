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

    /// <summary>Dials live in their own list, not the grid — same ButtonModel shape,
    /// position = index. Action is the dial itself; LongPressAction is what a tap fires.</summary>
    [JsonPropertyName("dials")]
    public List<ButtonModel> Dials { get; set; } = new();
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

    /// <summary>Fired by tapping a DIAL specifically (a dial has no separate "main tap action"
    /// the way a grid button does — dragging IS the main action, so this is what tap fires). Grid
    /// buttons no longer expose this in the editor (one action per button, matching Stream
    /// Deck).</summary>
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

    /// <summary>Used by multi_action/macro for their steps. Also reused for a dial stack: when
    /// Type == "dial" and this is set (non-empty), each entry is a full dial layer (its own
    /// DialTarget/DialProcess/Icon/Label) and tapping the dial cycles between them — same list,
    /// different meaning depending on the parent's Type.</summary>
    [JsonPropertyName("actions")]
    public List<ActionModel>? Actions { get; set; }

    [JsonPropertyName("delays")]
    public List<int>? Delays { get; set; }

    [JsonPropertyName("dialTarget")]
    public string? DialTarget { get; set; }

    /// <summary>Used when DialTarget == "app_volume" to bind the dial to one process's
    /// session. Null means "open the live multi-app mixer" (existing behaviour).</summary>
    [JsonPropertyName("dialProcess")]
    public string? DialProcess { get; set; }

    /// <summary>Used when DialTarget == "keystroke_step" — each drag detent fires one of these
    /// (Up when dragging up, Down when dragging down) instead of setting an absolute 0-100
    /// level. Same key-name format as Keys (CrossDeckHost.Actions.VirtualKey).</summary>
    [JsonPropertyName("dialStepUpKeys")]
    public List<string>? DialStepUpKeys { get; set; }

    [JsonPropertyName("dialStepDownKeys")]
    public List<string>? DialStepDownKeys { get; set; }

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

    /// <summary>Resolves which dial layer is actually being addressed — Actions[stackIndex] for
    /// a stacked dial (Actions non-empty), or this action itself for a plain unstacked dial.
    /// Clamped so an out-of-range index (e.g. a profile edited to fewer layers than a still-
    /// connected client remembers) falls back to the last layer instead of throwing.</summary>
    public ActionModel ResolveDialLayer(int stackIndex) =>
        Actions is { Count: > 0 } layers ? layers[Math.Clamp(stackIndex, 0, layers.Count - 1)] : this;
}
