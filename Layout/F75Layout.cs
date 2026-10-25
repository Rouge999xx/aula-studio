namespace AulaStudio.Layout;

/// <summary>A physical key: what's printed on it, its slot in the keyboard's key table, and where it sits.</summary>
public sealed record KeyCap(string Label, int Slot, double X1, double Y1, double X2, double Y2, bool IsKnob = false);

/// <summary>
/// The AULA F75's 75% layout. Positions and slot numbers come from AULA's own device file
/// (<c>Dev\kb\1\KB.ini</c>); the knob, which AULA's app doesn't draw, is added at the top right.
/// Coordinates are keycap tops in AULA's 660×290 drawing space.
/// </summary>
public static class F75Layout
{
    public const double Width = 660, Height = 290;

    public static readonly IReadOnlyList<KeyCap> Keys = new KeyCap[]
    {
        new("Esc", 0, 30, 30, 55, 57),
        new("F1", 12, 107, 30, 132, 57), new("F2", 18, 144, 30, 169, 57), new("F3", 24, 183, 30, 208, 57), new("F4", 30, 222, 30, 247, 57),
        new("F5", 36, 269, 30, 294, 57), new("F6", 42, 305, 30, 330, 57), new("F7", 48, 343, 30, 368, 57), new("F8", 54, 388, 30, 413, 57),
        new("F9", 60, 439, 30, 464, 57), new("F10", 66, 477, 30, 502, 57), new("F11", 72, 516, 30, 541, 57), new("F12", 78, 555, 30, 580, 57),
        new("Knob", 84, 603, 24, 637, 60, IsKnob: true),

        new("`", 1, 31, 81, 56, 108), new("1", 7, 70, 81, 95, 108), new("2", 13, 108, 81, 133, 108), new("3", 19, 147, 81, 172, 108),
        new("4", 25, 184, 81, 209, 108), new("5", 31, 223, 81, 248, 108), new("6", 37, 262, 81, 287, 108), new("7", 43, 300, 81, 325, 108),
        new("8", 49, 338, 81, 363, 108), new("9", 55, 377, 81, 402, 108), new("0", 61, 416, 81, 441, 108), new("-", 67, 453, 81, 478, 108),
        new("=", 73, 491, 81, 516, 108), new("Backspace", 79, 531, 81, 591, 108), new("Delete", 85, 604, 81, 629, 108),

        new("Tab", 2, 34, 120, 78, 147), new("Q", 8, 89, 119, 114, 146), new("W", 14, 127, 119, 152, 146), new("E", 20, 165, 119, 190, 146),
        new("R", 26, 204, 119, 229, 146), new("T", 32, 242, 119, 267, 146), new("Y", 38, 280, 119, 305, 146), new("U", 44, 318, 119, 343, 146),
        new("I", 50, 356, 119, 381, 146), new("O", 56, 395, 119, 420, 146), new("P", 62, 433, 119, 458, 146), new("[", 68, 471, 119, 496, 146),
        new("]", 74, 509, 119, 534, 146), new("\\", 80, 549, 119, 593, 146), new("PgUp", 86, 605, 119, 630, 146),

        new("Caps Lock", 3, 33, 158, 85, 185), new("A", 9, 98, 158, 123, 185), new("S", 15, 136, 158, 161, 185), new("D", 21, 174, 158, 199, 185),
        new("F", 27, 213, 158, 238, 185), new("G", 33, 252, 158, 277, 185), new("H", 39, 290, 158, 315, 185), new("J", 45, 327, 158, 352, 185),
        new("K", 51, 366, 158, 391, 185), new("L", 57, 405, 158, 430, 185), new(";", 63, 443, 158, 468, 185), new("'", 69, 481, 158, 506, 185),
        new("Enter", 81, 521, 158, 591, 185), new("PgDn", 87, 604, 158, 629, 185),

        new("Shift", 4, 33, 195, 105, 222), new("Z", 10, 116, 195, 141, 222), new("X", 16, 155, 195, 180, 222), new("C", 22, 193, 195, 218, 222),
        new("V", 28, 232, 195, 257, 222), new("B", 34, 270, 195, 295, 222), new("N", 40, 309, 195, 334, 222), new("M", 46, 347, 195, 372, 222),
        new(",", 52, 384, 195, 409, 222), new(".", 58, 424, 195, 449, 222), new("/", 64, 463, 195, 488, 222), new("Shift", 70, 502, 195, 558, 222),
        new("↑", 82, 567, 195, 592, 222), new("End", 88, 605, 195, 630, 222),

        new("Ctrl", 5, 34, 235, 69, 262), new("Win", 11, 79, 235, 114, 262), new("Alt", 17, 122, 235, 157, 262), new("", 35, 174, 235, 400, 262),
        new("Fn", 53, 414, 235, 449, 262), new("Ctrl", 59, 463, 235, 497, 262),
        new("←", 77, 529, 235, 554, 262), new("↓", 83, 568, 235, 593, 262), new("→", 89, 605, 235, 630, 262),
    };

    /// <summary>A friendlier name for the editor title ("Right Ctrl" rather than "Ctrl").</summary>
    public static string FullName(KeyCap key) => key.Slot switch
    {
        4 => "Left Shift", 70 => "Right Shift", 5 => "Left Ctrl", 59 => "Right Ctrl", 11 => "Win", 17 => "Alt",
        35 => "Space", 82 => "Up", 77 => "Left", 83 => "Down", 89 => "Right", 84 => "Knob (press)",
        _ => key.Label,
    };
}
