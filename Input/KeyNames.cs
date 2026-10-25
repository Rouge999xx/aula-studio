using AulaStudio.Protocol;

namespace AulaStudio.Input;

/// <summary>Translation between Windows virtual-key codes (what the hook sees) and HID usages (what the keyboard stores).</summary>
public static class KeyNames
{
    static readonly Dictionary<int, byte> vkToHid = BuildMap();
    static readonly Dictionary<byte, int> hidToVk = vkToHid.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key);

    public static byte? HidFromVk(int vk) => vkToHid.TryGetValue(vk, out var usage) ? usage : null;
    public static int? VkFromHid(byte usage) => hidToVk.TryGetValue(usage, out var vk) ? vk : null;

    /// <summary>Modifier keys are stored as a modifier bit, not as a key usage.</summary>
    public static byte ModifierBit(int vk) => vk switch
    {
        0xA2 or 0x11 => 0x01, 0xA0 or 0x10 => 0x02, 0xA4 or 0x12 => 0x04, 0x5B => 0x08,
        0xA3 => 0x10, 0xA1 => 0x20, 0xA5 => 0x40, 0x5C => 0x80,
        _ => 0,
    };

    public static string NameOfVk(int vk) => vk switch
    {
        0xAD => "Mute", 0xAE => "Volume down", 0xAF => "Volume up",
        0xB0 => "Next track", 0xB1 => "Previous track", 0xB2 => "Stop", 0xB3 => "Play/Pause",
        0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl", 0xA0 => "Left Shift", 0xA1 => "Right Shift",
        0xA4 => "Left Alt", 0xA5 => "Right Alt", 0x5B => "Left Win", 0x5C => "Right Win",
        0xA6 => "Browser back", 0xA7 => "Browser forward", 0xAC => "Browser home", 0xB4 => "Mail", 0xB7 => "Calculator",
        _ => HidFromVk(vk) is byte usage ? HidUsages.KeyName(usage) : $"Key 0x{vk:X2}",
    };

    /// <summary>Keys that need the "extended" flag when injected, or apps read them as numpad keys.</summary>
    public static bool IsExtended(int vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5 or 0x6F or 0x90;

    static Dictionary<int, byte> BuildMap()
    {
        var map = new Dictionary<int, byte>();
        for (int i = 0; i < 26; i++) map[0x41 + i] = (byte)(0x04 + i);                 // A-Z
        for (int i = 1; i <= 9; i++) map[0x30 + i] = (byte)(0x1E + i - 1);             // 1-9
        map[0x30] = 0x27;                                                              // 0
        for (int i = 0; i < 12; i++) map[0x70 + i] = (byte)(0x3A + i);                 // F1-F12
        for (int i = 0; i < 12; i++) map[0x7C + i] = (byte)(0x68 + i);                 // F13-F24
        int[] numpad = { 0x62, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x61 };  // Numpad 0-9
        for (int i = 0; i < 10; i++) map[0x60 + i] = (byte)numpad[i];
        (int Vk, byte Hid)[] rest =
        {
            (0x0D, 0x28), (0x1B, 0x29), (0x08, 0x2A), (0x09, 0x2B), (0x20, 0x2C),
            (0xBD, 0x2D), (0xBB, 0x2E), (0xDB, 0x2F), (0xDD, 0x30), (0xDC, 0x31), (0xBA, 0x33), (0xDE, 0x34),
            (0xC0, 0x35), (0xBC, 0x36), (0xBE, 0x37), (0xBF, 0x38), (0x14, 0x39),
            (0x2C, 0x46), (0x91, 0x47), (0x13, 0x48), (0x2D, 0x49), (0x24, 0x4A), (0x21, 0x4B), (0x2E, 0x4C),
            (0x23, 0x4D), (0x22, 0x4E), (0x27, 0x4F), (0x25, 0x50), (0x28, 0x51), (0x26, 0x52),
            (0x90, 0x53), (0x6F, 0x54), (0x6A, 0x55), (0x6D, 0x56), (0x6B, 0x57), (0x6E, 0x63), (0x5D, 0x65),
        };
        foreach (var (vk, hid) in rest) map[vk] = hid;
        return map;
    }
}
