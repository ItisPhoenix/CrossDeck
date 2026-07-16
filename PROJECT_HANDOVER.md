# CrossDeck — Complete Project Handover

## What Is This

**CrossDeck** is a fully open-source Stream Deck alternative. Your Android phone becomes a customizable button-grid remote control for your Windows PC over local WiFi.

- Repo root: `d:\CrossDeck`
- Both apps build clean as of this handover.
- Monorepo — Windows Host (WPF) + Android Client (Jetpack Compose) in one repo.

---

## Build Status

```
Windows Host:   dotnet build windows-host/CrossDeck.sln
                → Build succeeded. 0 Warning(s). 0 Error(s).

Android Client: $env:JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"; ./gradlew assembleDebug
                → BUILD SUCCESSFUL (37 tasks)
                (4 harmless parameter-naming warnings in ConnectionManager.kt re: OkHttp WebSocketListener)
```

---

## Repo Structure

```
d:\CrossDeck\
├── windows-host\CrossDeckHost\
│   ├── Actions\
│   │   ├── ActionExecutor.cs       Dispatch logic for all action types
│   │   ├── DialController.cs       WASAPI volume + DDC/CI brightness control
│   │   └── VirtualKey.cs           SendInput key name → VK code mapping
│   ├── ProfileStore\
│   │   ├── Models.cs               ProfileSet, Profile, ButtonModel, ActionModel, Position
│   │   └── ProfileStore.cs         JSON persistence (profiles.json on disk)
│   ├── Server\
│   │   ├── WebSocketServer.cs      WebSocket server (TcpListener), all message handling
│   │   ├── PairingManager.cs       PIN generation, token issuance & validation
│   │   ├── DiscoveryBeacon.cs      UDP broadcast on port 7891 every 2 s
│   │   └── AutoProfileWatcher.cs   Foreground window polling → auto profile switch
│   ├── Tray\
│   │   └── TrayIconManager.cs      System tray icon + dark ObsidianMenuRenderer
│   ├── EditorWindow.xaml/.cs       Borderless profile editor + 5×3 drag-drop grid
│   ├── IconPickerWindow.xaml/.cs   Borderless grid picker for the bundled built-in icon pack
│   ├── PairingWindow.xaml/.cs      Borderless pairing window + QR code display
│   ├── PresetPickerWindow.xaml/.cs First-run Streaming/Productivity/Blank preset picker
│   ├── ThemeManager.cs             Global Obsidian dark palette; imperative visual-tree styling (not a ResourceDictionary) via ApplyTheme(window)
│   ├── App.xaml/.cs                Application entry point
│   └── CrossDeckHost.csproj
│
├── android-client\app\src\main\java\com\crossdeck\client\
│   ├── connection\
│   │   ├── ConnectionManager.kt    WebSocket client, all StateFlows, sendStyleChange()
│   │   └── DiscoveryManager.kt     UDP discovery stub
│   ├── model\
│   │   └── Profile.kt              Profile, ButtonModel, ActionModel, Position, ProfileHeader
│   ├── ui\
│   │   ├── DeckGridScreen.kt       Main grid UI, 3D flip transitions, dial bottom-sheet, settings drawer
│   │   └── PairingScreen.kt        Dark pairing UI, radar scan pulse, haptics
│   ├── MainActivity.kt             MaterialTheme driven by live accentColor state
│   └── QrScannerActivity.kt        CameraX + ML Kit QR scanner
│
├── shared-schema\
│   └── protocol.md                 Wire protocol — source of truth for all message formats
├── docs\
│   └── Architecture-Spec.md        Full architecture reference (v3)
├── DESIGN.md                       Obsidian design system tokens + implementation notes
├── MASTER-PLAN.md                  Decision log + full milestone tracker
└── README.md                       Public-facing project overview
```

---

## Tech Stack

| | Windows Host | Android Client |
|---|---|---|
| Language | C# (.NET 8) | Kotlin |
| UI | WPF (borderless) | Jetpack Compose (Material 3) |
| WebSocket | TcpListener + manual handshake | OkHttp |
| Discovery | UDP broadcast on port 7891 | UDP socket listener |
| Persistence | JSON files on disk (authoritative) | SharedPreferences (pairing) |
| Build | dotnet build / Visual Studio 2022 | ./gradlew (JDK: Android Studio bundled JBR) |

---

## Completed Features

