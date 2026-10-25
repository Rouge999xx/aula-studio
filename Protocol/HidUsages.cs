namespace AulaStudio.Protocol;

/// <summary>Names for HID keyboard and consumer usages, and the F75's own keyboard functions.</summary>
public static class HidUsages
{
    public record KeyOption(byte Usage, string Name, string Group);
    public record MediaOption(ushort Usage, string Name);

    public static readonly IReadOnlyList<KeyOption> Keys = BuildKeys();

    public static readonly IReadOnlyList<MediaOption> MediaKeys = new MediaOption[]
    {
        new(0x00CD, "Play / Pause"), new(0x00B5, "Next track"), new(0x00B6, "Previous track"), new(0x00B7, "Stop"),
        new(0x00E9, "Volume up"), new(0x00EA, "Volume down"), new(0x00E2, "Mute"),
        new(0x006F, "Screen brighter"), new(0x0070, "Screen dimmer"),
        new(0x0192, "Calculator"), new(0x0194, "File Explorer"), new(0x018A, "Mail"), new(0x0183, "Media player"),
        new(0x0223, "Browser home"), new(0x0224, "Browser back"), new(0x0225, "Browser forward"),
        new(0x0227, "Browser refresh"), new(0x0221, "Search"), new(0x022A, "Bookmarks"),
    };

    // Modifier bits in byte 1 of a key slot.
    public static readonly (byte Bit, string Name, string Short)[] ModifierBits =
    {
        (0x01, "Left Ctrl", "Ctrl"), (0x02, "Left Shift", "Shift"), (0x04, "Left Alt", "Alt"), (0x08, "Left Win", "Win"),
        (0x10, "Right Ctrl", "RCtrl"), (0x20, "Right Shift", "RShift"), (0x40, "Right Alt", "RAlt"), (0x80, "Right Win", "RWin"),
    };

    static readonly Dictionary<byte, string> keyNames = Keys.ToDictionary(k => k.Usage, k => k.Name);
    static readonly Dictionary<ushort, string> mediaNames = MediaKeys.ToDictionary(m => m.Usage, m => m.Name);

    public static string KeyName(byte usage) => keyNames.TryGetValue(usage, out var name) ? name : $"Key 0x{usage:X2}";
    public static string MediaName(ushort usage) => mediaNames.TryGetValue(usage, out var name) ? name : $"Media 0x{usage:X4}";

    public static string DescribeKey(byte modifiers, byte usage)
    {
        // A modifier on its own (e.g. the Left Ctrl key) has no usage, just one modifier bit.
        if (usage == 0)
        {
            var single = ModifierBits.Where(m => (modifiers & m.Bit) != 0).Select(m => m.Name).ToList();
            return single.Count == 0 ? "Off" : string.Join(" + ", single);
        }
        var parts = ModifierBits.Where(m => (modifiers & m.Bit) != 0).Select(m => m.Short).ToList();
        parts.Add(KeyName(usage));
        return string.Join("+", parts);
    }

    /// <summary>The F75's built-in Fn-layer functions, named where their purpose is certain (from AULA's KB.ini and manual).</summary>
    public static string KeyboardFunctionName(KeyEntry e) => (e.B0, e.B1, e.B2, e.B3) switch
    {
        (7, 0, 0, 0x01) => "Lock Win key",
        (7, 0, 0, 0x1D) => "Knob (volume / lighting)",
        (8, 3, 1, 0) => "Backlight brighter",
        (8, 3, 2, 0) => "Backlight dimmer",
        (8, 4, 1, 0) => "Lighting faster",
        (8, 4, 2, 0) => "Lighting slower",
        (8, 0, 0, 0) => "Next lighting effect",
        _ => $"Keyboard function {e.B0:X2}·{e.B3:X2}",
    };

    static List<KeyOption> BuildKeys()
    {
        var list = new List<KeyOption>();
        for (int i = 0; i < 26; i++) list.Add(new((byte)(0x04 + i), ((char)('A' + i)).ToString(), "Letters"));
        string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        for (int i = 0; i < 10; i++) list.Add(new((byte)(0x1E + i), digits[i], "Numbers"));
        list.AddRange(new KeyOption[]
        {
            new(0x28, "Enter", "Editing"), new(0x29, "Esc", "Editing"), new(0x2A, "Backspace", "Editing"), new(0x2B, "Tab", "Editing"),
            new(0x2C, "Space", "Editing"), new(0x49, "Insert", "Editing"), new(0x4C, "Delete", "Editing"),
            new(0x2D, "-", "Symbols"), new(0x2E, "=", "Symbols"), new(0x2F, "[", "Symbols"), new(0x30, "]", "Symbols"),
            new(0x31, "\\", "Symbols"), new(0x33, ";", "Symbols"), new(0x34, "'", "Symbols"), new(0x35, "`", "Symbols"),
            new(0x36, ",", "Symbols"), new(0x37, ".", "Symbols"), new(0x38, "/", "Symbols"),
            new(0x39, "Caps Lock", "Locks"), new(0x47, "Scroll Lock", "Locks"), new(0x53, "Num Lock", "Locks"),
            new(0x46, "Print Screen", "System"), new(0x48, "Pause", "System"), new(0x65, "Menu", "System"),
            new(0x4A, "Home", "Navigation"), new(0x4D, "End", "Navigation"), new(0x4B, "Page Up", "Navigation"), new(0x4E, "Page Down", "Navigation"),
            new(0x4F, "Right", "Navigation"), new(0x50, "Left", "Navigation"), new(0x51, "Down", "Navigation"), new(0x52, "Up", "Navigation"),
        });
        for (int i = 0; i < 12; i++) list.Add(new((byte)(0x3A + i), $"F{i + 1}", "Function keys"));
        for (int i = 0; i < 12; i++) list.Add(new((byte)(0x68 + i), $"F{i + 13}", "Function keys"));
        list.AddRange(new KeyOption[]
        {
            new(0x54, "Num /", "Numpad"), new(0x55, "Num *", "Numpad"), new(0x56, "Num -", "Numpad"), new(0x57, "Num +", "Numpad"),
            new(0x58, "Num Enter", "Numpad"), new(0x63, "Num .", "Numpad"),
        });
        for (int i = 0; i < 9; i++) list.Add(new((byte)(0x59 + i), $"Num {i + 1}", "Numpad"));
        list.Add(new(0x62, "Num 0", "Numpad"));
        return list;
    }
}
