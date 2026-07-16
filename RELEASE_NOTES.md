# CrossDeck v1.0.0 (draft)

First public release. Turn an Android phone into a customizable Stream-Deck-style
button grid for your Windows PC over local WiFi — fully open source, no telemetry,
no paywall.

## Highlights

- WebSocket pairing via PIN or QR code, with UDP auto-discovery on the local network
- Multi-profile button grids, editable live from either the phone or the PC
- Folders, dial controls (volume/brightness), and an extensible action set (hotkeys,
  launch app, media keys, URLs, shell commands, text snippets, multi-step macros)
- Auto-profile-switch based on the focused Windows app
- Dynamic accent color theming synced live between both apps
- Icon system: a bundled 94-icon pack or upload your own image per button
- Resilient reconnect on Android (exponential backoff + last-known-profile overlay)
  and a "Revoke Device" action in the Windows tray menu

## Requirements

- **Windows Host**: Windows 10/11, .NET 8 runtime
- **Android Client**: Android 12+, same WiFi network as the PC

## Build & Package

**Windows (portable, no installer):**
```powershell
cd windows-host
dotnet publish CrossDeckHost -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
Produces a single portable `CrossDeckHost.exe` under `CrossDeckHost/bin/Release/net8.0-windows/win-x64/publish/`.

**Android (APK):**
```bash
cd android-client
./gradlew assembleRelease
```
Produces `app/build/outputs/apk/release/app-release-unsigned.apk` — sign it before
distributing (see [Android's app signing docs](https://developer.android.com/studio/publish/app-signing)).

## Known Limitations (v1)

- One phone ↔ one PC pairing at a time (multi-device support deferred)
- No installer/MSIX packaging — ships as a portable .exe by design
- Corporate/public WiFi with client isolation or blocked multicast will break
  auto-discovery (manual IP entry still works)

## Checklist Before Publishing

- [ ] Trademark/domain check on "CrossDeck"
- [ ] Record demo GIF + screenshots (see README placeholders)
- [ ] Sign the release APK
- [ ] Tag the release and run `gh release create`
