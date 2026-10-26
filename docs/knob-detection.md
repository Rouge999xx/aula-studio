# Knob detection: why the knob kept changing the volume

## Summary

To avoid reacting to its own scrolls, AULA Studio ignored every keystroke that Windows marks as *injected*. The F75's knob clicks arrive as exactly that kind of keystroke, so AULA Studio ignored the knob too, and Windows went on changing the volume.

**The fix:** AULA Studio now recognises its own input by a marker it attaches itself, and handles every other volume key, including the knob's. The knob has worked since. Several safety nets were added at the same time; see [Hardening](#hardening-added-with-the-fix).

## The symptom

- In CapCut, with a Scroll rule selected, turning the knob still changed the system volume, and Windows' volume pop-up appeared.
- The line at the bottom of the Knob page never changed. It reports every knob event the engine handles, so the engine wasn't seeing a single click.

## How the knob reaches Windows

The F75 (wired, `258A:010C`) presents two USB interfaces. The second one is split into seven HID collections:

| Interface | Collection | What it is | Windows driver |
|---|---|---|---|
| `MI_00` | | Boot keyboard | `kbdhid` |
| `MI_01` | `COL01` | System control (usage page `0x01`, usage `0x80`) | none |
| `MI_01` | `COL02` | **Consumer control (usage page `0x0C`, usage `0x01`): the knob** | none |
| `MI_01` | `COL03`, `COL05` | Vendor-defined | none |
| `MI_01` | `COL04` | Keyboard | `kbdhid` |
| `MI_01` | `COL06` | Vendor-defined (`0xFF00`): the configuration channel AULA Studio writes to | none |
| `MI_01` | `COL07` | Mouse | `mouhid` |

The knob's clicks come from `COL02`:

- Each report is three bytes: report ID `2`, then one 16-bit consumer usage. The array covers usages `0x000`–`0x2FF`.
- A clockwise click sends `0xE9` (Volume Increment), a counter-clockwise click sends `0xEA` (Volume Decrement), and each click is followed by `0x0000` (released).

`COL02` has no function driver, so these reports never go through the keyboard driver. Windows' input stack reads the consumer-control collection itself and **synthesizes** `VK_VOLUME_UP` / `VK_VOLUME_DOWN` keystrokes from it. (The "Human Interface Device Service", `hidserv`, runs in session 0 and can't inject into the user's desktop, so it isn't the path here.)

Those synthesized keystrokes do reach low-level keyboard hooks, but with a telling signature. This is a line from AULA Studio's `knob.log`, recorded on the real keyboard:

```
hook: Volume down, synthesized by Windows, scan 00, flags 11
```

`flags 11` is `LLKHF_EXTENDED (0x01) | LLKHF_INJECTED (0x10)`, and the scan code is 0. Both mean the keystroke was created in software, not by a keyboard driver.

## The bug

The hook turned `LLKHF_INJECTED` into an `Injected` flag, and the knob engine skipped any key that carried it:

```csharp
// Input/KeyboardHook.cs (before)
var e = new KeyEvent((int)info.vkCode, message is WM_KEYDOWN or WM_SYSKEYDOWN, (info.flags & LLKHF_INJECTED) != 0);

// Knob/KnobEngine.cs (before)
if (e.Injected || e.Vk is not (VolumeUp or VolumeDown or Mute)) return false;
```

The intent was to skip AULA Studio's *own* input. When the knob scrolls, AULA Studio sends a wheel event with `SendInput`, and for some modes Ctrl/Alt/Shift or shortcut keys too. Those come back through the same hook marked as injected.

But `LLKHF_INJECTED` only means "not from a keyboard driver"; it doesn't mean "from this app". Every knob click matched the filter, so the engine returned without swallowing it. The key carried on to Windows, Windows changed the volume, and nothing ever reached the activity line.

## The fix

AULA Studio already tagged everything it sends with a marker in `dwExtraInfo`: `0x4B4E4F42`, which spells "KNOB" in ASCII. The hook now reports that tag separately, and the knob engine skips only AULA Studio's own input:

