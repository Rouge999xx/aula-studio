# The AULA F75 configuration protocol

These are the parts of the F75's protocol that AULA Studio uses. Everything here was checked against a real keyboard (wired, `258A:010C`) before AULA Studio wrote anything to it. The starting points were the reverse-engineering notes of [open-aula](https://github.com/paulcoding810/open-aula) and [openaula](https://github.com/not-ayan/openaula). Where those notes and the keyboard disagree, this page follows the keyboard.

## The configuration channel

- **Collection:** `MI_01 COL06`, a vendor-defined HID collection (usage page `0xFF00`, usage `0x01`).
- **Reports:** 520-byte feature reports with report ID `6`. AULA Studio finds the collection by its VID/PID and checks those three values before opening it.
- **Packet:**

  ```
  byte  0     1     2     3     4      5     6       7       8 ... 519
        06    cmd   sub   00    total  idx   sizeLo  sizeHi  data (up to 512 bytes)
  ```

  - `sub` is the layer for key tables.
  - Every transfer AULA Studio makes fits in one packet, so `total = 1` and `idx = 0`.
  - The size is little-endian, e.g. `00 02` = 512.

- **Reading:**
  1. Send the header with the command's read bit set (`cmd | 0x80`), using `HidD_SetFeature`.
  2. Wait about 20 ms.
  3. Fetch the reply with `HidD_GetFeature`.

  The reply echoes `cmd` and `sub` in bytes 1–2, and the data starts at byte 8.
- **Writing:** send the header plus the data with `HidD_SetFeature`, then wait about 30 ms so the keyboard can commit to flash.
- **Pacing:** wait about 20 ms before every packet.

### Commands used

| Command | Direction | Size | What it is |
|---|---|---|---|
| `0x83` (`sub` = layer) | read | 512 | Key table: layer 0 = base, layer 1 = Fn layer |
| `0x03` (`sub` = layer) | **write** | 512 | Key table. The only command AULA Studio ever writes. |
| `0x84` | read | 128 | Lighting block. Saved in backups, never written. |
| `0x8A` | read | 512 | Color table. Saved in backups, never written. |

AULA Studio never sends lighting writes, macros, reset (`0x11`) or firmware commands.

## The key table

128 slots of 4 bytes each. A slot's index is the key's position in the keyboard matrix. AULA's own layout file (`Dev\kb\1\KB.ini`) lists the positions, and AULA Studio embeds them in `Layout/F75Layout.cs`.

| Slot | Key |
|---|---|
| 0 | Esc |
| 11 | Left Win |
| 53 | Fn |
| 59 | Right Ctrl |
| 84 | Knob (press) |
| 127 | Trailer, always `00 00 5A A5` |

### Slot formats

| Bytes | Meaning | Example |
|---|---|---|
| `00 mods 00 key` | Keyboard key: HID usage in byte 3, modifier bits in byte 1 | Esc = `00 00 00 29`, Ctrl+Shift+Z = `00 03 00 1D` |
| `00 mods 00 00` | Modifier on its own | Left Ctrl = `00 01 00 00`, Left Win = `00 08 00 00` |
| `00 00 00 00` | Off (the key does nothing) | |
| `02 00 hi lo` | Media key (consumer usage, big-endian) | Play/Pause = `02 00 00 CD` |
| `0D 00 00 00` | Fn | slot 53 |
| `07 …` / `08 …` | The keyboard's own functions | see below |
| `01 …` | Mouse function | |
| `03 …` | Macro | |

Modifier bits: `01` Left Ctrl, `02` Left Shift, `04` Left Alt, `08` Left Win, `10` Right Ctrl, `20` Right Shift, `40` Right Alt, `80` Right Win.

**Correction to open-aula's notes:** they place the key's HID usage in byte 2 (`00 mods key 00`). The keyboard reports it in byte 3 (`00 mods 00 key`), and writes in that format work.

Keyboard functions seen on this F75:

| Bytes | Function |
|---|---|
| `07 00 00 01` | Lock the Win key |
| `07 00 00 1D` | Knob press (volume / lighting) |
| `08 03 01 00` / `08 03 02 00` | Backlight brighter / dimmer |
| `08 04 01 00` / `08 04 02 00` | Lighting faster / slower |
| `08 00 00 00` | Next lighting effect |

AULA Studio keeps any entry it doesn't recognise byte for byte.

## Things to be careful about

- **Fn (slot 53):** writing anything other than `0D 00 00 00` there removes every Fn shortcut, because Fn is gone. Writing `0D 00 00 00` back fixes it, which is what **Backups › Repair Fn key** does.
- **Win key (slot 11):** it's `00 08 00 00` (a modifier on its own). **Repair Win key** writes that back.
- **One writer at a time:** AULA's own app (`OemDrv.exe`) writes the same memory, so AULA Studio refuses to write while it's running.
- **The 2.4 GHz receiver (`3554:FA09`)** speaks a different protocol and isn't supported for configuration.

## The knob

**Rotation isn't in the key table.** The firmware sends it as consumer-control usages on `MI_01 COL02`:

- report ID `2`, one 16-bit usage per report;
- `0xE9` = clockwise (Volume Increment), `0xEA` = counter-clockwise (Volume Decrement);
- then `0x0000` on release.

Windows turns these into volume keys itself. [knob-detection.md](knob-detection.md) explains how AULA Studio intercepts them. The press is the keyboard function in slot 84. Holding the knob down switches it between volume and backlight brightness.
