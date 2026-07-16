# [App Name TBD] — Finalized Architecture & Spec (v2)

Name placeholder used throughout: **AppName**. Candidates parked: HotkeyHub, PanelPilot, ButtonBridge, MacroLink — decide + do a manual domain/trademark check before publishing.

---

## Decision Log (locked — no more assumptions)

| # | Decision | Answer |
|---|---|---|
| 1 | Core concept | Phone = the deck (like Stream Deck hardware). PC = controlled target. Full functional parity with Stream Deck, not phonedeck.io's exact UI/branding. |
| 2 | Editing location | **Both phone and PC can edit, kept in sync.** PC is the authoritative store; edits from either side push through PC in real time. |
| 3 | Distribution | Open source, no app store. Manual GitHub Releases. |
| 4 | License | MIT |
| 5 | Windows Host run mode | Tray app (session-bound), not a background service |
| 6 | Auto-switch profile on focused app change | MVP |
| 7 | Dial/slider gesture (swipe = adjust volume/brightness) | MVP |
| 8 | Folders (sub-pages) | MVP |
| 9 | Multi-action buttons (sequence w/ delays) | MVP |
| 10 | Text snippet action | MVP |
| 11 | Grid layout | Dynamic, auto-fits phone screen |
| 12 | Icons | Built-in icon pack + custom upload |
| 13 | Min Android version | Android 12+ (API 31+) |
| 14 | Windows version support | Windows 10 + 11 both |
| 15 | Repo structure | Monorepo (host + client + docs, one repo) |
| 16 | Network mode | Router-based WiFi only (same LAN). No hotspot fallback in v1. |
| 17 | Multi-device scope | One phone ↔ one PC only in v1. Multi-PC-per-phone and multi-phone-per-PC deferred. |
| 18 | Third-party integrations (OBS/Spotify/Discord) | None prioritized — generic hotkey/launch/URL actions cover it. Revisit only if a real need shows up. |
| 19 | Auto-update mechanism | Skip for v1 — manual GitHub Releases. Add "check for update" prompt later if wanted. |
| 20 | Dev environment | Android Studio + Visual Studio/VS Code with .NET already installed and ready |

**Note on decision #6 vs #17 vs #16:** auto-switch-profile MVP status assumed router-based LAN, which is confirmed — no conflict.

---

## Revised MVP Scope

Because folders, multi-action, dial/slider, auto-profile-switch, and text snippets are all now MVP (not phased), this is a **bigger MVP than the original v1 draft**. Recommend building it in the internal sub-order below even though it all ships as "MVP" — get the wire protocol and sync model solid before layering features on top, since bidirectional edit-sync is the part most likely to cause bugs if rushed.

**Internal build order within MVP:**
1. Pairing (QR/PIN) + WebSocket connection + auth token
2. Single flat profile, PC-authoritative, read-only display on phone (prove the wire works)
3. Bidirectional edit sync (edit on either side, PC persists, both sides update live)
4. Core action types: hotkey, launch app, media control, open URL, run command, text snippet
5. Folders (nested pages)
6. Multi-action (action sequences with delays)
7. Dial/slider gesture → volume/brightness
8. Auto-profile-switch on focused window change
9. Icon pack + custom upload
10. Polish: reconnect handling, offline state, dark/light theme

---

## Architecture (unchanged core, sync model refined)

```
┌───────────────────────────┐         WiFi (LAN, router)        ┌────────────────────────────┐
│   Android Client           │ <────────────────────────────────> │   Windows Host              │
│  - Grid UI (dynamic)       │        WebSocket, JSON, token auth  │  - Tray app                 │
│  - Profile editor          │                                     │  - WS Server                │
│  - Local cache (offline)   │        mDNS/NSD discovery            │  - Action Execution Engine  │
│  - Icon store               │        or manual IP + QR pairing    │  - Profile Store (authoritative, JSON) │
└───────────────────────────┘                                     │  - Auto-Profile Watcher     │
                                                                    │  - Action Execution Engine  │
                                                                    └────────────────────────────┘
```

### Sync Model (resolves decision #2)

