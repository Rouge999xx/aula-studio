namespace AulaStudio.Protocol;

public enum EntryKind { Off, Key, Media, Mouse, Macro, Fn, KeyboardFunction, Unknown }

/// <summary>
/// One 4-byte slot of the F75's key matrix, as read from the keyboard itself:
/// <list type="bullet">
/// <item><c>00 mods 00 key</c>: keyboard key (HID usage) plus modifier bits (01 LCtrl, 02 LShift, 04 LAlt, 08 LWin, 10/20/40/80 right side)</item>
/// <item><c>02 00 hi lo</c>: media / consumer usage</item>
/// <item><c>0D 00 00 00</c>: Fn</item>
/// <item><c>07 …</c> / <c>08 …</c>: keyboard's own functions (Win lock, backlight, channels…) - kept byte for byte</item>
/// </list>
/// </summary>
public readonly record struct KeyEntry(byte B0, byte B1, byte B2, byte B3)
{
    public static readonly KeyEntry Off = new(0, 0, 0, 0);
    public static readonly KeyEntry FnKey = new(0x0D, 0, 0, 0);
    public static readonly KeyEntry Trailer = new(0, 0, 0x5A, 0xA5);

    public static KeyEntry Keyboard(byte usage, byte modifiers = 0) => new(0, modifiers, 0, usage);
    public static KeyEntry Media(ushort usage) => new(2, 0, (byte)(usage >> 8), (byte)usage);

    public EntryKind Kind => B0 switch
    {
        0 when B1 == 0 && B2 == 0 && B3 == 0 => EntryKind.Off,
        0 => EntryKind.Key,
        1 => EntryKind.Mouse,
        2 => EntryKind.Media,
        3 => EntryKind.Macro,
        0x0D => EntryKind.Fn,
        7 or 8 => EntryKind.KeyboardFunction,
        _ => EntryKind.Unknown,
    };

    public byte Modifiers => B1;
    public byte Usage => B3;
    public ushort MediaUsage => (ushort)(B2 << 8 | B3);

    public string Hex => $"{B0:X2} {B1:X2} {B2:X2} {B3:X2}";

    public string Describe() => Kind switch
    {
        EntryKind.Off => "Off",
        EntryKind.Key => HidUsages.DescribeKey(Modifiers, Usage),
        EntryKind.Media => HidUsages.MediaName(MediaUsage),
        EntryKind.Fn => "Fn",
        EntryKind.Macro => $"Macro {B3 + 1}",
        EntryKind.Mouse => "Mouse function",
        EntryKind.KeyboardFunction => HidUsages.KeyboardFunctionName(this),
        _ => $"Raw {Hex}",
    };

    public static KeyEntry FromBytes(ReadOnlySpan<byte> b) => new(b[0], b[1], b[2], b[3]);
}
