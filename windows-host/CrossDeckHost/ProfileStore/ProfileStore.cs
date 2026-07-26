using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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
                var (migrated, migratedJson) = MigrateLegacyPositions(json);
                var (dialsMigrated, dialsJson) = MigrateDialButtons(migratedJson);
                migrated = migrated || dialsMigrated;
                var loaded = JsonSerializer.Deserialize<ProfileSet>(dialsJson, _jsonOptions);
                if (loaded is not null && loaded.Profiles.Count > 0)
                {
                    Set = loaded;
                    // Backfilling icons only happens inside SaveLocked (mutation-triggered) — a
                    // profile loaded as-is with buttons saved before an icon existed for their
                    // type (e.g. multi_action before it got its own icon instead of a grid mosaic)
                    // would otherwise stay iconless until the next unrelated edit touched it.
                    bool iconsAssigned = AutoAssignIcons();
                    if (migrated || iconsAssigned) SaveLocked();
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

    /// <summary>Buttons saved without an icon get a builtin one matching their action type.
    /// Returns true if any button was actually backfilled, so callers that don't already save
    /// unconditionally (LoadOrCreateDefault) know whether they need to persist the change.</summary>
    private bool AutoAssignIcons()
    {
        bool changed = false;
        foreach (var profile in Set.Profiles)
        {
            foreach (var b in profile.Buttons.Concat(profile.Dials))
            {
                if (!string.IsNullOrEmpty(b.Icon)) continue;
                // Deliberately no fallback for multi_action here — the grid tile falls back to a
                // live step mosaic when Icon is unset (see EditorWindow.RebuildGrid /
                // DeckGridScreen's DeckButton), same as 0.3.1-beta. A static icon would
                // permanently win over that mosaic since AutoAssignIcons runs on every save.
                b.Icon = DefaultBuiltinIconFor(b.Action);
                if (b.Icon != null) changed = true;
            }
        }
        return changed;
    }

    /// <summary>The builtin icon a fresh action of this type gets when nothing custom is set —
    /// shared between AutoAssignIcons (the button's own icon) and the long-press chain badge's
    /// per-segment mosaic (EditorWindow.BuildBadgeGlyphContent), which needs the same fallback for
    /// steps that never had their own icon picked.</summary>
    public static string? DefaultBuiltinIconFor(ActionModel action) => action.Type switch
    {
        "hotkey" => "builtin:keyboard",
        "media_control" => action.MediaCommand switch
        {
            "PlayPause" => "builtin:play",
            "NextTrack" => "builtin:skip-forward",
            "PrevTrack" => "builtin:skip-back",
            "VolumeMute" => "builtin:volume-x",
            _ => "builtin:volume-2"
        },
        "launch_app" => "builtin:zap",
        "open_url" => "builtin:globe",
        "run_command" => "builtin:terminal",
        "text_snippet" => "builtin:file-text",
        "macro" => "builtin:disc",
        "open_folder" => "builtin:folder",
        "dial" => action.Actions is { Count: > 0 } ? "builtin:layers" : action.DialTarget switch
        {
            "brightness" => "builtin:sun",
            "mic" => "builtin:mic",
            _ => "builtin:volume-2"
        },
        _ => null
    };

    /// <summary>One-time migration: pre-redesign saves have explicit position.row/col coordinates
    /// that no longer exist in the model. Reorders each profile's buttons array by (row, then col)
    /// before deserializing into the new position-less ButtonModel, so a button's order — now the
    /// only thing that determines its place in the auto-flow grid — matches its old visual layout
    /// instead of whatever arbitrary order the file happens to list buttons in.</summary>
    private static (bool Migrated, string Json) MigrateLegacyPositions(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return (false, json); }
        if (root is not JsonObject rootObj || rootObj["profiles"] is not JsonArray profiles) return (false, json);

        bool anyMigrated = false;
        foreach (var profileNode in profiles.OfType<JsonObject>())
        {
            if (profileNode["buttons"] is not JsonArray buttons) continue;
            var buttonObjects = buttons.OfType<JsonObject>().ToList();
            if (!buttonObjects.Any(b => b.ContainsKey("position"))) continue;
            anyMigrated = true;

            int RowOf(JsonObject b) => (b["position"] as JsonObject)?["row"]?.GetValue<int>() ?? 0;
            int ColOf(JsonObject b) => (b["position"] as JsonObject)?["col"]?.GetValue<int>() ?? 0;
            var sorted = buttonObjects.OrderBy(RowOf).ThenBy(ColOf).ToList();

            buttons.Clear();
            foreach (var b in sorted) buttons.Add(b);
        }

        return anyMigrated ? (true, root.ToJsonString()) : (false, json);
    }

    /// <summary>One-time migration: dials used to be plain grid buttons (action.type == "dial").
    /// The dial strip moved them into their own Dials list, so any surviving
    /// grid button of that type is pulled out — in place, preserving order — the first time an
    /// old profiles.json loads. Same shape-sniffing pattern as MigrateLegacyPositions.</summary>
    private static (bool Migrated, string Json) MigrateDialButtons(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return (false, json); }
        if (root is not JsonObject rootObj || rootObj["profiles"] is not JsonArray profiles) return (false, json);

        bool anyMigrated = false;
        foreach (var profileNode in profiles.OfType<JsonObject>())
        {
            if (profileNode["buttons"] is not JsonArray buttons) continue;
            var dialButtons = buttons.OfType<JsonObject>()
                .Where(b => (b["action"] as JsonObject)?["type"]?.GetValue<string>() == "dial")
                .ToList();
            if (dialButtons.Count == 0) continue;
            anyMigrated = true;

            var existingDials = (profileNode["dials"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new();
            foreach (var d in dialButtons) buttons.Remove(d);
            var dialsArray = new JsonArray();
            foreach (var d in existingDials) dialsArray.Add(d.DeepClone());
            foreach (var d in dialButtons) dialsArray.Add(d.DeepClone());
            profileNode["dials"] = dialsArray;
        }

        return anyMigrated ? (true, root.ToJsonString()) : (false, json);
    }

    private CancellationTokenSource? _saveDebounceCts;

    // The real choke point — every mutator ends here, unlike Save() which not all of them called.
    private void SaveLocked()
    {
        AutoAssignIcons();
        _saveDebounceCts?.Cancel();
        _saveDebounceCts = new CancellationTokenSource();
        var ct = _saveDebounceCts.Token;
        var json = JsonSerializer.Serialize(Set, _jsonOptions);
        var filePath = _filePath;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, ct);
                File.WriteAllText(filePath, json);
            }
            catch (OperationCanceledException) { }
            catch { /* best effort save */ }
        });
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

    /// <summary>Dials are stored in Profile.Dials rather than Profile.Buttons but otherwise use
    /// the exact same ButtonModel + mutators — this picks which list a given edit targets.</summary>
    private static List<ButtonModel> TargetList(Profile profile, string list) =>
        list == "dials" ? profile.Dials : profile.Buttons;

    public void UpdateButton(string profileId, ButtonModel updatedButton, string list = "buttons")
    {
        lock (_lock)
        {
            var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (target == null) return;
            var items = TargetList(target, list);

            // A button's grid position is just its index in this list (see ButtonModel's own
            // doc comment) — replacing in place keeps it where it was. Remove-then-Add moved
            // every edited button to the end of its folder scope on every single edit.
            var index = items.FindIndex(b => b.ButtonId == updatedButton.ButtonId);
            if (index >= 0)
                items[index] = updatedButton;
            else
                items.Add(updatedButton);
            SaveLocked();
        }
        NotifyChanged();
    }

    public void DeleteButton(string profileId, string buttonId, string list = "buttons")
    {
        bool changed = false;
        lock (_lock)
        {
            var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (target == null) return;
            var items = TargetList(target, list);

            var existing = items.FirstOrDefault(b => b.ButtonId == buttonId);
            if (existing != null)
            {
                items.Remove(existing);
                SaveLocked();
                changed = true;
            }
        }
        if (changed) NotifyChanged();
    }

    /// <summary>Reorders one folder scope's buttons to match orderedButtonIds — used by both the
    /// Windows editor's own drag-and-drop and the buttons_reorder message from Android. Cross-scope
    /// interleaving in the underlying list doesn't matter (each scope renders independently,
    /// filtered by ParentFolderId), only within-scope order does, so the whole scope is just
    /// removed and re-appended in its new order rather than reordered in place. Applies equally
    /// to list == "dials" — dials are folder-scoped exactly like buttons.</summary>
    public void ReorderButtons(string profileId, string? parentFolderId, List<string> orderedButtonIds, string list = "buttons")
    {
        lock (_lock)
        {
            var target = Set.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (target == null) return;
            var items = TargetList(target, list);

            var scopeButtons = items.Where(b => b.ParentFolderId == parentFolderId).ToList();
            var byId = scopeButtons.ToDictionary(b => b.ButtonId);
            var reordered = orderedButtonIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            foreach (var b in scopeButtons)
            {
                if (!reordered.Contains(b)) reordered.Add(b);
            }

            items.RemoveAll(b => b.ParentFolderId == parentFolderId);
            items.AddRange(reordered);
            SaveLocked();
        }
        NotifyChanged();
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
                Label = "Google",
                Icon = ExtractAndSaveIcon("chrome.exe") ?? "builtin:globe",
                Action = new ActionModel { Type = "open_url", Url = "https://google.com" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_002",
                Label = "Lock PC",
                Icon = "builtin:lock",
                Action = new ActionModel { Type = "run_command", Command = "rundll32.exe user32.dll,LockWorkStation" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_003",
                Label = "Volume",
                Icon = "builtin:volume-2",
                Action = new ActionModel { Type = "dial", DialTarget = "volume" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_004",
                Label = "Brightness",
                Icon = "builtin:sun",
                Action = new ActionModel { Type = "dial", DialTarget = "brightness" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_005",
                Label = "Play/Pause",
                Icon = "builtin:play",
                Action = new ActionModel { Type = "media_control", MediaCommand = "PlayPause" }
            });
            // Row 2 additions
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_006",
                Label = "Mute Meetings",
                Icon = "builtin:mic-off",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F1" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_007",
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
                Label = "Mute",
                Icon = "builtin:volume-x",
                Action = new ActionModel { Type = "media_control", MediaCommand = "VolumeMute" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_002",
                Label = "Volume",
                Icon = "builtin:volume-2",
                Action = new ActionModel { Type = "dial", DialTarget = "volume" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_003",
                Label = "Notepad",
                Icon = ExtractAndSaveIcon("notepad.exe") ?? "builtin:file-text",
                Action = new ActionModel { Type = "launch_app", Path = "notepad.exe" }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_004",
                Label = "Snip Tool",
                Icon = ExtractAndSaveIcon("SnippingTool.exe") ?? "builtin:camera",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Win", "Shift", "S" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_005",
                Label = "Play/Pause",
                Icon = "builtin:play",
                Action = new ActionModel { Type = "media_control", MediaCommand = "PlayPause" }
            });
            // Row 2 additions
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_006",
                Label = "OBS Record",
                Icon = ExtractAndSaveIcon("obs64.exe") ?? "builtin:disc",
                Action = new ActionModel { Type = "hotkey", Keys = new List<string> { "Ctrl", "Shift", "F9" } }
            });
            profile.Buttons.Add(new ButtonModel
            {
                ButtonId = "b_007",
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
            if (!File.Exists(fullPath))
            {
                // A bare name with no extension is most likely a running process's name (the
                // app-volume mixer's shape) — resolve it via the running process itself.
                if (Path.GetExtension(exeNameOrPath).Length == 0)
                {
                    try
                    {
                        using var proc = System.Diagnostics.Process.GetProcessesByName(exeNameOrPath).FirstOrDefault();
                        var procPath = proc?.MainModule?.FileName;
                        if (procPath != null) fullPath = procPath;
                    }
                    catch { /* access denied on an elevated process, etc. — fall through to null */ }
                }
                if (!File.Exists(fullPath)) return null;
            }

            // Attempt to pull a crisp high-res 256x256 icon from the system shell first
            using (var icon = JumboIcon.ExtractJumbo(fullPath) ?? System.Drawing.Icon.ExtractAssociatedIcon(fullPath))
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

    /// <summary>Same shape as <see cref="ExtractAndSaveIcon"/> but for a "uwp:" ExePath — no .exe
    /// to read a classic icon resource from, so this goes through the Shell's AppsFolder tile
    /// image instead (see UwpIcon).</summary>
    public static string? ExtractAndSaveUwpIcon(string appUserModelId)
    {
        try
        {
            using var tile = UwpIcon.ExtractTile(appUserModelId);
            if (tile == null) return null;

            // Every icon on the grid — Discord/Brave/Docker's .ico-derived jumbo icons included —
            // fills its own square canvas at ~full bleed; the client applies one fixed icon-to-
            // container ratio uniformly to all of them (DeckGridScreen.kt's iconSize = cellSize *
            // 0.42), so a smaller host-side inset here would just make THIS icon look smaller than
            // every other one instead of matching them. Only the corner rounding is host-side
            // (a plain square would still look like a color block among circular/shield logos).
            const int canvasSize = 256;
            const float cornerRadiusFraction = 0.18f;
            var cornerRadius = canvasSize * cornerRadiusFraction;
            using var padded = new System.Drawing.Bitmap(canvasSize, canvasSize);
            using (var g = System.Drawing.Graphics.FromImage(padded))
            using (var clipPath = RoundedRectPath(0, 0, canvasSize, canvasSize, cornerRadius))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SetClip(clipPath);
                g.DrawImage(tile, 0, 0, canvasSize, canvasSize);
            }

            using var ms = new MemoryStream();
            padded.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return SaveIconFromBytes(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(float x, float y, float width, float height, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
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
    public static byte[] ResizeToIconPng(byte[] src, int size = 256)
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
        var path = Path.Combine(assetsDir, hash + ".png");
        // Skip write if already cached — avoids concurrent callers racing on the same file handle.
        if (!File.Exists(path))
        {
            try { File.WriteAllBytes(path, resized); }
            catch (IOException) { /* another thread just won the same write — file exists now */ }
        }

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

        // Google's endpoint first: favicon.ico on most sites is a tiny legacy 16x16/32x32 icon
        // that just looks blurry once upscaled to our 144x144 tile size. Google's sz=256 request
        // actually returns a real higher-res source image, not a client-side upscale.
        string[] candidates =
        {
            $"https://www.google.com/s2/favicons?domain={host}&sz=256",
            $"https://{host}/favicon.ico"
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
