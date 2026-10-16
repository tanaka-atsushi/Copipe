# Copipe

English | [日本語](README.ja.md)

**Hold a hotkey, see your clipboard history next to the mouse cursor, click to type it.**

Copipe is a small clipboard history tool for Windows. It sits in the system tray and remembers the text you copy.
Hold the hotkey (CapsLock by default) and a popup appears by the cursor. Double-click an item, or press its number key,
and Copipe types it at the text cursor of the app you are working in. Let go of the hotkey and the popup disappears.
(Prefer to tap the hotkey instead of holding it? Set *Show popup* to *Until used* in Settings.)

- **Never steals focus.** The popup does not activate, so your text cursor stays where it was.
- **Keyboard or mouse.** The first ten items are numbered 1–9, 0. Pinned items get the letters a–z.
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
2. **Hold the hotkey** (CapsLock by default). The popup shows your history, newest first.
3. While holding it, **double-click an item** or **press its number key**. The full text, including line breaks and tabs, is typed where your text cursor is.
   You can keep holding the hotkey and insert more items.
4. **Release the hotkey** to close the popup.

With *Show popup* set to **Until used**, the popup stays open after you let go of the hotkey. It closes when you:

- use an item (type it or open it). Opening a snippet group does not close it.
- press **Esc**. Inside a snippet group, Esc goes up one level first; at the top level it closes the popup.
- press the hotkey again.

While the popup is open, number keys, a–z, the arrow keys, Enter, Esc and the mode key do not reach other apps,
so close the popup when you are done.

**Find snippets by name** (*Until used* only): the popup opens with a search box at the top, ready to type.
The first history item is already selected, so Enter types it right away.
Type letters to list the snippets and groups whose names start with them, from every level. Use ↑↓ to pick one and Enter to use it
(a group opens in the snippet list, with its first item selected).
Letters go into the search box as English letters even when Japanese (IME) input or an input tool such as Meltype is on.
To search by a Japanese name, press **Hankaku/Zenkaku** (or click the **A** at the right of the box) to switch to normal input (**あ**); press it again to go back.
While converting Japanese, Enter confirms the conversion and Esc cancels it.
Number keys still pick clipboard history items by number.
Esc clears the search box first, then closes the popup. In this mode, pick pinned items by clicking or with ↑↓ instead of a–z.
Right-click a suggestion to edit, rename or delete it, the same as in the snippet list.

### History

- Copipe keeps the **last 10 text items**. Copying the same text again moves it to the top.
- Items over 100,000 characters are not kept.
- Inserting an item does not reorder the list, so the numbers stay put while you work.
- Right-click a row to **Pin** it, **Unpin** it or **Delete** it.
  - Pinned items stay at the top, are not pushed out by new copies, and survive *Clear history*.
  - You can pin up to 26 items. Click 📌 to unpin.
- Drop a file, folder or text from another app onto the popup to pin it. Files, folders and URLs open when you select them.
- Drag rows to reorder them. Drag a history row onto a pinned row to pin it there; drag a pinned row onto a history row to unpin it.

> **Note:** The history is saved to disk as plain text. If you copy a password, remove it with *Clear history* in the tray menu.

### Snippets

Press the **mode key** (Tab by default), or turn the mouse wheel over the popup, to switch between *Clipboard history* and *Snippets*.

- Each level has ten slots (1–9, 0). A slot holds a **snippet** or a **group**, and groups can be nested as deep as you like.
- With *Popup layout* set to **Side by side** in Settings, history and snippets appear together: history on the left, snippets on the right.
  Number keys, a–z, the arrow keys, Enter and Esc work on one side at a time, the one with a blue line under its title.
  Press the mode key (or turn the wheel) to switch sides, or use **← →**: → on the history side moves to snippets, and ← at the top level of snippets moves back to history
  (on a snippet group, → still opens it and ← inside a group still goes up one level). With the mouse you can use either side directly.
  The selected row on the inactive side turns light gray and becomes blue again when you switch back.
  Dragging works within one side; you cannot yet drag a history item onto the snippets side.
- Select a snippet to type it. Select a group to open it (a single click always opens a group, whatever *Paste with* is set to).
  ↑↓ also stop on empty slots; press Enter on one, or click it once, to add a snippet there.
- Press **F2** to edit the selected snippet, rename the selected group, or add a snippet to the selected empty slot
  (also works on the selected search suggestion).
- In the display name and group name boxes, letter keys type English letters even when Japanese (IME) input or Meltype is on,
  since names are searched in English. Press **Hankaku/Zenkaku** (or click the **A** at the right) to type Japanese.
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
  With *Show popup* set to *Until used*, the popup comes back at the same level after the dialog closes. Drag slots to move them, or drop one on a group to put it inside.
- A snippet can also open a file or folder (the *File...* and *Folder...* buttons in the snippet dialog).

### Tray menu

Right-click the tray icon to open *Settings...*, *Edit snippets in bulk...*, *Clear history*, *About Copipe...* (version and donation link) or *Exit*. The *About...* button in the bottom left of Settings opens the same About window.

### Settings

Settings opens automatically the first time you start Copipe.

| Setting | |
|---|---|
| **Hotkey** | The key you hold to show the popup. F1, Pause, CapsLock and so on. It must be a single key: Ctrl, Shift, Alt and Win combinations are not allowed. |
| **Show popup** | *While held* (default): the popup shows while you hold the hotkey. *Until used*: press the hotkey once and the popup stays until you use an item, press Esc or press the hotkey again. |
| **Mode key** | Switches between history and snippets while the popup is open (Tab by default). |
| **Popup layout** | *Switch* (default): show history or snippets and switch with the mode key. *Side by side*: show both, history on the left and snippets on the right. |
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
