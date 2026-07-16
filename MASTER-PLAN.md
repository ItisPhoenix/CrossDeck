# CrossDeck — Master Plan & Reference

**Status as of this doc:** Milestones 1 and 2 (Core Feature Parity) mostly COMPLETE. Batch 1, Multi-profile management, Multi-action buttons, and Dial/slider gestures are fully done. UDP discovery is partially done. Next up: 2g Auto-profile-switch.

App name: **CrossDeck** ✅ — already used throughout the codebase (`CrossDeckHost`, `com.crossdeck.client`). Do a manual domain/trademark check before publishing — no live search was available to verify this for you. Rejected candidates: HotkeyHub, PanelPilot, ButtonBridge, MacroLink.

---

## 1. Concept

Your Android phone becomes a Stream-Deck-style button grid. The Windows PC is the controlled target. Full functional parity with Elgato Stream Deck's software behavior — not a UI/branding copy of phonedeck.io (that site was never accessible to browse; this plan is built from known Stream Deck / phone-macro-deck app patterns).

---

## 2. Competitive Positioning & Strategy

You want real adoption, not just a personal tool (explicit choice). Direct competitors identified: **Macro Deck** (open source, WiFi/USB, plugin marketplace for OBS/Spotify/Discord — closest match to this project, arguably ahead of where our Milestone 3 lands), **Bitfocus Companion** (pro/enterprise, browser-based, built for broadcast hardware — different shape, not real competition for this use case), **Deckboard** (native apps, QR pairing, free tier + paywalled pro tier).