### Both Apps
- ✅ WebSocket connection with PIN auth + token-based reconnect
- ✅ All action types: `hotkey`, `launch_app`, `media_control`, `open_url`, `run_command`, `text_snippet`, `open_folder`, `multi_action`, `dial`
- ✅ Multi-profile CRUD — create, rename, delete, switch from either side
- ✅ Bidirectional live profile editing — edit on either side, PC persists, both update
- ✅ Folders — scoped sub-pages with `parentFolderId`, full history stack
- ✅ Dial controls — volume (WASAPI COM) + brightness (DDC/CI / WMI fallback)
- ✅ UDP auto-discovery — Android scans on launch, finds PC without manual IP entry
- ✅ QR pairing — Windows shows QR, Android scans → instant connect
- ✅ Auto-profile-switch — Windows watches foreground process, switches profile automatically
- ✅ First-run preset picker — Streaming / Productivity / Blank (shows once, saved in profiles.json)
- ✅ App-level heartbeat every 25 s (replaces WebSocket ping/pong to avoid OkHttp race)
- ✅ Dynamic accent color sync over WebSocket (5 choices, persists in ProfileSet.AccentColor)
- ✅ Icon system — 94-icon built-in pack + custom image upload, token-authenticated asset server (see Milestone 3 Status)
- ✅ Android reconnect overlay with exponential backoff; Windows tray "Revoke Paired Device"

### Windows Host Specific
- ✅ Borderless WPF windows (`WindowStyle="None"`, `AllowsTransparency="True"`)
- ✅ Custom title bar with drag, minimize, close on all windows
- ✅ ThemeManager.cs — global Obsidian visual-tree styling (`AccentColor` prop + `ApplyTheme(window)`) + dynamic accent updates
- ✅ Custom dark tray context menu (ObsidianMenuRenderer)
- ✅ Start on Windows login opt-in checkbox in tray (default OFF)

### Android Specific
- ✅ Edge-to-edge layout behind system bars
- ✅ Dynamic MaterialTheme — primary color driven by live `accentColor` StateFlow
- ✅ 3D flip card transition on profile switch (600 ms, rotationY, cameraDistance 12× density)
- ✅ Full-screen bottom-sheet dial slider with haptic CLOCK_TICK ticks every 5 units
- ✅ Settings bottom-sheet — 5-color accent picker
- ✅ Pulsing radar ring animation during WiFi scan
- ✅ Haptic feedback: KEYBOARD_TAP on button press, CONFIRM on connect, LONG_PRESS on QR connect

---

## Milestone 3 Status

### 3a. Icon System ✅ — DONE
- Windows: asset server rewritten onto a second manual-`TcpListener` (`_port + 1`, no HttpListener/admin requirement — see `WebSocketServer.cs` class doc comment), hand-parsed HTTP GET + POST on `/assets/`, token-gated via `X-CrossDeck-Token` header or `?token=`.
- Upload resizes to 144×144 (`ProfileStoreService.ResizeToIconPng`/`SaveIconFromBytes`) before SHA256-hashing, so PC and Android uploads of the same image hash identically.
- Icon convention: `ButtonModel.Icon` is `null`, `builtin:<name>` (bundled pack, rendered locally, never fetched), or `<hash>` (custom upload, fetched from `/assets/<hash>`).
- Bundled pack: 94 Lucide icons (`Assets/Builtin/` on PC as Content-copied files enumerated via `Directory.GetFiles`; `assets/builtin/` on Android enumerated via `AssetManager.list`).
- PC: `IconPickerWindow` (built-in grid) + existing Browse/Clear in `EditorWindow`. Android: built-in grid + image-picker upload in `EditButtonDialog` (`DeckGridScreen.kt`), shared `resolveIconBitmap` helper reused by both the grid buttons and the edit-dialog preview.

