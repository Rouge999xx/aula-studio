# Architecture

AULA Studio is a WPF app on .NET 10, built as one framework-dependent exe. WinForms is referenced only for the tray icon. There are no NuGet packages: HID, the keyboard hook, Raw Input, input injection and Core Audio all go through P/Invoke or COM interop.

## Startup

`App.OnStartup`, in order:

1. **One-off modes.** `--dump`, `--selftest` and `--knobtest` run their check and exit.
2. **Single instance.** A mutex (`Local\AulaStudio`) and two named events handle a second launch:
   - `Local\AulaStudio.Show`: a second plain launch brings the running window to the front;
   - `Local\AulaStudio.Exit`: `--exit` asks the running copy to quit cleanly.
3. **`Services.Init`** creates the long-lived pieces on the UI thread:
   - the settings;
   - `KeyboardService`;
   - `KeyboardHook`;
   - `ConsumerInput`;
   - `KnobEngine`;
   - `AppKeyEngine`.
4. **The window and tray.** The main window is created (shown unless `--tray`), then the tray icon.
5. **Connecting.** The keyboard is connected on a background task.

## The keyboard side

| Piece | Job |
|---|---|
| `Device/HidNative.cs` | SetupAPI enumeration, `CreateFile`, HID capabilities, feature reports |
| `Device/AulaDevice.cs` | Finds the configuration collection (VID/PID + usage page `FF00` + 520-byte feature report), sends packets, and keeps the packet log shown in Settings. It refuses any write other than the key table. |
| `Device/KeyboardService.cs` | Every operation that touches the keyboard: connect, read, save, restore, back up, and repair Fn or Win |
| `Protocol/KeyEntry.cs` | One 4-byte slot: what kind it is, and a readable description |
| `Protocol/KeyMatrix.cs` | A 128-slot layer: protected slots, the trailer, and the differences between two tables |
| `Protocol/HidUsages.cs` | Key, media and keyboard-function names |
| `Layout/F75Layout.cs` | Where each key sits on the picture, and its matrix slot (from AULA's `KB.ini`) |
| `Backup/BackupStore.cs` | Backup files: save, list, the Original, and pruning (newest 60 kept, the Original never deleted) |

### The safe write routine

Every write in `KeyboardService` goes through one routine:

```
Guard            refuse if not connected, or if AULA's own app is running
fresh read       both layers, the lighting block and the color table
check            no trailer changes; Fn and knob press only with Advanced on; Fn stays on its key
backup           a full copy to %APPDATA%\AulaStudio\backups
write            only the slots that were edited, applied on top of the fresh read; Fn layer first, base layer last
read back        both layers, compared byte for byte
roll back        on any mismatch, write the fresh read back and check again
```

Only edited slots are applied on top of a fresh read, so anything that changed on the keyboard in the meantime is kept.

## The knob side

```
F75 knob ── HID report (MI_01 COL02) ──► Windows input stack ── synthesized VK_VOLUME_* ──► KeyboardHook ──┐
        └──────────────────────────────► Raw Input (ConsumerInput) ─────────────────────────────────────────┤
                                                                                                             ▼
                                                    KnobEngine: pair the two copies, pick the rule for the app in front
                                                                                                             │
                                  ├── swallow the volume key (hook route), so Windows doesn't change the volume
                                  ├── Inject: wheel, sideways wheel, modifier+wheel, or keys (tagged "KNOB")
                                  └── VolumeGuard.Hold(): undo any volume change Windows makes anyway
```

| Piece | Job |
|---|---|
| `Input/KeyboardHook.cs` | System-wide `WH_KEYBOARD_LL` hook. Handlers run in order, and returning true swallows the key. `Ours` marks AULA Studio's own input (`dwExtraInfo` = `0x4B4E4F42`). `Reinstall()` restores a hook that Windows dropped. |
| `Input/ConsumerInput.cs` | Raw Input for consumer-control collections, on a message-only window. It decodes reports with `HidP_GetUsages` and raises one event per newly pressed usage. |
| `Input/Inject.cs` | `SendInput` for wheel events and shortcuts, all tagged with the marker |
| `Knob/KnobEngine.cs` | The heart of the knob: routes, pairing, per-app rules, acceleration, the press action and the second mode |
| `Knob/VolumeGuard.cs` | Core Audio fallback. It tracks the default playback device's level and mute state, and puts them back after a click that was used for something else. |
| `Knob/OsdWindow.cs` | The small on-screen label. It never takes focus and lets clicks through. |
| `Knob/KnobLog.cs` | `knob.log`, written on a background thread so the hook never waits for the disk. It only records volume/mute keys and knob clicks. |
| `Knob/KnobSelfTest.cs` | `--knobtest` |
| `Settings/AppSettings.cs` | `settings.json`: per-app knob rules, the tray's manual mode, the knob presets, and version migrations |

### The two routes and pairing

- **A click seen by the hook** is handled right away. If the rule isn't Volume, the hook swallows the key.
- **A click seen by Raw Input** is first matched against a hook click: the same key, within 250 ms.
  - If there's a match, the click was already handled.
  - If there's none, the engine waits 250 ms for the hook's copy. If it doesn't come, the engine handles the click anyway and reinstalls the hook.
  - If that happens again after the reinstall, Raw Input becomes the main route, and the volume guard undoes Windows' volume changes.
- **On the F75 this was tested with, every click takes the hook route.** See [knob-detection.md](knob-detection.md) for the evidence.

### Speed in the hook callback

Windows silently removes a low-level hook that is slow to answer, so the callback does very little:

- It looks up the app in front with `QueryFullProcessImageName`.
- It decides whether to swallow the key.
- It queues everything else (injecting input, the volume guard, updating the UI) to run after it returns.

## App keys

Per-app key rules and macros, set on the App keys page.

| Piece | Job |
|---|---|
| `AppKeys/AppKeyEngine.cs` | Hook handler. For a key with a rule in the app in front (or in "Every app"), it swallows the key and sends the replacement. |
| `Input/KeyCapture.cs` | "Press a key" for editors. It hooks in ahead of everything else and grabs the next key or shortcut, so no app sees it. |
| `Input/Inject.cs` | `KeyDown` / `KeyRepeat` / `KeyUp` hold a replacement key exactly as long as the real one; `Type` sends text as Unicode characters, independent of the keyboard layout. |
| `Settings/AppSettings.cs` | `KeyProfile` (one app), `KeyRule` (one key), `MacroStep` (press keys, type text, or wait) |
| `Views/AppKeysView.xaml(.cs)` | The App keys page |

How a rule runs:

- **Key or shortcut.** The key-down is swallowed, and the replacement's modifiers and key go down. Each key repeat sends another key-down of the replacement, and the key-up releases it all.
  - What was pressed is remembered per key, so the release matches even if the app in front changed meanwhile.
  - A key-down more than 1.1 s after the last one, with no key-up in between, means a release was missed (for example, the PC locked). The old replacement is released first.
- **Macro and text** run once per press, on a background task, with 20 ms between steps. A second press while a macro is still running is ignored.
- **Nothing** swallows the key's press and release.
- **Rules that aren't filled in** (no key, no steps, no text) leave their key alone, and so do the knob's volume and mute keys.
- **Rules never apply while AULA Studio itself is in front,** so its own editors keep working.

Everything AULA Studio sends carries the hook marker, so a replacement is never remapped again. Page Up → Space and Space → Page Up can't loop.

## Settings safety

- **Atomic saves.** `settings.json` is written to `settings.json.new` and then moved into place, so a crash can't leave half a file.
- **Unreadable files are kept.** If the file can't be read, AULA Studio copies it to `settings.unreadable-<date>.json` and then starts with defaults. It also says so in the main window, so the next save can't silently wipe the knob and key rules in the old file.

## The interface

- **`MainWindow`**:
  - the sidebar navigation;
  - the connection status card;
  - toasts;
  - a dark title bar, set through DWM.
  - It also rescans when Windows reports a device change.
- **Pages** (`Views/`): Keyboard, Knob, App keys, Backups and Settings.
  - Each page is created the first time it's opened, then kept.
  - Hook handlers that only matter on screen (key capture, the key tester) attach in `Loaded` and detach in `Unloaded`.
  - The Keyboard page keeps following the keyboard while hidden, for example after a restore from the Backups page.
- **`Theme.xaml`**: the colors and every control style.
- **`Tray/TrayIcon.cs`**: a WinForms `NotifyIcon` with a dark menu renderer. The menu is rebuilt each time it opens, so the checkmarks are always current.
- **`app.manifest`**: per-monitor DPI awareness (v2), so the window stays sharp on scaled displays.

## Threading

- **The UI thread** hosts the keyboard hook and the Raw Input window, so knob and app-key handling never need locks.
- **Macros** run on background tasks, because their waits must not hold up the hook.
- **Keyboard I/O** runs on background tasks, serialized by a lock in `KeyboardService`, with another inside `AulaDevice`.
- **Core Audio notifications** arrive on Windows' threads and are passed to the UI thread.
- **The knob log** is written on its own background thread.
