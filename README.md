# AppName (rename before publishing)

Open source Stream-Deck-style app: your Android phone becomes a customizable button deck that controls your Windows PC over local WiFi.

Full architecture, decision log, and protocol spec: [`docs/Architecture-Spec.md`](docs/Architecture-Spec.md).

## Repo Structure

```
/windows-host      .NET 8 / WPF tray app — runs on the PC being controlled
/android-client    Kotlin / Jetpack Compose app — runs on the phone (the "deck")
/shared-schema      protocol.md — WebSocket JSON message formats, source of truth for both sides
/docs               architecture spec, decision log
LICENSE             MIT
```

## Build Status — Milestone 1 (current)

Goal of this milestone: prove the hard part works end-to-end before adding features.

- [x] Windows Host: WebSocket server + PIN pairing + serves one hardcoded profile
- [x] Android Client: manual IP+PIN pairing screen + connects + renders that profile read-only + sends `button_press` on tap
- [x] Windows Host: receives `button_press`, executes a hotkey action via `SendInput`
- [ ] Bidirectional profile editing + sync (Milestone 2)
- [ ] Folders, multi-action, dial/slider, auto-profile-switch, text snippets (Milestone 2/3)
- [ ] Icon pack + custom upload with asset endpoint (Milestone 3)
- [ ] mDNS auto-discovery (manual IP entry works now; NSD discovery stub included but not wired up yet)

See `docs/Architecture-Spec.md` → "Revised MVP Scope" for the full planned build order.

## Building — Windows Host

Prerequisites: Windows 10/11, .NET 8 SDK, Visual Studio 2022 with ".NET desktop development" workload.

```
cd windows-host/HostApp
dotnet restore
dotnet build
dotnet run
```

On first run: a tray icon appears. Right-click → "Show Pairing Info" to get the PC's local IP, port, and a 6-digit PIN. Windows Firewall will prompt to allow the app — accept it, or the phone can't connect.

## Building — Android Client

Prerequisites: Android Studio, SDK Platform 31+, JDK 17, a physical Android 12+ device on the same WiFi as the PC.

1. Open `android-client/` in Android Studio, let Gradle sync.
2. Run on your device (USB debugging enabled).
3. On the pairing screen, enter the PC's IP, port, and PIN shown in the Windows tray app.
4. Once connected, the deck grid renders. Tap a button to trigger its action on the PC.

## License

MIT — see [`LICENSE`](LICENSE).
