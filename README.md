# CrossDeck

Open source Stream-Deck-style app: your Android phone becomes a customizable button deck that controls your Windows PC over local WiFi.

## Repo Structure

```
/windows-host      .NET 8 / WPF tray app — runs on the PC being controlled
/android-client    Kotlin / Jetpack Compose app — runs on the phone (the "deck")
LICENSE             MIT
```

## Features Built

- [x] **Windows Host**: WebSocket server + tray icon controls + active profile listener.
- [x] **Android Client**: Dynamic grid system auto-fitted to any screen size.
- [x] **UDP Auto-Discovery**: Scan and connect instantly on the local network (saves last connection).
- [x] **Multi-Profile Management**: Create, rename, delete, and switch profiles from either the Android deck or the PC.
- [x] **Bidirectional Grid Editor**: Edit actions, labels, folder layout, and grid positions on the fly from either client or host.
- [x] **Folders**: Scoped button pages with nesting navigation hierarchy and back navigations.
- [x] **Actions Engine**:
  - `hotkey` (Standard keyboard key strokes)
  - `media_control` (Play, pause, skip, mute, volume change)
  - `launch_app` (Launch programs/executables)
  - `open_url` (Open URLs in default browser)
  - `run_command` (Console shell command line execution)
  - `text_snippet` (Send raw text snippets)
  - `multi_action` (Sequenced combinations with custom delay intervals)
- [x] **Dials / System Controls**: Tap a dial button to pull up a smooth 60fps slider modal popup to control:
  - System volume level natively via WASAPI COM.
  - Monitor brightness natively (DDC/CI hardware controller for desktop monitors via `dxva2.dll` with a WMI fallback for laptops).

## Building — Windows Host

Prerequisites: Windows 10/11, .NET 8 SDK, Visual Studio 2022 with ".NET desktop development" workload.

```powershell
cd windows-host/CrossDeckHost
dotnet restore
dotnet build
dotnet run
```

On first run: a tray icon appears. Right-click → "Show Pairing Info" to get the PC's local IP, port, and a 6-digit PIN. Windows Firewall will prompt to allow the app — accept it, or the phone can't connect.

## Building — Android Client

Prerequisites: Android Studio, SDK Platform 31+, JDK 17, a physical Android 12+ device on the same WiFi as the PC.

1. Open `android-client/` in Android Studio, let Gradle sync.
2. Run on your device (USB debugging enabled).
3. Tap "Scan" to auto-discover your PC, or enter the IP + PIN manually.
4. Once connected, the deck grid renders. Tap a button or slider to control your PC.

## License

MIT — see [`LICENSE`](LICENSE).
