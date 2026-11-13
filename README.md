# Copipe

English | [日本語](README.ja.md)

**Press a hotkey, see your clipboard history next to the mouse cursor, click to type it.**

Copipe is a small clipboard history tool for Windows. It sits in the system tray and remembers the text you copy.
Press the hotkey (CapsLock by default) and a popup appears by the cursor. Double-click an item, or type part of a snippet name and press Enter,
and Copipe types it at the text cursor of the app you were working in.

- **Types where you were.** After you pick an item, Copipe goes back to your app and types it at the text cursor.
- **Keyboard or mouse.** Pick items with the arrow keys and Enter, or type a snippet's name to find it.
- **Snippets.** Keep reusable text (or files, folders and links to open) in groups, ten per level.
- **Light.** A single small `.exe` with no runtime to install. Built on .NET Framework 4.8, which comes with Windows.
- **English and Japanese.** The app follows the Windows display language, or you can pick one in Settings.

## Download

**[Download the latest release](../../releases/latest)**

| File | |
|---|---|
| `Copipe-Setup-<version>.exe` | Installer. Installs for the current user, no admin rights needed. Adds Copipe to the Start menu. |
| `Copipe-<version>.zip` | Portable. Unzip anywhere and run `Copipe.exe`. |

Requires Windows 10 or 11.

> **"Windows protected your PC"?** Copipe is not code-signed yet, so SmartScreen may warn you the first time.
> Click **More info** → **Run anyway**.

## How to use

1. Start Copipe. An icon appears in the system tray, and from now on the text you copy is kept in the history.
2. **Press the hotkey** (CapsLock by default). The popup shows your history, newest first, and stays open.
3. **Double-click an item**, or select it with **↑↓** and press **Enter**. The full text, including line breaks and tabs, is typed where your text cursor is.

The popup closes when you:

- use an item (type it or open it). Opening a snippet group does not close it.
- press **Esc**. Inside a snippet group, Esc goes up one level first; at the top level it closes the popup.
- press the hotkey again.

While the popup (its search box) is in front, it takes all keys. Click another app and the popup stays open,
but your keys go to that app (arrow keys, Enter and Esc too); click the popup to type in it again.

**Find snippets by name**: the popup has a search box at the top, ready to type in when the popup opens.
It is an ordinary text box: type anything, Japanese (IME) included.
The first history item is already selected, so Enter types it right away.
Type to list the snippets and groups whose names contain what you typed, from every level (names that start with it come first). Use ↑↓ to pick one and Enter to use it
(a group opens in the snippet list, with its first item selected).
While converting Japanese, Enter confirms the conversion and Esc cancels it (the arrow keys belong to the popup, so pick candidates with Space).
Esc clears the search box first, then closes the popup.
Right-click a suggestion to edit, rename or delete it, the same as in the snippet list.

### History

- Copipe keeps the **last 10 text items** by default. Change it (1–100) with *History size* in Settings; lowering it drops the oldest items right away.
  Copying the same text again moves it to the top.
- Items over 100,000 characters are not kept.
- Inserting an item does not reorder the list, so items stay put while you work.
- Right-click a row to **Pin** it, **Unpin** it or **Delete** it.
  - Pinned items stay at the top, are not pushed out by new copies, and survive *Clear history*.
  - You can pin up to 26 items. Click 📌 to unpin.
- Drop a file, folder or text from another app onto the popup to pin it. Files, folders and URLs open when you select them.
- Drag rows to reorder them. Drag a history row onto a pinned row to pin it there; drag a pinned row onto a history row to unpin it.

> **Note:** The history is saved to disk as plain text. If you copy a password, remove it with *Clear history* in the tray menu.

### Snippets

The popup shows *Clipboard history* on the left and *Snippets* on the right.

- The arrow keys, Enter and Esc work on one side at a time, the one with a blue line under its title.
  Press the **mode key** (Tab by default) to switch sides, or use **← →**: → on the history side moves to snippets, and ← at the top level of snippets moves back to history
  (on a snippet group, → still opens it and ← inside a group still goes up one level). With the mouse you can use either side directly.
  The selected row on the inactive side turns light gray and becomes blue again when you switch back.
  The popup always opens with the history side active.
- Each level has ten slots. A slot holds a **snippet** or a **group**, and groups can be nested as deep as you like.
- Drag a history row onto an empty snippet slot to save it as a snippet. Drop it on a group (or a level name in the header) to put it in that group's first free slot;
  hold it over a group for a second to open it.