### 3b. Polish ✅ — DONE
- Android reconnect: `ConnectionManager` exponential backoff (1s→30s cap) on `onFailure`/`onClosed` when a profile was already synced this run; cancelled on `auth_failed`, explicit `disconnect()`, or a fresh manual connect.
- `MainActivity` shows a frosted `ReconnectOverlay` (pulsing spinner + "Manual Connection") over the dimmed, touch-blocked last-known profile instead of jumping straight to `PairingScreen`; still falls back to `PairingScreen` when there's never been a profile or the user taps Manual Connection.
- Windows: tray "Revoke Paired Device" (confirmation dialog) calls `PairingManager.RevokeAllTokens()` + `WebSocketServer.DisconnectAllClients()` + regenerates the pairing PIN. `PairingManager` tokens now persist to `%AppData%\CrossDeckHost\tokens.json` (previously in-memory only, so revokes/tokens didn't survive a host restart).

### 3c. Ship Prep ⬜ — NOT STARTED
- Trademark/domain check on "CrossDeck" before publishing
- Real launcher icons for Android + .ico for Windows (both currently system defaults)
- Demo GIF/screenshots for README
- First GitHub release

---

## Data Models

### Windows Host (C# — `ProfileStore/Models.cs`)

```csharp
class ProfileSet {
    string ActiveProfileId = "p_default";
    List<Profile> Profiles;
    bool PresetSelected = false;
    string AccentColor = "#00d4ff";   // NEW — dynamic theme sync
}

class Profile {
    string ProfileId;
    string Name;
    string? TriggerProcess;           // for auto-profile-switch
    List<ButtonModel> Buttons;
}

class ButtonModel {
    string ButtonId;
    Position Position;                // { row, col } — abstract logical grid
    string Label;
    string? Icon;                     // hash filename, null if no icon
    ActionModel Action;
    string? ParentFolderId;
}

class ActionModel {
    string Type;                      // hotkey | launch_app | media_control | open_url |
                                      // run_command | text_snippet | open_folder |
                                      // multi_action | dial
    List<string>? Keys;
    string? Path;
    string? MediaCommand;
    string? Url;
    string? Command;
    string? Text;
    string? TargetFolderId;
    List<ActionModel>? Actions;       // for multi_action
    List<int>? Delays;                // for multi_action (ms between each)
    string? DialTarget;               // "volume" | "brightness"
}
```

### Android Client (Kotlin — `model/Profile.kt`)

Mirrors exactly the C# model above, as `@Serializable` data classes using kotlinx.serialization.

---

## Wire Protocol Summary

Transport: WebSocket `ws://<host-ip>:7890/ws`. All messages are JSON with a `type` field. Unknown fields ignored (forward compat).

### Message Types

| type | direction | when |
|---|---|---|
| `auth` | client → server | First message always. `{ "pin": "..." }` or `{ "token": "..." }` |
| `auth_ok` | server → client | Auth success. Returns `token` + `hostName` |
| `auth_failed` | server → client | Bad PIN or expired token |
| `profile_sync` | server → client | After auth + on any profile/button change. Includes full profile + `accentColor` |
| `profile_list` | server → client | On connect + profile set changes. Lists all profiles + `activeProfileId` |
| `button_press` | client → server | User tapped a button. `{ "buttonId": "...", "pressType": "short" }` |
| `ack` | server → client | After button press. `{ "status": "ok" \| "error", "message"?: "..." }` |
| `profile_edit` | either → either | Edit a button. `{ "op": "update_button" \| "delete_button", "profileId", "button"? \| "buttonId"? }` |
| `profile_switch` | client → server | Switch active profile. `{ "profileId": "..." }` |
| `profile_create` | client → server | New profile. `{ "name": "..." }` |
| `profile_delete` | client → server | Delete profile. `{ "profileId": "..." }` |
| `profile_rename` | client → server | Rename. `{ "profileId": "...", "name": "..." }` |
| `dial_adjust` | client → server | Set dial value 0–100 or null to query. `{ "buttonId", "value"? }` |
| `dial_state` | server → client | Broadcast after dial_adjust. `{ "buttonId", "value" }` |
| `style_change` | client → server | Change accent color. `{ "accentColor": "#hex" }` |
| `heartbeat` | server → client | Every 25 s. No-op keepalive. `{ "type": "heartbeat" }` |

### profile_sync shape (always includes accentColor)

```json
{
  "type": "profile_sync",
  "profile": {
    "profileId": "p_default",
    "name": "Default",
    "buttons": [
      {
        "buttonId": "b_001",
        "position": { "row": 0, "col": 0 },
        "label": "Mute",
        "icon": null,
        "action": { "type": "hotkey", "keys": ["VolumeMute"] },
        "parentFolderId": null
      }
    ]
  },
  "accentColor": "#00d4ff"
}
```

### UDP Auto-Discovery

- Windows broadcasts UDP to `255.255.255.255:7891` every 2 s.
- Android sends `"CROSSDECK_DISCOVER"` UDP datagram, waits 1.5 s for reply.
- Reply: `{ "ip": "192.168.1.50", "port": 7890, "hostName": "DESKTOP-ABC123" }`

---

## Design System — Obsidian Cyber-Intelligence

Both apps share the same visual language. Key tokens:

```
background:      #080810  (deep void)
surface:         #0E0E10  (near-black panel)
outline:         #1F1F23  (subtle separator border)
on-surface:      #FFFFFF
on-muted:        #9CA3AF

accent (default):  #00d4ff  Neon Cyan
accent options:    #8b5cf6  Neon Purple
                   #ffb703  Cyberpunk Yellow
                   #2ec4b6  Toxic Green
                   #e63946  Crimson Red

border-inactive:   1px solid #1F1F23
border-active:     1.5px solid <accent>
corner-radius:     8dp (buttons) / 12dp (cards) / 16dp (sheets)
```

Rules:
- No light theme. Dark only.
- Borders, not filled colors, for inactive elements.
- Every interactive element has a scale/glow micro-interaction.
- Accent color is user-selectable at runtime and syncs live over WebSocket.

---

## Key Implementation Details

### Accent Color Sync Flow
1. User picks color in Android settings drawer → `connectionManager.sendStyleChange(hex)` → WS `{ "type": "style_change", "accentColor": "#hex" }`
2. `WebSocketServer.cs` handles `style_change` → sets `_profileStore.Set.AccentColor = hex` → saves → calls `BroadcastAllAsync(profile_sync)` which includes `accentColor`
3. Android `ConnectionManager.handleMessage()` parses `accentColor` from `profile_sync` → updates `_accentColor` MutableStateFlow
4. `MainActivity.kt` collects `accentColor` → `Color(android.graphics.Color.parseColor(hex))` → rebuilds `MaterialTheme(colorScheme = PremiumDarkColors.copy(primary = parsedColor))`

### WebSocket Server Notes
- Uses `TcpListener`, NOT `HttpListener` (HttpListener requires admin or netsh reservation)
- Manual WebSocket upgrade handshake implemented in `WebSocketServer.cs`
- `SemaphoreSlim` per-socket prevents concurrent send races
- `keepAliveInterval` DISABLED — app-level `heartbeat` message sent every 25 s instead (OkHttp's built-in ping raced with `SendAsync` on the server stream)
- `BroadcastAllAsync` serialises all broadcasts through the same semaphore

### Android Build Notes
- Java: `C:\Program Files\Android\Android Studio\jbr` (set as `JAVA_HOME` when building from terminal)
- Build: `$env:JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"; ./gradlew assembleDebug`
- `ActionModel` is a `data class` — all fields are `val`. NEVER mutate them after construction. Build with named constructor args.
- `currentFolderId` is `String?` — when adding to `folderHistory: List<Pair<String, String>>`, use `(currentFolderId ?: "") to label`

### Known Non-Issues
- 4 warnings in `ConnectionManager.kt` about OkHttp `WebSocketListener` parameter naming — harmless, pre-existing
- Gradle deprecated features warning about Gradle 10 compatibility — irrelevant for now

---

## Important Files to Read Before Touching

| File | Why |
|---|---|
| `WebSocketServer.cs` | All WebSocket protocol handling. Very large (25 KB). Has SemaphoreSlim concurrency guards. Do NOT add concurrent sends. |
| `DeckGridScreen.kt` | Very large (46 KB). Contains main screen, DeckButton, EmptyEditButton, EditButtonDialog, formatMultiAction, parseMultiAction. |
| `ConnectionManager.kt` | All Android state flows + WebSocket message dispatch. Source of truth for client-side protocol handling. |
| `ProfileStore.cs` | JSON persistence. Manages `profiles.json`. Do NOT bypass this for reads/writes. |
| `ThemeManager.cs` | Global WPF visual-tree styling. Set `ThemeManager.AccentColor = hex`, then call `ThemeManager.ApplyTheme(window)` per open window to change accent at runtime. |
| `shared-schema/protocol.md` | Canonical wire format reference. Update this if any message shape changes. |

---

## What To Work On Next (Prioritized)

3a and 3b are done (see Milestone 3 Status above). Only 3c remains:

### Ship Prep (Milestone 3c)
- Generate a real `.ico` for Windows (currently system default)
- Generate real launcher icon set for Android in `res/mipmap-*/`
- Record a demo GIF
- Trademark/domain check on "CrossDeck"
- Create the first GitHub release

---

## Decisions That Are Locked (Do Not Revisit)

- **No light theme** — dark only, always
- **No plugin marketplace** — `ActionModel.Type` string dispatch stays extensible but no loader built
- **No telemetry** — zero analytics, zero tracking, structurally permanent
- **MIT license** — no freemium, no paywalled features, ever
- **WPF not WinUI 3** — no MSIX packaging, ships as one portable .exe
- **TcpListener not HttpListener** — no admin rights required
- **One phone ↔ one PC in v1** — multi-device deferred
- **UDP discovery not full mDNS** — simpler, works on all home routers
- **Heartbeat not WebSocket ping/pong** — avoids OkHttp concurrent send race
