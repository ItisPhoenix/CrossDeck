# Wire Protocol — v1 (Milestone 1 scope)

Transport: WebSocket, `ws://<host-ip>:<port>/ws`. Default port `7890`.

All messages are JSON objects with a `type` field. Extra/unknown fields must be ignored by both sides (forward compatibility).

## Auth

After WebSocket handshake, the client's very first message must be `auth`. Server closes the connection if auth fails or if any other message type arrives first.

```json
// Client -> Server (first message, always)
{ "type": "auth", "pin": "483920" }
```

```json
// Server -> Client
{ "type": "auth_ok", "token": "a1b2c3d4-...", "hostName": "DESKTOP-ABC123" }
```

```json
// Server -> Client (auth failed)
{ "type": "auth_failed", "reason": "invalid_pin" }
```

Reconnects after the first pairing should send the saved `token` instead of the PIN:

```json
{ "type": "auth", "token": "a1b2c3d4-..." }
```

## Profile Sync

Server is authoritative. Sent automatically right after successful auth, and again any time the profile changes.

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
        "action": { "type": "hotkey", "keys": ["VolumeMute"] }
      },
      {
        "buttonId": "b_002",
        "position": { "row": 0, "col": 1 },
        "label": "Notepad",
        "icon": null,
        "action": { "type": "launch_app", "path": "notepad.exe" }
      }
    ]
  }
}
```

## Button Press

```json
// Client -> Server
{ "type": "button_press", "buttonId": "b_001", "pressType": "short" }
```

```json
// Server -> Client (ack)
{ "type": "ack", "buttonId": "b_001", "status": "ok" }
```

`status` can be `"ok"` or `"error"`; on error include a `"message"` field.

## Profile Edit (Milestone 2)

Allows editing of profiles from either side.

```json
// Client -> Server (or Server -> Client)
{
  "type": "profile_edit",
  "profileId": "p_default",
  "op": "update_button",
  "button": {
    "buttonId": "b_001",
    "position": { "row": 0, "col": 0 },
    "label": "Mute",
    "icon": null,
    "action": { "type": "hotkey", "keys": ["VolumeMute"] }
  }
}
```

Or for deleting a button:

```json
{
  "type": "profile_edit",
  "profileId": "p_default",
  "op": "delete_button",
  "buttonId": "b_001"
}
```

PC Host applies change and broadcasts a `profile_sync` message to all connected clients.

## Profile Management (Milestone 2d)

### Profile List (Server -> Client)
Sent on connection or whenever the set of profiles changes.
```json
{
  "type": "profile_list",
  "activeProfileId": "p_default",
  "profiles": [
    { "profileId": "p_default", "name": "Default" },
    { "profileId": "p_gaming", "name": "Gaming" }
  ]
}
```

### Profile Switch (Either -> Either)
Switches the active profile.
```json
{
  "type": "profile_switch",
  "profileId": "p_gaming"
}
```

### Profile Create (Either -> Either)
Creates a new profile.
```json
{
  "type": "profile_create",
  "name": "New Profile Name"
}
```

### Profile Delete (Either -> Either)
Deletes a profile.
```json
{
  "type": "profile_delete",
  "profileId": "p_gaming"
}
```

## Action Types (Milestone 2 subset)

| type | fields | behavior |
|---|---|---|
| `hotkey` | `keys: string[]` | Simulates the key combo via `SendInput` |
| `launch_app` | `path: string` | Starts the process |
| `media_control` | `mediaCommand: string` | Exposes standard commands (`PlayPause`, `NextTrack`, `PrevTrack`, `VolumeUp`, `VolumeDown`, `VolumeMute`) |
| `open_url` | `url: string` | Opens target URL in default browser |
| `run_command` | `command: string` | Runs a command shell wrapper via `cmd.exe /c` |
| `text_snippet` | `text: string` | Pastes text via clipboard clipboard-inject simulation |
| `open_folder` | `targetFolderId: string` | Enters a sub-folder view, filtering displayed buttons by parentFolderId |

More action types (`multi_action`) are defined in the full architecture spec and land in future sub-milestones.

## Not Yet Implemented (future milestones)

- Asset upload endpoint for custom icons — Milestone 3
- Folders / nested pages — Milestone 2 (open_folder)
- mDNS discovery broadcast — Milestone 2 (manual IP entry works now)