- Select a snippet to type it. Select a group to open it (a single click always opens a group, whatever *Paste with* is set to).
  ↑↓ also stop on empty slots; press Enter on one, or click it once, to add a snippet there.
- Press **F2** to edit the selected snippet, rename the selected group, or add a snippet to the selected empty slot
  (also works on the selected search suggestion).
- When you copy text from Outlook or Word, paragraph indents (such as the indent Outlook makes when you type a space at the start of a line)
  are kept as full-width spaces at the start of the line, in the history and when you paste into the snippet text box.
  Lines that wrap still start at the left edge.
- A snippet uses either its text or a file, folder or URI. When the dialog opens, neither is marked.
  The box you put the cursor in gets a red **●** and a red frame; the other is grayed out (**○**) and is not saved.
  You can still click in the gray one to switch to it.
- **Edit in bulk...** (in the right-click menu, the tray menu and Settings) opens an editor laid out like the popup:
  groups on the left, the 10 slots of the chosen group in the middle, and the chosen slot on the right.
  Pick a type (type text / open a file, folder or URL / group) and fill in the field for it; typing in an empty slot adds a snippet.
  *Paste from Excel etc.* puts copied rows (name, contents) into the free slots of the current group. Nothing is saved until you click OK. **Esc** goes back up one level, and you can click a level name in the header to jump to it.
- Right-click a slot to add, edit, rename, delete or pin to history (deleting asks for confirmation first).
  The popup comes back at the same level after the dialog closes. Drag slots to move them, or drop one on a group to put it inside.
- A snippet can also open a file or folder (the *File...* and *Folder...* buttons in the snippet dialog).

### Tray menu

Right-click the tray icon to open *Settings...*, *Edit snippets in bulk...*, *Clear history*, *About Copipe...* (version and donation link) or *Exit*. The *About...* button in the bottom left of Settings opens the same About window.

### Settings

Settings opens automatically the first time you start Copipe.

| Setting | |
|---|---|
| **Hotkey** | The key you press to show the popup. F1, Pause, CapsLock and so on. It must be a single key: Ctrl, Shift, Alt and Win combinations are not allowed. |
| **Mode key** | Switches the active side (history / snippets) while the popup is open (Tab by default). |
| **Paste with** | Double-click (default) or single-click. |
| **Language** | Auto (follow Windows), English or Japanese. |

Good to know:

- **While Copipe is running, other apps do not receive the hotkey.** With the default CapsLock, CapsLock no longer switches upper/lower case, even with Shift held. With F1, VS Code's F1 command palette stops working (Ctrl+Shift+P still works).
- **F12 cannot be used.** Windows reserves it for debuggers.
- **If the popup does not appear,** another app may be grabbing the key. Choose a different key.
- **Only one Copipe runs at a time.** Starting it again just shows "Copipe is already running." and the running one keeps working.
- **If you use CapsLock as the hotkey,** it no longer toggles Caps Lock, even with Shift, Ctrl or Alt held: the popup appears instead. On a Japanese keyboard, Shift+英数 (Eisu) also works as the hotkey.
- **Copipe cannot type into apps running as administrator.** This is a Windows restriction (UIPI).

## Your data

Everything is stored in `%LOCALAPPDATA%\Copipe`:

| File | |
|---|---|
| `settings.ini` | Settings. You can edit it in Notepad. |
| `history.json` | Clipboard history and pinned items. |
| `phrases.json` | Snippets. You can edit it by hand. Copipe reloads it the next time you open Snippets. |

Nothing is sent anywhere. Copipe makes no network connections.

**Uninstalling:** If you used the installer, uninstall Copipe from *Settings → Apps → Installed apps*. It asks whether to delete your data as well.
For the portable version, exit Copipe and delete the files.

## Support

Copipe is free. If it saves you time, you can [buy me a coffee](https://buymeacoffee.com/bigcomi) ☕

Bug reports and ideas are welcome in [Issues](../../issues).

## Building from source

Copipe builds with the C# compiler that ships with Windows (`csc.exe`, .NET Framework 4.8). You do not need Visual Studio or the .NET SDK.

```powershell
.\build.ps1              # build bin\Copipe.exe
.\tools\Build-Installer.ps1   # build the installer from bin\Copipe.exe
.\build.ps1 -Run         # build and start
```

The installer needs Inno Setup 7 or 6; without it, the script skips the installer with a warning.
The source is limited to C# 5 because that is what this compiler supports.
Design notes, measurements and the test harness are documented in Japanese in [README.ja.md](README.ja.md).

## License

Copyright (C) 2026 Atsushi Tanaka

[GNU General Public License v3.0](LICENSE)
