# CrossDeck User Manual

CrossDeck turns your Android phone into a Stream Deck for your PC. You build pages of buttons
and dials in the **Windows editor**, and they show up live on the **phone app**, which is what
you actually press while working. Every action you configure actually runs *on the PC* — the
phone is just the remote control.

If this is your first time opening CrossDeck, start with **Quick Start** below — it walks through
everything needed to get one working button on your phone in a couple of minutes. Everything
after that is a detailed reference you can come back to as needed; you don't need to read it all
up front.

---

## Quick Start

1. **Install and open CrossDeck Host** on your PC, and the **CrossDeck** app on your Android
   phone. Make sure both devices are connected to the **same WiFi network** — this is required.
2. **Pair the phone to the PC.** In the Windows editor, click the status chip in the bottom-left
   corner (it says "Offline"). A QR code, an address, and a 6-digit PIN appear. Scan the QR code
   with the phone app, or type the address and PIN in by hand. Once it connects, the status chip
   turns green and shows your phone's name. Full detail: [§1](#1-getting-connected-pairing).
3. **Look at the editor window.** The middle of the screen is a grid of squares — this is your
   first page of buttons, currently empty. Full layout tour: [§2](#2-the-windows-editor-layout).
4. **Click any empty square in the grid.** A panel opens on the right — this is where you set up
   what the button does.
5. **Give it a name and an action.** Type a name in the **Label** box, then click **Action Type**
   and pick something simple to try first — **Open Website** is a good one: type `youtube.com`
   into the box that appears. The full list of what a button can do, explained one at a time, is
   in [§6](#6-action-types--full-reference).
6. **Look at your phone.** The button you just created is already there, live — no Save button,
   no sync step, no restart. Tap it and it opens the site right there on your PC.

That's the whole loop: click a square, set it up, it's live. Everything else in this manual is
about the different things a button (or a dial) can do, and how to organize more of them into
pages. Jump to any section from the table of contents below.

---

## Table of Contents

1. [Getting Connected (Pairing)](#1-getting-connected-pairing)
2. [The Windows Editor Layout](#2-the-windows-editor-layout)
3. [Profiles & Pages](#3-profiles--pages)
4. [Buttons](#4-buttons)
5. [Dials](#5-dials)
6. [Action Types — Full Reference](#6-action-types--full-reference)
7. [Keyboard Shortcuts — Full Reference](#7-keyboard-shortcuts--full-reference)
8. [Multiple Actions (Chains)](#8-multiple-actions-chains)
9. [Record Macro](#9-record-macro)
10. [Icons](#10-icons)
11. [Settings](#11-settings)
12. [On the Phone](#12-on-the-phone)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. Getting Connected (Pairing)

1. Launch **CrossDeck Host** on your PC.
2. If no phone has ever paired, the status chip in the bottom-left of the editor (shows
   "Offline") is clickable — click it to open the pairing popup: a QR code, an address, and a PIN.
3. Open the CrossDeck app on your phone. **Your phone and PC must be on the same WiFi network** —
   this is a local-network protocol, it does not work over the internet or over mobile data.
4. Either scan the QR code, or manually enter the address (`IP:port`) and the 6-digit PIN.
5. Once connected, the status chip turns green and shows your phone's name instead of "Offline".
   The pairing popup automatically closes. The phone saves its pairing token and uses it when the
   app returns from another app or starts again, so it does not need the PIN or QR code each time.
6. PIN/QR pairing is only needed for the first pairing, after the host revokes the device, or after
   you choose **Forget This PC** on the phone. If the saved host is temporarily unavailable, use
   **Retry now** on the reconnect overlay; do not clear the pairing unless you intend to pair again.

**Security notes:**
- The PIN locks out after 5 wrong attempts in a row (brute-force protection), with an escalating
  cooldown before you can try again.
- There is no encryption/token-expiry layer beyond the PIN gate — this is designed for trusted
  home WiFi, not a public or shared network. Keep both devices on your private home LAN.

---

## 2. The Windows Editor Layout

Three zones, left to right:

- **Profiles** (left column) — every page you've created. The highlighted one is the page
  currently live on your phone. Click any profile to switch to it immediately.
- **Grid** (center) — the buttons and dials on the active profile, laid out exactly as they'll
  appear on your phone (up to 5 columns × up to 4 rows visible at once, scrolls if you have more).
- **Property panel** (right, docked) — click any button or dial to edit it here.

**Auto-apply, no Save button:** every field you change in the property panel takes effect
*immediately* — on the grid, and pushed live to your connected phone. There is nothing to save
and nothing to lose by clicking around. If your action is incomplete (e.g. no app chosen yet), a
red hint text under the fields tells you exactly what's still missing, and nothing gets written to
the grid until it's resolved.

**Undo:** the first time you edit an *already-existing* button in a given selection, an Undo toast
appears briefly in the bottom-right of the grid — click it to instantly revert that edit. Further
edits to the same button in the same sitting don't re-trigger it (so it always means "undo the
edit I just made," not "undo my last keystroke"). New buttons and dial edits don't have Undo —
delete them instead if you change your mind before they're set up right.

**Resizing:** drag the vertical splitter between the grid and the property panel to make either
side wider (the panel can range from 260 to 460px wide). The whole window resizes too; buttons
shrink-to-fit down to a minimum comfortable size, then the grid scrolls instead of shrinking
further.

---

## 3. Profiles & Pages

A **profile** is one full page: up to **20 buttons** plus its own dial row underneath. Manage
profiles from the left column:

- **+ New Page** — creates a blank profile and switches to it immediately.
- **Import a preset…** — starts from a ready-made layout instead of blank. Current presets:
  **Productivity** and **Streaming**.
- **Right-click any profile row** for a context menu:
  - **✏️ Rename…**
  - **🗑️ Delete Page** (only if you have more than one profile — the last one can't be deleted)
  - **📤 Export Profile…** — saves the profile (buttons, dials, everything) to a file you can back
    up or share.
  - **📥 Import Profile…** — loads an exported profile file back in as a new page.
- **Trigger** field (top toolbar, next to the breadcrumb) — type a process name including the
  extension (e.g. `chrome.exe`, `discord.exe`) and CrossDeck automatically switches your phone to
  *this* profile the moment that process is the foreground window on your PC, switching back when
  it isn't. Leave it blank to disable auto-switching for that profile — it'll then only change
  when you manually pick it.

### Folders

Any button set to the **Open Folder** action type creates a sub-page — its own independent
20-slot grid nested one level under the current profile. This is the only way to organize more
than 20 items behind a single profile. From the folder button's property panel, click
**Enter Folder in Editor Grid** to jump straight into editing it. Once inside, use **◀ Go Back**
(top-left of the grid area) or click a breadcrumb segment to return to a parent page. Folders can
be nested arbitrarily deep.

---

## 4. Buttons

### How to add your very first button (step by step)

1. Look at the middle of the screen — that's the **grid**, a bunch of squares. Some already have
   a picture and a name on them (those are buttons that already exist). Find a square that's
   **empty** (blank, no picture, maybe just a **+**).
2. **Click that empty square.**
3. A panel slides in on the right side of the screen. This is where you tell CrossDeck what you
   want the button to do when you press it.
4. At the top there's a box labeled **Label** — type a short name for your button here, like
   `Open Chrome` or `Say Hi`. (You can also leave this blank and CrossDeck will pick a name for
   you automatically, based on what the button does.)
5. Find the box labeled **Action Type** and click it — a list drops down with all the things a
   button can do (open an app, type something, press a key, and so on — every option is explained
   one by one in [§6 below](#6-action-types--full-reference)). Click the one you want.
6. More boxes appear underneath, specific to what you picked — fill those in too. Each one has a
   little grey hint text telling you what to type and an example.
7. That's it — look back at the grid. **Your new button is already sitting there**, with its name
   and a picture on it. You do not need to click a "Save" button anywhere — everything you type
   saves itself instantly, and it's already live on your phone too.

If you skip a step it needs (like forgetting to type a website address), the button simply won't
show up on the grid yet, and red text under the boxes tells you exactly what's still missing.
Nothing breaks — just fill in what it's asking for.

**To edit a button that already exists:** click it on the grid (instead of an empty square) and
the exact same right-hand panel opens, already filled in with its current settings. Change
anything you like — it updates the second you type, no separate "edit mode" or Save step.

**To delete a button:** click it, then click the little trash-can icon at the top of the right
panel, then confirm.

---

The property panel shows:

- **Label** — the text shown on the button, on both PC preview and phone. Leave it blank and
  CrossDeck auto-suggests one from the action (the app's filename for Launch App, the domain for
  Open Website, the first word of the command for Run Command, etc.) — it keeps updating that
  suggestion live as you change the action, right up until you type your own text into Label, at
  which point your text wins permanently for that button.
- **Icon** — see [§10 Icons](#10-icons) below.
- **Action Type** — what happens on press. Full breakdown in [§6](#6-action-types--full-reference).
- **🗑 (top-right of the panel)** — deletes the button after a confirmation prompt. Hidden while
  configuring a brand-new button that hasn't been added to the grid yet (nothing to delete).

**Reordering:** press and drag any button on the grid to a new position — the buttons between the
old and new spot shift over to make room, same as reordering icons on a phone home screen.

---

## 5. Dials

The row below the grid holds dials — for things you adjust continuously (volume, brightness)
rather than tap once. Click a dial cell exactly like a button; the property panel shows
dial-specific fields.

### Dial targets

| Target | What it controls | Extra fields |
|---|---|---|
| **Master Volume** | System-wide volume, dragged on the phone | none |
| **Microphone Volume** | Your default mic's input level | none |
| **Brightness** | Screen brightness | none |
| **App Volume** | One specific app's volume, OR the live mixer | **Bind to one app** — pick from currently-playing apps or type a process name (e.g. `spotify`). **Leave this blank** and the dial instead opens a live mixer sheet on the phone with a slider + mute toggle for every app currently playing audio — useful when you don't want to commit the dial to one app. |
| **Keystroke Step** | Fires a keystroke per drag detent instead of setting a level | **Step Up** and **Step Down** — comma-separated key combos (see §7), fired once per detent as you drag up or down. Good for things with no absolute value, like a zoom level (`Ctrl,Shift,Equal` / `Ctrl,Minus`) or scroll (`Down` isn't in the key list — use application-specific shortcuts instead). |

### Stacking multiple targets on one dial

Check **"Stack multiple targets on this dial"** to combine several targets on a single dial
instead of using up multiple dial slots — tapping the dial cycles to the next target in the stack.
Click **+ Add Layer** to add another target; each layer is configured exactly like the
single-target view above (its own target, process binding or step keys, and an optional label).

### Tap Action

Every dial can *also* have its own tap behavior — separate from the drag/adjust gesture. Check
**"Enable tap action"** and you get the exact same action editor as a normal button (including
Multiple Actions chaining), which fires only when the dial is *tapped*, never when it's dragged.

---

## 6. Action Types — Full Reference

Every button, and every dial's Tap Action, can be any of these. All of them run **on the PC**,
triggered by the phone press.

### Keyboard Shortcut
Sends a real key combination to your PC, exactly as if you'd pressed it on your own keyboard.

**Setup:** click the box and type the key names separated by commas — for example `Ctrl,C` to
copy. Tapping the button on your phone presses that exact combination. The complete list of valid
key names, plus a table of common ready-made shortcuts, is in
[§7](#7-keyboard-shortcuts--full-reference).

### Launch App
Opens a program on your PC — the same as double-clicking its icon, but triggered from your phone.

**Setup:** click **Discovered PC Application** and pick from the dropdown list of everything
installed on your PC, or start typing to filter it down. CrossDeck fetches that app's real icon
automatically once picked, so there's no separate icon step. If the app you want isn't listed,
click **📂 Browse** and locate its `.exe` file yourself.

### Media Control
Sends the same signal as your keyboard's dedicated media keys (Play/Pause, Skip, Volume) — useful
if your keyboard doesn't have them.

**Setup:** click the box and choose Play/Pause, Next Track, Previous Track, Volume Up, Volume
Down, or Mute. It affects whatever's currently playing on your PC — you don't pick a specific app.

### Open Website
Opens a web page in your browser.

**Setup:** type the address, e.g. `youtube.com` — you don't need to type `https://`, CrossDeck adds
it automatically. It opens in your default browser as a new window. The site's favicon is fetched
automatically for the button's icon.

### Run Command
Runs a command-line command in the background, with no window shown. This is intended for people
already comfortable with a command line — if that's not familiar territory, one of the other
action types is probably a better fit.

**Setup:** type the exact command as you'd type it into Command Prompt. It runs silently — no
window appears and no output is shown, so it suits fire-and-forget commands (launching a tool,
running a script) rather than ones you need to read a result from.

### Text Snippet
Types out a block of text for you wherever your cursor currently is — useful for things you type
often, like an email signature or a canned reply.

**Setup:** type or paste the text you want (multiple lines are fine). Click into the field you
want it typed into first, then tap the button. CrossDeck pastes the text via your clipboard and
restores whatever was on the clipboard beforehand, so nothing else is disturbed.

### Open Folder
Doesn't open a folder on your PC — it creates a new page of buttons inside CrossDeck, similar to
a folder of apps on a phone's home screen. Use it once a page's 20 button slots aren't enough.

**Setup:** pick this action type and leave **Target Folder ID** blank — CrossDeck fills it in for
you. Once set, an **Enter Folder in Editor Grid** button appears in the panel; click it to start
adding buttons inside the new page. See [§3 Folders](#folders) for more.

### Multiple Actions
Runs several actions from a single button press — for example, opening an app and sending a
keyboard shortcut in sequence.

**Setup:** pick this action type, then click **+ Add Step** for each action you want in the chain.
Each step is a full action editor of its own (any type above). Steps run in the order you add
them, on a single tap. Full detail in [§8](#8-multiple-actions-chains).

### Record Macro
Records real keystrokes and mouse clicks as you perform them, then replays that same sequence
whenever the button is pressed.

**Setup:** pick this action type, click **Record**, then perform the keystrokes/clicks you want
saved in whatever app you're targeting. Return to CrossDeck and click **Stop** when done. Full
detail in [§9](#9-record-macro).

---

## 7. Keyboard Shortcuts — Full Reference

The **Hotkey Combination** field takes a **comma-separated list of key names**, all pressed down
together and released together (a true chord, like a real keyboard shortcut) — e.g. typing
`Ctrl,Shift,A` presses Ctrl+Shift+A as one combo, not three separate keystrokes. Key names are
**not case-sensitive** (`ctrl` and `Ctrl` both work), and the order you list them in doesn't
matter functionally, but conventionally modifiers go first.

### Valid key names

**Modifiers** — `Ctrl`, `Alt`, `Shift`, `Win`

**Letters** — `A` through `Z`

**Numbers** — `0` through `9`

**Function keys** — `F1` through `F12`

**Common keys** — `Enter`, `Escape`, `Tab`, `Space`, `Backspace`, `Delete`

**Punctuation** (US keyboard layout) — `Equal` (`=`), `Minus` (`-`), `Comma` (`,`), `Period` (`.`),
`Semicolon` (`;`), `Slash` (`/`), `Backtick` (`` ` ``), `LeftBracket` (`[`), `RightBracket` (`]`),
`Backslash` (`\`), `Quote` (`'`)

**Media/volume** — `VolumeMute`, `VolumeDown`, `VolumeUp`, `MediaNextTrack`, `MediaPrevTrack`,
`MediaStop`, `MediaPlayPause` (these also exist as their own dedicated **Media Control** action
type — use whichever you prefer)

> **Not currently supported:** arrow keys, Home/End/Page Up/Page Down, Insert, Caps Lock, the
> Numpad, and any non-US-layout punctuation. If you need one of these, ask for it to be added —
> the key table is a simple lookup and new keys are cheap to add.

### Worked examples

| What you want | Type this |
|---|---|
| Copy | `Ctrl,C` |
| Paste | `Ctrl,V` |
| Undo | `Ctrl,Z` |
| Switch window | `Alt,Tab` |
| Task Manager | `Ctrl,Shift,Escape` |
| Windows search | `Win,S` |
| Zoom in (many apps) | `Ctrl,Shift,Equal` |
| Zoom out | `Ctrl,Minus` |
| New private/incognito window | `Ctrl,Shift,N` |
| Lock PC | `Win,L` |
| Force quit (some apps) | `Alt,F4` |

A single key with no modifier is valid too — just type e.g. `F5` or `Escape` on its own for a
plain keypress.

---

## 8. Multiple Actions (Chains)

Set a button's Action Type to **Multiple Actions** to fire a whole sequence on one press:

- Click **+ Add Step** to add another action to the chain — each step gets the *entire* action
  editor (any of the types in §6, including another chain if you really want to nest them).
- Each step has its own **delay-after** value (in milliseconds) — how long to wait *after* that
  step runs before starting the next one. Use this to give a launching app time to open before the
  next step tries to interact with it, for example.
- Steps run **in order, top to bottom**, all on a single press — there's no step-by-step popup or
  confirmation, the whole chain just runs.
- Reorder steps by dragging them; remove a step with its own delete control.

A dial's **Tap Action**, when it's a chain, behaves identically.

---

## 9. Record Macro

For sequences that are easier to *perform* than to describe field-by-field:

1. Set the action type to **Record Macro**.
2. Click **● Record Keystrokes & Clicks**. The button turns into a recording indicator with a hint
   telling you it's live.
3. Switch to whatever app/window you want to record in, and perform the keystrokes and/or mouse
   clicks exactly as you want them replayed.
4. A small floating "Recording…" panel appears in the top-right corner of your screen — click
   **■ Stop** there (no need to switch back to the CrossDeck window) when you're done, or
   **Cancel** to discard everything captured this session (asks for confirmation first). The
   floating panel can be dragged anywhere if it's in the way.
5. The recorded sequence appears as steps you can review, reorder, or delete individually — same
   as a hand-built Multiple Actions chain, because that's exactly what it becomes under the hood.

Only one recording can run at a time across the whole editor — starting a second one while one is
already active is a no-op.

---

## 10. Icons

Every button and dial can carry its own icon, shown as a small thumbnail once set:

- **📂 Browse** — upload any PNG/JPG/JPEG/ICO file from your PC.
- **🎨 Built-in** — opens a searchable grid of CrossDeck's bundled icon set (a monochrome Lucide
  icon pack). Type in the search box to filter by name.
- **Automatic extraction** — for **Launch App**, CrossDeck pulls the app's real icon the moment
  you pick it (works for both classic desktop apps and Store/UWP apps). For **Open Website**, it
  fetches the site's favicon as you type the URL. Neither of these needs a manual icon step.

If you don't set anything, the button falls back to a generic icon based on its action type.

---

## 11. Settings

Click the gear icon, bottom-left of the editor (next to the connection status chip):

- **Theme** — five accent color options: Neon Cyan, Neon Purple, Cyberpunk Yellow, Toxic Green,
  Crimson Red. Applies instantly across the whole app (Windows editor and phone both).
- **Start CrossDeck on PC startup** — adds or removes a Windows Run-key entry so the host launches
  automatically when you log in, running in the background until a phone connects.
- **Pairing** — shown only while no phone is connected: the same QR/address/PIN as the sidebar
  popup, plus a **New PIN** button.
- **About** — version info.

---

## 12. On the Phone

- **Tap** any button to fire its action.
- **Drag** a dial to adjust its level continuously; **tap** a dial (instead of dragging) to fire
  its Tap Action, if one is set.
- **Swipe** between profile pages, or use the page-dot indicator, if you have more than one page.
- Everything is **live**: any change you make in the Windows editor — a new button, an edited
  label, a deleted profile — reflects on the phone within moments. No manual refresh, no
  reconnect needed.
- If the connection drops (PC sleeps, WiFi hiccups, or you switch to another Android app), the
  phone reconnects automatically with its saved token once both devices are back on the same
  network; the status chip on PC will show "Offline" in the meantime. PIN/QR is not needed for
  normal reconnects.

---

## 13. Troubleshooting

- **Phone won't connect** — confirm both devices are on the exact same WiFi network (not a guest
  network that isolates devices from each other). If the phone shows the reconnect overlay, tap
  **Retry now** and wait for the host to return. If the host revoked the device or you used
  **Forget This PC**, pair again with the new PIN/QR code; otherwise do not restart the app or
  re-pair just to recover a normal reconnect.
- **A hotkey does nothing** — double-check every key name against the [valid list](#valid-key-names)
  in §7; an unrecognized name is silently ignored rather than erroring loudly right now.
- **An app's icon looks wrong, generic, or missing** — open the button and re-pick the app from
  the dropdown; icons are extracted and cached the first time, so a stale cache from a reinstalled
  app can occasionally need a re-pick to refresh.
- **A button won't appear on the grid / phone** — the property panel shows a red hint under the
  action fields naming exactly what's still missing (e.g. "Enter a keyboard shortcut", "Choose or
  type an app"). Nothing commits until that's resolved.
- **Made an edit you didn't want** — look for the Undo toast, bottom-right of the grid; it only
  appears once, right after your *first* edit to an existing button in that sitting.
- **Run Command seems to do nothing** — remember it runs through `cmd.exe`, hidden, with no visible
  output; a command that needs a visible window or interactive input won't behave as expected.
- **Text Snippet pastes nothing** — the field/app you're pasting into must support a normal
  Ctrl+V paste; some elevated (Run as Administrator) windows block simulated input from a
  non-elevated CrossDeck process.