```csharp
// Input/KeyboardHook.cs
public readonly record struct KeyEvent(int Vk, bool Down, bool Injected, bool Ours, uint ScanCode, uint Flags);

var e = new KeyEvent((int)info.vkCode, message is WM_KEYDOWN or WM_SYSKEYDOWN, (info.flags & LLKHF_INJECTED) != 0,
                     info.dwExtraInfo == Marker, info.scanCode, info.flags);

// Knob/KnobEngine.cs
if (e.Ours || e.Vk is not (VolumeUp or VolumeDown or Mute)) return false;
```

A knob click is now handled properly:

- The hook returns 1, swallowing the key, so Windows never changes the volume.
- The engine performs the action set for the app in front.

The key tester on the Keyboard page got the same correction.

## Hardening added with the fix

1. **A second route: Raw Input.**
   - `Input/ConsumerInput.cs` registers for consumer-control reports (usage page `0x0C`, usage `0x01`, `RIDEV_INPUTSINK`) on a message-only window.
   - It decodes them with `HidP_GetUsages` and reports newly pressed usages.
   - Only reports from the F75 count: the wired keyboard `258A:010C`, or its 2.4 GHz receiver `3554:FA09`.
2. **Pairing.**
   - Each click can now arrive twice: as a key in the hook and as a HID report, within a few milliseconds of each other.
   - The engine pairs them (the same key, within 250 ms), so each click is handled exactly once, whichever copy arrives first.
3. **Self-healing.**
   - If a HID click arrives and the hook's copy doesn't follow within 250 ms, the click is handled anyway, a little late.
   - The hook is then reinstalled, because Windows silently removes low-level hooks that once took too long to answer (`LowLevelHooksTimeout`).
   - If clicks still miss the hook after that, the engine switches to the Raw Input route for good.
4. **The volume guard.**
   - Raw Input can only listen; it can't stop Windows from changing the volume.
   - `Knob/VolumeGuard.cs` watches the default playback device through Core Audio (`IAudioEndpointVolume` change notifications). Right after a click that was used for something else, it puts the level and mute state back.
   - In the self-test it undid Windows' change within about 20 ms. Windows' volume pop-up still flashes on this route.
   - It also covers any case where Windows changes the volume even though the hook swallowed the key.
5. **A faster per-app lookup.**
   - The hook callback finds the app in front with `QueryFullProcessImageName` instead of `Process.GetProcessById`, which enumerates every process on the system.
   - Windows drops hooks that answer slowly, so the callback has to stay fast.
6. **Troubleshooting aids.**
   - The Knob page's activity line now also shows the latest knob event that happened while another page was showing.
   - `knob.log` and `--knobtest` were added.

## Verification

- **Synthesized volume keys, sent the way Windows sends the knob's:**
  - With a Volume rule, they pass through to Windows.
  - With a Scroll rule, they are swallowed and turned into scrolls, and the volume stays put.
- **`AulaStudio.exe --knobtest`:**

  ```
  knob test: volume is 32
  volume put back from 34 to 32
  knob test: with the guard, Windows' change was undone (volume 32)
  knob test: without the guard, Windows changed the volume (volume 34)
  knob test: volume back at 32; PASSED
  ```

- **The real knob, turned in CapCut:**

  ```
  14:00:21.872  HID: Volume down from 258A:010C COL02 (F75)
  14:00:21.872  hook: Volume down, synthesized by Windows, scan 00, flags 11, same click as the HID report
  14:00:21.873    -> CapCut: Scroll ↺   (CapCut)
  ```

  Every click took the keyboard-hook route and was paired with its HID report, and the volume guard never had to act.

## Takeaway

`LLKHF_INJECTED` says a keystroke didn't come from a keyboard driver. On Windows, media keys from consumer-control collections never do: that covers volume knobs, and the media keys of many keyboards. To skip your own `SendInput` events, tag them through `dwExtraInfo` and check for that tag, not for the injected flag.
