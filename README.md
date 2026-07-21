# CrossDeck

A Stream-Deck-style app: your Android phone becomes a customizable button deck that controls your Windows PC over local WiFi.

Made by [ItisPhoenix](https://github.com/ItisPhoenix).

<!--
  Demo GIF goes here. Capture steps:
  1. Pair a phone with a running Windows Host (same WiFi).
  2. Screen-record the Android app (Android 11+: swipe-down quick settings > Screen record,
     or `adb shell screenrecord /sdcard/demo.mp4`) covering: pairing, a few button taps, a
     profile switch (3D flip), and opening the dial slider.
  3. Convert to a GIF (e.g. `ffmpeg -i demo.mp4 -vf "fps=12,scale=360:-1" demo.gif`) and drop it
     at docs/demo.gif, then replace this comment with: ![CrossDeck demo](docs/demo.gif)
-->

<!--
  Screenshots: take one of the Windows editor grid and one of the Android deck grid (same
  profile, so they visibly match), save as docs/screenshot-host.png and
  docs/screenshot-client.png, then replace this comment with an image row, e.g.:
  <p float="left">
    <img src="docs/screenshot-host.png" width="420" />
    <img src="docs/screenshot-client.png" width="200" />
  </p>
-->

## Repo Structure

```
/windows-host      .NET 8 / WPF tray app — runs on the PC being controlled
/android-client    Kotlin / Jetpack Compose app — runs on the phone (the "deck")
```

## Features

- **Windows Host**: WebSocket server + borderless WPF tray app + profile editor.
- **Android Client**: Full-screen Jetpack Compose grid, edge-to-edge, dark Obsidian theme.
- **Unified Design Language**: Both apps share the same **Obsidian Cyber-Intelligence** dark tech aesthetic — matching color palettes, typography, and interaction patterns across platforms.
- **Dynamic Accent Colors**: Choose from Neon Cyan, Neon Purple, Cyberpunk Yellow, Toxic Green, or Crimson Red. Color syncs live over WebSocket between both apps.
- **UDP Auto-Discovery**: Scan and connect instantly on the local network (saves last connection).
- **QR Pairing**: Scan a QR code in the Windows pairing window for instant connection.
- **Multi-Profile Management**: Create, rename, delete, and switch profiles from either the Android deck or the PC.
- **3D Profile Transitions**: Animated 3D flip card transition on the Android app whenever profiles are switched.
- **Bidirectional Grid Editor**: Edit actions, labels, folder layout, and grid positions on the fly from either client or host.
- **Folders**: Scoped button pages with nesting navigation hierarchy and back navigation.
- **First-Run Preset Picker**: Select Streaming, Productivity, or Blank preset on first launch.
- **Auto-Profile-Switch**: Windows Host watches the foreground process and switches profiles automatically.
- **Actions Engine**:
  - `hotkey` — Standard keyboard keystrokes via `SendInput`
  - `media_control` — Play, pause, skip, mute, volume up/down
  - `launch_app` — Launch programs/executables
  - `open_url` — Open URLs in default browser
  - `run_command` — Console shell command execution
  - `text_snippet` — Send raw text snippets via clipboard injection
  - `multi_action` — Sequenced combinations with custom delay intervals
  - `open_folder` — Navigate into sub-folder button pages
- **Dials / System Controls**: Tap a dial button to open a full-screen bottom-sheet touch-bar slider with haptic detent ticks to control:
  - System volume via WASAPI COM
  - Monitor brightness via DDC/CI (`dxva2.dll`) with WMI fallback for laptops
- **Haptic Feedback**: KEYBOARD_TAP, CONFIRM, and CLOCK_TICK haptics on the Android app for taps, connections, and slider steps.
- **Custom Tray Menu**: Dark-styled Windows system tray context menu matching the Obsidian UI theme.
- **Icon System**: 94-icon built-in pack (Lucide) or upload your own image, per button *and* per long-press action or individual chain step, synced over a token-authenticated asset server.
- **Resilient Reconnect**: Android auto-retries with backoff and shows the last-known deck (greyed out) behind a reconnect overlay instead of dropping straight to the pairing screen.
- **Revoke Device**: Kick the paired phone and issue a new PIN from the Windows tray menu.
- **Live State Buttons**: Buttons reflect real PC state, pushed live — Mute glows when actually muted, Play/Pause when actually playing, a `launch_app` button when its app is the focused window, and dial buttons show the live volume/brightness level.
- **Running Apps Switcher**: A live grid of every open PC window on the phone — tap to focus, long-press to close. Alt-Tab from your phone, including apps you never made a button for.
- **Macro Recorder**: Hit Record in the PC editor's multi-action panel, perform your keystrokes anywhere, hit Stop — the captured combos and timing become a button. Clicks back on the CrossDeck window itself are ignored, so returning to hit Stop doesn't add a stray step.
- **Multi-Action Popup**: Long-press (or tap, for a chain button) pops up every step as a real full-size button — tap one to run just that step, not the whole chain. The closed-grid preview tiles the button into a mosaic of per-step glyphs instead of one busy icon.

## Why does this app need these permissions?

- **`SendInput` (Windows)**: required to simulate hotkeys and media keys. This is the same API any macro tool uses.
- **Foreground window polling (Windows)**: used only for auto-profile-switch (switches the active button profile when you focus a specific app like OBS or Chrome). No content is read — only the process name.
- **Local network access (Android)**: to connect to the Windows Host WebSocket server on your LAN. No external network connections are made.
- **Camera (Android)**: used only by the QR scanner for pairing. Not used at any other time.

The full source code is auditable in this repository.

---

## Download

| | |
|---|---|
| 🖥️ **Windows Host** | [**Download CrossDeckSetup.exe**](https://github.com/ItisPhoenix/CrossDeck/raw/main/windows-host/Setup/CrossDeckSetup.exe) |
| 📱 **Android Client** | [**Download CrossDeck Client.apk**](https://github.com/ItisPhoenix/CrossDeck/raw/main/android-client/Release/CrossDeck%20Client.apk) |

Both links download the file directly — no extra clicks.

- **Windows**: run the installer. Windows will show a SmartScreen "Unknown Publisher" warning (no code-signing cert) — click **More info → Run anyway**.
- **Android**: install the APK directly (not on Play Store). Enable **Install unknown apps** for your browser/file manager under Android Settings → Apps, then open the downloaded file.

---

## Building — Windows Host

**Prerequisites**: Windows 10/11, .NET 8 SDK, Visual Studio 2022 with ".NET desktop development" workload.

```powershell
cd windows-host
dotnet restore
dotnet build
dotnet run --project CrossDeckHost
```

On first run: a tray icon appears. Right-click → **Show Pairing Info** to get the IP, port, and 6-digit PIN. Accept the Windows Firewall prompt — the phone cannot connect without it.

---

## Building — Android Client

**Prerequisites**: Android Studio (with bundled JDK 17), SDK Platform 31+, Android 12+ physical device on the same WiFi as the PC.

```bash
# From android-client/
./gradlew assembleDebug
```

Or open `android-client/` in Android Studio and run on your device.

1. Tap **Scan WiFi** to auto-discover your PC, or tap **Scan QR** to pair via QR code.
2. Enter PIN manually if preferred.
3. Once connected, the deck grid renders. Tap ⚙ to change the accent color theme.

---

## Network Setup

- PC and phone must be on the **same router/WiFi**.
- Disable **AP Isolation** / **Client Isolation** on the router if present — the most common cause of "can't find PC" issues.
- Test on a home network first; corporate/public WiFi typically blocks mDNS and multicast.

---

## License

MIT — see [LICENSE](LICENSE).