- **PC holds the authoritative profile store** (JSON files on disk).
- Any edit — from phone or PC — is sent as a `profile_edit` message over the WebSocket.
- PC applies the edit, persists it, then broadcasts a `profile_sync` message with the updated profile back to all connected clients (in v1 that's just the one phone, but this also sets up cleanly for #17 later).
- **Conflict handling for v1:** last-write-wins per button (simple, fine for single-phone-single-PC scope). No merge logic needed until multi-phone (deferred).
- If phone edits while disconnected: queue locally, replay on reconnect, PC's `profile_sync` response is final truth (phone reconciles to it).

### Grid Position Semantics (fixes ambiguity between dynamic phone layout + PC editor)

Button `position` is stored as abstract `(row, col)` in an unbounded logical grid — not tied to any physical screen size. This is the canonical value synced between phone and PC.

- **Phone (dynamic, auto-fit):** computes how many columns fit the current screen width, then reflows/wraps the logical grid into that many visual columns at render time. Logical `(row, col)` never changes from a phone rotation or different device — only the *visual* wrap does.
- **PC editor:** shows a reference grid using a configurable column count (default e.g. 5, user-adjustable in editor settings) purely as an editing convenience — it's editing the same logical `(row, col)` values, just visualized differently than the phone's auto-fit.
- Net effect: both sides edit the same source of truth; only the on-screen wrap differs, which is expected and fine.

### Custom Icon Asset Transfer (separate from profile_edit sync)

Do not embed image bytes in `profile_edit`/`profile_sync` JSON messages — bloats the WebSocket channel and blocks fast UI updates. Instead:

- PC Host exposes a small local HTTP endpoint (e.g. `http://<host-ip>:<port>/assets/`) for icon upload/download, authenticated with the same pairing token (as a header or query param).
- Profile JSON stores only an icon reference: `"icon": "asset_7f3a.png"` (content-hash-based filename avoids collisions).
- Upload flow: phone (or PC) picks image → resized locally → POSTed to `/assets/` → Host stores it → returns the hash filename → that filename goes into the `profile_edit` message like any other field.
- Both sides fetch missing assets from the same endpoint on demand and cache locally.

```json
// Phone -> PC edit
{
  "type": "profile_edit",
  "profileId": "p_default",
  "op": "update_button",
  "button": { "buttonId": "b_042", "label": "Mute", "action": {...} }
}

// PC -> all clients, after persisting
{
  "type": "profile_sync",
  "profileId": "p_default",
  "profile": { ...full updated profile... }
}
```

---

## Repo Structure (monorepo)

```
/appname
  /windows-host          # .NET (C#) WPF/WinUI3 tray app
    /Actions              # action execution engine (hotkey, launch, media, url, cmd, text)
    /Server                # WebSocket server, pairing/auth
    /ProfileStore           # JSON persistence, sync broadcast logic
    /AutoProfileWatcher      # foreground window polling
  /android-client         # Kotlin + Jetpack Compose
    /ui/grid                # dynamic grid, folders, dial gesture
    /editor                  # profile/button editor (mirrors PC editor capabilities)
    /connection              # discovery, pairing, WebSocket client, offline queue
    /icons                    # built-in icon pack + custom upload handling
  /shared-schema           # JSON schema / protocol docs shared by both (source of truth for message formats)
  /docs                     # this spec, decision log, protocol docs
  LICENSE                   # MIT
  README.md
```

---

## Tech Stack (confirmed)

| Layer | Choice |
|---|---|
| Windows Host UI | .NET 8 (C#) + WPF — finalized over WinUI 3 (no MSIX packaging, no Windows App SDK runtime, ships as one portable .exe, fits no-code-signing distribution plan) |
| Windows WS Server | `System.Net.WebSockets`, in-process |
| Android Client | Kotlin + Jetpack Compose |
| Android Networking | OkHttp (WebSocket) + `NsdManager` (mDNS discovery) |
| Persistence | JSON files (both sides); PC copy is authoritative |
| Icons | Bundle open, permissively-licensed icon set (e.g. Lucide) + custom upload, resized ~144×144 |
| Min targets | Android API 31+ (Android 12), Windows 10 (1809+) and 11 |
| License | MIT |

---

## Security & Distribution Note

The Windows Host simulates input (`SendInput`) and polls the foreground window (for auto-profile-switch) — the same behavioral pattern antivirus heuristics use to flag keyloggers. Combined with no code signing (decision #19, GitHub Releases only), expect:

- Windows SmartScreen "Windows protected your PC" warning on first run (unsigned .exe) — normal for unsigned open source binaries, not a bug.
- Possible AV false-positive flags on some antivirus products, especially early after release before the binary builds reputation.

Mitigation for v1: README must include an explicit, upfront "Why does this app need these permissions?" section explaining `SendInput` (to trigger hotkeys/media keys) and foreground-window polling (for auto-profile-switch), plus a note that the code is open source and auditable. This is a transparency/trust measure, not a technical fix — a self-signed or paid code-signing cert can be revisited later if adoption grows enough to justify the cost.

## Prerequisites (for you to build/run/test on your own machines)

**Windows PC**
- Windows 10 (1809+) or Windows 11
- .NET 8 SDK
- Visual Studio 2022 (Community) with ".NET desktop development" workload
- Git for Windows
- Allow the app through Windows Firewall when prompted on first run

**Android phone — physical device recommended, not emulator** (emulator WiFi/mDNS is unreliable for this)
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
- Disable "AP Isolation" / "Client Isolation" on the router if present — most common cause of "can't find PC"
- Test on home network first; corporate/public WiFi often blocks mDNS/multicast

**Delivery method:** Claude builds the full monorepo and delivers as files here. User pushes to their own GitHub repo when ready (not pushed automatically).

## Next Step

Scaffold the monorepo: solution/project skeletons for `windows-host` (.NET) and `android-client` (Gradle/Compose), plus `/shared-schema` with the protocol JSON schemas above already written out. Say the word and I'll generate it.