**What's actually different here, honestly assessed:**
- **Dial/slider as a first-class touch gesture** (swipe-to-adjust), not a touchscreen button grid pretending to be hardware. Most phone-deck clones replicate the physical-button metaphor 1:1; leaning into genuinely touch-native interaction (swipe between folder pages, haptic feedback, drag-to-reorder) is a real, defensible angle if pushed consistently through the UI, not just the one dial feature.
- **No plugin marketplace = small, fully auditable codebase.** Real trade-off (less out-of-the-box than Macro Deck), not a pure win. No third-party plugin supply-chain surface, whole thing readable in an afternoon.
- **Zero telemetry, permanently, structurally** (decision #23) — no analytics SDK ever, not even opt-in-default-off. Real trust signal against tools that phone home.
- **No freemium, ever** — MIT, nothing gated (Deckboard has a paid tier; this one structurally can't add one without abandoning the license model).

**What it honestly lacks vs. the competition:** no plugin ecosystem, no native OBS/Spotify/Discord integration, zero real users, zero battle-testing, one bug already found in the first 20 minutes of use. Macro Deck has years of hardening this doesn't have yet.

**Audience:** deliberately not chosen (decision #24) — resolved at the app level instead via a first-run preset picker (Streaming / Productivity / Blank), so each user self-selects rather than the product picking a lane. Non-streamer productivity users (accessibility use cases, daily desktop macros for spreadsheets/CAD/dev tools) are a real underserved niche none of the three competitors above are chasing — worth leaning into in messaging even with the preset picker covering both.

**Unprompted honest caveat:** code quality and positioning are maybe 10% of what determines adoption. Distribution — a good README, a demo GIF, posting in the right place at the right time — is the other 90%, and that's a separate skill from anything happening in this repo. Worth going into Milestone 3 ship-prep with that in mind.

---

## 3. Full Decision Log

| # | Decision | Locked Answer |
|---|---|---|
| 1 | Core concept | Phone = deck, PC = controlled target |
| 2 | Editing location | Both phone and PC, kept in sync (PC authoritative store) |
| 3 | Distribution | Open source, no app store, manual GitHub Releases |
| 4 | License | MIT |
| 5 | Windows Host run mode | Tray app (session-bound), not a background service |
| 6 | Auto-switch profile on focused app change | MVP |
| 7 | Dial/slider gesture (volume/brightness) | MVP |
| 8 | Folders (sub-pages) | MVP |
| 9 | Multi-action buttons (sequence w/ delays) | MVP |
| 10 | Text snippet action | MVP |
| 11 | Grid layout | Dynamic, auto-fits phone screen; canonical position stored as abstract (row, col) |
| 12 | Icons | Built-in icon pack + custom upload, via separate asset endpoint (not embedded in profile JSON) |
| 13 | Min Android version | Android 12+ (API 31+) |
| 14 | Windows version support | Windows 10 + 11 both |
| 15 | Repo structure | Monorepo (host + client + docs, one repo) |
| 16 | Network mode | Router-based WiFi only (same LAN). No hotspot fallback in v1 |
| 17 | Multi-device scope | One phone ↔ one PC only in v1 |
| 18 | Third-party integrations | None prioritized — generic hotkey/launch/URL actions cover it |
| 19 | Auto-update mechanism | Skip for v1 — manual GitHub Releases |
| 20 | Dev environment | Android Studio + Visual Studio/VS Code with .NET — confirmed ready |
| 21 | Windows Host UI framework | **WPF** (finalized over WinUI 3 — no MSIX packaging, no Windows App SDK runtime, ships as one portable .exe, fits no-code-signing plan) |
| 22 | Code delivery | Claude builds and delivers files here; you push to your own GitHub repo when ready (not pushed automatically) |
| 23 | Telemetry/analytics | **Never, permanently.** No analytics SDK ever, no opt-in toggle added later either — matches the privacy-first positioning |
| 24 | Target audience | Deferred to app level — first-run preset picker (Streaming / Productivity / Blank) on Windows Host, flows to phone via existing `profile_sync`, no new sync mechanism needed |
| 25 | Plugin marketplace (revisited given adoption goal) | Reaffirms #18 — stays out of scope. Action-type dispatch (`ActionModel.Type` string) kept extensible so a future plugin loader isn't a rewrite, but no marketplace built now — real plugin ecosystems are nearly a second project |
| 26 | Outside contributions (issues/PRs) | Decide later — no CONTRIBUTING.md yet |
| 27 | Delivery process | Flat file drops, no git commit history from Claude; **batched testing** — multiple M2 subtasks bundled per check-in, not one test round per subtask |
| 28 | Start on Windows login | MVP, opt-in via tray menu checkbox, **default OFF** (auto-starting background apps without asking undercuts the privacy-first positioning) |
| 29 | App name | **CrossDeck** — already used in all code (`CrossDeckHost` assembly, `com.crossdeck.client` Android package, `CrossDeck` repo root). Do a trademark/domain check before first public release. |

---

## 4. Prerequisites

**Standing constraint — applies to every milestone, not just M1:** Claude's build environment is Linux-only with no Windows GUI and no Android SDK network access. Every milestone gets written by hand, not compiled or run by Claude. You are the only one who can actually build/test any of this. Report exact errors (build error text, crash logs, wrong behavior) and fixes come fast — but nothing gets verified before it reaches you.

**Windows PC**
- Windows 10 (1809+) or Windows 11
- .NET 8 SDK
- Visual Studio 2022 (Community) with ".NET desktop development" workload
- Git for Windows
- Allow the app through Windows Firewall when prompted on first run

**Android phone — physical device, not emulator** (emulator WiFi/mDNS is unreliable for this)
- Android 12+ (API 31+)
- Developer Options + USB Debugging enabled
- USB cable for first install, or wireless ADB debugging
- Same WiFi network as the PC during testing

**Android Studio setup**
- SDK Platform 34 (latest stable) + Platform 31
- Latest SDK Build-Tools
- JDK 17 (bundled with Android Studio — verify in SDK Manager → SDK Tools)

**Network**
- PC and phone on the same router/WiFi
- Disable "AP Isolation" / "Client Isolation" on the router if present — #1 cause of "can't find PC" bugs
- Test on home network first; corporate/public WiFi often blocks mDNS/multicast

---

## 5. Architecture

```
┌───────────────────────────┐         WiFi (LAN, router)        ┌────────────────────────────┐
│   Android Client           │ <────────────────────────────────> │   Windows Host              │
│  - Grid UI (dynamic)       │        WebSocket, JSON, token auth  │  - Tray app (WPF)           │
│  - Profile editor          │                                     │  - WS Server (raw TCP +     │
│  - Local cache (offline)   │        mDNS/NSD discovery            │    manual handshake)        │
│  - Icon store               │        or manual IP + QR pairing    │  - Action Execution Engine  │
└───────────────────────────┘                                     │  - Profile Store (JSON,     │
                                                                    │    authoritative)           │
                                                                    │  - Auto-Profile Watcher     │
                                                                    │  - Asset HTTP endpoint      │
                                                                    └────────────────────────────┘
```

**Why raw TCP + manual WebSocket handshake instead of `HttpListener`:** `HttpListener` requires Administrator privileges or a `netsh http add urlacl` reservation for any prefix other than exactly `http://localhost/` — a dealbreaker for a consumer app people just double-click to run. `TcpListener` has no such restriction. Already implemented this way in `Server/WebSocketServer.cs`.

### Sync Model (resolves decision #2)

- PC holds the authoritative profile store (JSON file on disk).
- Any edit — from phone or PC — is sent as a `profile_edit` message over the WebSocket.
- PC applies the edit, persists it, then broadcasts `profile_sync` with the full updated profile to all connected clients.
- Conflict handling for v1: last-write-wins per button. No merge logic needed until multi-phone support (deferred past v1).
- Phone edits while disconnected: queue locally, replay on reconnect; PC's `profile_sync` response is final truth.

### Grid Position Semantics (resolves ambiguity between dynamic phone layout + PC editor)

Button `position` is stored as abstract `(row, col)` in an unbounded logical grid — canonical, synced value.
- **Phone:** computes how many columns fit current screen width, reflows the logical grid into that many visual columns at render time.
- **PC editor:** shows a reference grid with a configurable column count, editing the same logical `(row, col)` values.
- Only the visual wrap differs between the two — expected and fine.

### Custom Icon Asset Transfer (resolves why icons can't just ride in profile_edit JSON)

- PC Host exposes a local HTTP endpoint (`http://<host-ip>:<port>/assets/`) for icon upload/download, authenticated with the pairing token.
- Profile JSON stores only a reference: `"icon": "asset_7f3a.png"` (content-hash filename avoids collisions).
- Upload flow: image picked → resized locally → POSTed to `/assets/` → Host stores it, returns hash filename → that filename goes into `profile_edit` like any other field.
- Both sides fetch missing assets from the endpoint on demand and cache locally.

### First-Run Preset Picker (resolves decision #24 — audience deferred to app level)

- Lives on the **Windows Host** (new picker window, same pattern as the existing `PairingWindow`), shown on first launch before the sample profile is created.
- Three choices: Streaming, Productivity, Blank. Selected preset becomes `ProfileStoreService.Current` on first run.
- No new sync mechanism needed — flows to the phone automatically through the existing `profile_sync` message.
- "Streaming" preset leans on generic hotkeys (e.g. common OBS/Twitch keyboard shortcuts configured as `hotkey` actions) since there's no native OBS plugin integration (decision #18/#25 stand).

### Security & Distribution Note

The Windows Host simulates input (`SendInput`) and polls the foreground window (auto-profile-switch) — the same behavioral pattern antivirus heuristics use to flag keyloggers. Combined with no code signing:
- Expect a Windows SmartScreen warning on first run (unsigned .exe) — normal for unsigned open source binaries.
- Possible AV false-positives early on, before the binary builds reputation.
- Mitigation: README needs an upfront "Why does this app need these permissions?" section. Revisit a signing cert later if adoption justifies the cost.

---

## 6. Wire Protocol

Transport: WebSocket, `ws://<host-ip>:<port>/ws`. Default port `7890`. All messages are JSON with a `type` field; unknown fields must be ignored by both sides.

**Auth (first message on every connection, always):**
```json
{ "type": "auth", "pin": "483920" }
```
```json
{ "type": "auth_ok", "token": "a1b2c3d4-...", "hostName": "DESKTOP-ABC123" }
```
```json
{ "type": "auth_failed", "reason": "invalid_pin" }
```
Reconnect using saved token instead of PIN: `{ "type": "auth", "token": "..." }`

**Profile sync (sent right after auth, and on any change):**
```json
{
  "type": "profile_sync",
  "profile": {
    "profileId": "p_default",
    "name": "Default",
    "buttons": [
      { "buttonId": "b_001", "position": {"row":0,"col":0}, "label": "Mute",
        "icon": null, "action": {"type":"hotkey","keys":["VolumeMute"]} }
    ]
  }
}
```

**Button press:**
```json
{ "type": "button_press", "buttonId": "b_001", "pressType": "short" }
```
```json
{ "type": "ack", "buttonId": "b_001", "status": "ok" }
```

**Profile edit (Milestone 2):**
```json
{ "type": "profile_edit", "profileId": "p_default", "op": "update_button", "button": {...} }
```
Server re-broadcasts `profile_sync` after applying.

**Action types:**

| type | fields | status |
|---|---|---|
| `hotkey` | `keys: string[]` | ✅ Milestone 1 — done, fixed (extended-key flag bug), **retested and confirmed working** |
| `launch_app` | `path: string` | ✅ Milestone 1 — done, confirmed working |
| `media_control` | `mediaCommand: string` (PlayPause/NextTrack/PrevTrack/VolumeUp/VolumeDown/VolumeMute) | ✅ Milestone 2 — done |
| `open_url` | `url: string` | ✅ Milestone 2 — done |
| `run_command` | `command: string` | ✅ Milestone 2 — done |
| `text_snippet` | `text: string` | ✅ Milestone 2 — done |
| `open_folder` | `targetFolderId: string` | ✅ Milestone 2 — done (folder nav + back button on both sides) |
| `multi_action` | `actions: [...], delays: [...]` | ⬜ Not started |

**Additional wire messages added:**

| type | direction | purpose |
|---|---|---|
| `profile_list` | server → client | lists all profiles + `activeProfileId` on connect and on any profile set change |
| `profile_switch` | client → server | switch active profile by ID |
| `profile_create` | client → server | create new profile with a name |
| `profile_delete` | client → server | delete a profile by ID |
| `profile_rename` | client → server | rename a profile |
| `heartbeat` | server → client | app-level keepalive every 25 s (replaces WebSocket ping/pong to avoid stream race condition) |

---

## 7. Repo Structure

```
/appname
  /windows-host/HostApp        .NET 8 WPF tray app
    /Actions                    action execution engine (hotkey, launch_app done; rest M2)
    /Server                     WebSocketServer.cs, PairingManager.cs
    /ProfileStore                Models.cs, ProfileStore.cs (JSON persistence)
    /Tray                        TrayIconManager.cs
  /android-client               Kotlin + Jetpack Compose
    /connection                  ConnectionManager.kt (WS client), DiscoveryManager.kt (NSD stub, not wired)
    /model                       Profile.kt (data classes matching protocol)
    /ui                          PairingScreen.kt, DeckGridScreen.kt
  /shared-schema/protocol.md    wire protocol — source of truth for both sides
  /docs                         this doc + prior architecture notes
  LICENSE (MIT), README.md, .gitignore
```

---

## 8. Milestone Plan — Full Detail

### Milestone 1 — Foundation (COMPLETE)

Goal: prove the wire protocol works end-to-end before adding features.

- [x] Pairing: PIN shown in Windows tray window, phone enters IP+port+PIN manually
- [x] WebSocket connect + token-based auth + reconnect-with-token
- [x] Single hardcoded profile (3 buttons), PC-authoritative, sent via `profile_sync`
- [x] Phone renders profile read-only in dynamic grid, tap sends `button_press`
- [x] PC executes `hotkey` (SendInput) and `launch_app` (Process.Start), sends `ack`
- [x] Bug found + fixed: media/volume hotkeys needed `KEYEVENTF_EXTENDEDKEY` flag (launch_app unaffected, which is why only Notepad worked initially)
- [x] **Confirmed by user retest: Mute + Volume Up both work.** Milestone 1 fully closed.

### Milestone 2 — Core Feature Parity

Goal: everything that makes this an actual Stream Deck replacement, not just a proof of concept. 9 subtasks, delivered in 3 batches (decision #27 — batched testing, not per-subtask).

**Batch 1 (2a–2c): ✅ COMPLETE**

**2a. Bidirectional profile editing + sync** ✅
- [x] Windows: full editor window — 5×3 grid view, click cell to edit/create button, assign all action types, browse for app path
- [x] Android: editor mode on grid screen — tap-to-edit dialog, create/delete buttons
- [x] Both: `profile_edit` → PC applies → broadcasts `profile_sync` to all clients
- [x] Start-on-Windows-login opt-in checkbox in tray menu (default OFF, per decision #28)
- _Note: offline edit queue not implemented — phone edit goes straight through WS; queuing deferred_

**2b. Remaining action types** ✅
- [x] `media_control` (PlayPause / NextTrack / PrevTrack / VolumeUp / VolumeDown / VolumeMute)
- [x] `open_url` (ShellExecute)
- [x] `run_command` (cmd /c)
- [x] `text_snippet` (SendInput character-by-character)
- [x] `open_folder` (folder navigation — see 2c)

**2c. Folders** ✅
- [x] `open_folder` action type with `targetFolderId`
- [x] PC editor: "Enter Folder in Grid" button + breadcrumb + Back navigation
- [x] Android: folder tap navigates into subfolder, back button/history stack
- [x] `parentFolderId` field on `ButtonModel` — buttons scoped to a folder ID

**Batch 2 (2d–2f):**

**2d. Multi-profile management** ✅ _(scope changed from "multi-action buttons" — moved here because user needed this first)_
- [x] `ProfileSet` model: collection of named profiles, one active at a time
- [x] PC Editor: profile selector dropdown, New / Rename / Delete profile buttons
- [x] Android: profile switcher dropdown in grid header, create/rename/delete dialogs
- [x] `profile_list` + `profile_switch` + `profile_create` + `profile_delete` + `profile_rename` messages wired end-to-end
- [x] Bug fixed: editor was hardcoding `"p_default"` as target profile ID — now uses `ActiveProfileId`
- [x] Visual save feedback on PC (green ✓ / red ✗ status label, 2 s auto-dismiss)
- [x] Android Snackbar feedback (green on `profile_sync` content change, red on `ack` error)

**2d-original. Multi-action buttons** ✅
- [x] `multi_action` action type: ordered list of sub-actions + delay-in-ms between each
- [x] ActionExecutor runs sequencing/delay asynchronously via `Task.Delay`

**2e. UDP auto-discovery pairing** 🔄 _(partially done — replaces mDNS/NSD plan with simpler UDP broadcast)_
- [x] Windows: `DiscoveryBeacon.cs` — UDP broadcast on port 7891 every 2 s (`{"type":"crossdeck_beacon","ip":"...","port":7890}`)
- [x] Android: `ConnectionManager` — UDP scan on port 7891, auto-fills IP+port on discover
- [x] `PairingScreen`: "Scan" button triggers discovery, status label shows scanning/found/timeout
- [x] SharedPreferences persist last-used IP+port+PIN so user doesn't re-enter on same PC
- [ ] Full mDNS/NSD client-side discovery (optional upgrade — UDP beacon covers the use case)

**2f. Dial/slider gesture** ✅
- [x] Phone: Tap a dial action button to open a sleek horizontal slider modal popup
- [x] Smooth real-time absolute value adjustments throttled to prevent network overload
- [x] PC volume and brightness controls: WASAPI COM for system volume, dual-mode DDC/CI + WMI for screen brightness (works on both laptops and desktop monitors)

**2g. Auto-profile-switch** ⬜
- Windows: `AutoProfileWatcher` — poll foreground window (`GetForegroundWindow` + process name) on a timer, map process name → profile ID via a user-configured rule list, switch `ProfileStoreService.Current` and broadcast `profile_sync` on match

**Batch 3 (2h–2i):**

**2h. QR pairing** ⬜
- Windows: `PairingWindow` renders a QR code encoding IP+port+PIN (needs a QR generation library, e.g. QRCoder — already noted as commented-out in `HostApp.csproj`)
- Android: camera-based QR scan on `PairingScreen` as an alternative to manual entry (CameraX + ML Kit barcode scanning, or a lighter-weight QR-only library)

**2i. First-run preset picker** ⬜
- Windows: new picker window (Streaming / Productivity / Blank), shown once on first launch, sets initial `ProfileStoreService.Current`
- Define the three preset profiles' actual button sets when this task starts
- No Android-side work — flows through existing `profile_sync`

### Milestone 3 — Assets, Polish, Ship

**3a. Icon system**
- Windows: implement the `/assets/` HTTP endpoint (upload/download, token-authenticated)
- Both: built-in icon pack (bundle a permissively-licensed set, e.g. Lucide), custom upload UI, local caching by hash

**3b. Polish**
- Reconnect handling (exponential backoff, visible "reconnecting…" state instead of dumping back to pairing screen)
- Offline state (phone shows last-known profile greyed out when disconnected, per original spec)
- Dark/light theme
- "Revoke device" UI on the Windows side (pairing manager already has `RevokeToken`, just needs a settings UI)

**3c. Ship prep**
- ~~Finalize app name~~ **CrossDeck** ✅ — do the trademark/domain check before publishing
- ~~Rename `com.appname.client` package and `HostApp` assembly to the real name~~ Already done — code uses `com.crossdeck.client` and `CrossDeckHost`
- Write the "why does this app need these permissions" README section (flagged in Security note above)
- Generate real launcher icons (Android) and app icon (Windows) — both currently use system defaults
- First GitHub release (manual, per decision #19)
- Demo GIF/screenshots for README — distribution matters as much as code (see Competitive Positioning section)

---

## 9. Additions Made Along the Way

Things built that weren't in the original milestone plan, added in response to real testing and user feedback:

| What | Where | Why |
|---|---|---|
| Profile rename | PC Editor + Android + wire | User request — needed to label profiles properly |
| UDP discovery beacon | `DiscoveryBeacon.cs` (Windows, port 7891) | Simpler than mDNS — no library dependency, works on all home routers |
| Android pairing auto-scan | `PairingScreen.kt` | Scans on launch, manual "Scan" button, status label — removes need to type IP every time |
| SharedPreferences pairing persistence | `ConnectionManager.kt` | Saves last IP/port/PIN so same PC doesn't need re-entry after app restart |
| Sync bug fix: hardcoded `p_default` | `EditorWindow.xaml.cs` | Save/Delete on PC were always writing to the default profile regardless of which was active |
| PC save feedback label | `EditorWindow.xaml` + `.cs` | Green ✓ / red ✗ status text below Save button, 2 s auto-dismiss via DispatcherTimer |
| Android Snackbar feedback | `DeckGridScreen.kt` + `ConnectionManager.kt` | Green snackbar when `profile_sync` changes buttons (PC edited); red snackbar on `ack` error |
| WebSocket stability fix | `WebSocketServer.cs` + `ConnectionManager.kt` | OkHttp ping/pong timeout on every PC click — root cause: two concurrent `Task.Run` broadcasts racing on same stream + server's `keepAliveInterval` pings bypassing the SemaphoreSlim. Fixed by: fusing both broadcasts into one serialised `BroadcastAllAsync`, disabling `keepAliveInterval`, removing OkHttp `pingInterval`, adding app-level `{"type":"heartbeat"}` every 25 s through the semaphore |
| SemaphoreSlim memory leak fix | `WebSocketServer.cs` | Per-socket semaphores were never removed from `_semaphores` dict on disconnect; now removed and disposed in `finally` |

---

## 10. Immediate Next Action

M2 Batch 1 complete. Profile management (multi-profile CRUD) complete. UDP discovery pairing complete. WebSocket stability fixed.

**Options for what to build next:**
1. **2f Auto-profile-switch** — high-value feature (foreground app → auto-switch profile); self-contained Windows-only change
2. **2d-original Multi-action buttons** — `multi_action` action type with delays; purely server-side
3. **2e complete mDNS** — upgrade UDP beacon to proper mDNS/NSD (optional, UDP already works)
4. **2h QR pairing** — connection ergonomics; needs QRCoder on Windows + CameraX on Android

Pick whichever you want to do next.
