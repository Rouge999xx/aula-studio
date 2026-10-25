using System.Text.Json;
using System.Text.Json.Serialization;
using AulaStudio.Input;

namespace AulaStudio.Settings;

public enum KnobMode { Volume, Scroll, ScrollSideways, Zoom, Keys }
public enum ZoomModifier { Ctrl, Alt, Shift }
/// <summary>A key held while scrolling, e.g. Alt+scroll moves sideways in Qt apps such as CapCut.</summary>
public enum HoldKey { None, Ctrl, Alt, Shift }
public enum PressAction { Mute, SwitchMode, PlayPause }

/// <summary>A key plus modifiers, stored as a Windows virtual-key code.</summary>
public sealed class Shortcut
{
    public int Vk { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    public Shortcut() { }
    public Shortcut(int vk, bool ctrl = false, bool shift = false, bool alt = false) { Vk = vk; Ctrl = ctrl; Shift = shift; Alt = alt; }

    public bool SameAs(Shortcut other) => Vk == other.Vk && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt && Win == other.Win;
    public Shortcut Copy() => new() { Vk = Vk, Ctrl = Ctrl, Shift = Shift, Alt = Alt, Win = Win };

    public override string ToString()
    {
        if (Vk == 0) return "Nothing";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        parts.Add(KeyNames.NameOfVk(Vk));
        return string.Join("+", parts);
    }
}

/// <summary>What one detent of the knob does.</summary>
public sealed class KnobAction
{
    public KnobMode Mode { get; set; } = KnobMode.Volume;
    public ZoomModifier ZoomWith { get; set; } = ZoomModifier.Ctrl;
    public HoldKey ScrollWith { get; set; } = HoldKey.None;
    public Shortcut Clockwise { get; set; } = new();
    public Shortcut CounterClockwise { get; set; } = new();

    public string Describe() => Mode switch
    {
        KnobMode.Volume => "Volume",
        KnobMode.Scroll => ScrollWith == HoldKey.None ? "Scroll" : $"{ScrollWith}+scroll",
        KnobMode.ScrollSideways => ScrollWith == HoldKey.None ? "Scroll sideways" : $"{ScrollWith}+scroll sideways",
        KnobMode.Zoom => $"Zoom ({ZoomWith}+scroll)",
        KnobMode.Keys => KeyPresets.Find(this)?.Short ?? $"{CounterClockwise} / {Clockwise}",
        _ => Mode.ToString(),
    };

    public KnobAction Copy() => new()
    {
        Mode = Mode, ZoomWith = ZoomWith, ScrollWith = ScrollWith,
        Clockwise = Clockwise.Copy(), CounterClockwise = CounterClockwise.Copy(),
    };

    public bool SameAs(KnobAction other) => Mode == other.Mode && Mode switch
    {
        KnobMode.Zoom => ZoomWith == other.ZoomWith,
        KnobMode.Scroll or KnobMode.ScrollSideways => ScrollWith == other.ScrollWith,
        KnobMode.Keys => Clockwise.SameAs(other.Clockwise) && CounterClockwise.SameAs(other.CounterClockwise),
        _ => true,
    };
}

/// <summary>The modes offered in the tray menu, which apply in every app until switched back to Automatic.</summary>
public static class ManualModes
{
    public static readonly IReadOnlyList<(string Label, KnobAction Action)> All = new (string, KnobAction)[]
    {
        ("Volume", new() { Mode = KnobMode.Volume }),
        ("Scroll", new() { Mode = KnobMode.Scroll }),
        ("Scroll sideways", new() { Mode = KnobMode.ScrollSideways }),
        ("Zoom (Ctrl+scroll)", new() { Mode = KnobMode.Zoom, ZoomWith = ZoomModifier.Ctrl }),
        ("Zoom (Alt+scroll)", new() { Mode = KnobMode.Zoom, ZoomWith = ZoomModifier.Alt }),
        ("Frame step (← / →)", new() { Mode = KnobMode.Keys, Clockwise = new(0x27), CounterClockwise = new(0x25) }),
        ("Frame step (PgUp / PgDn)", new() { Mode = KnobMode.Keys, Clockwise = new(0x22), CounterClockwise = new(0x21) }),
        ("Jump between edits (↑ / ↓)", new() { Mode = KnobMode.Keys, Clockwise = new(0x28), CounterClockwise = new(0x26) }),
    };
}

/// <summary>Ready-made key pairs for the knob's "Keys" mode.</summary>
public static class KeyPresets
{
    public sealed record Preset(string Name, string Short, Shortcut Clockwise, Shortcut CounterClockwise);

    public static readonly IReadOnlyList<Preset> All = new Preset[]
    {
        new("Frame step  (→ / ←)", "Frame step (← / →)", new(0x27), new(0x25)),
        new("Frame step  (Page Down / Page Up)", "Frame step (PgUp / PgDn)", new(0x22), new(0x21)),
        new("Next / previous edit  (↓ / ↑)", "Jump between edits", new(0x28), new(0x26)),
        new("Zoom timeline  (= / -)", "Zoom (= / -)", new(0xBB), new(0xBD)),
        new("Redo / Undo", "Undo / Redo", new(0x5A, ctrl: true, shift: true), new(0x5A, ctrl: true)),
    };

    public static Preset? Find(KnobAction action) =>
        All.FirstOrDefault(p => p.Clockwise.SameAs(action.Clockwise) && p.CounterClockwise.SameAs(action.CounterClockwise));
}

public sealed class KnobProfile
{
    public string Name { get; set; } = "";
    /// <summary>Process name without ".exe"; empty means "every other app".</summary>
    public string ProcessName { get; set; } = "";
    public KnobAction Turn { get; set; } = new();
    public KnobAction SecondTurn { get; set; } = new();
    public PressAction Press { get; set; } = PressAction.Mute;
    /// <summary>1-5; 3 is one normal mouse-wheel notch per detent.</summary>
    public int Speed { get; set; } = 3;
    public bool Reverse { get; set; }
    public bool Accelerate { get; set; } = true;

    [JsonIgnore] public bool IsDefault => ProcessName.Length == 0;

    public static List<KnobProfile> Defaults() => new()
    {
        new() { Name = "Everywhere else", Turn = new() { Mode = KnobMode.Volume } },
        // Premiere Pro: the wheel scrolls the timeline; press to step frame by frame with the arrow keys.
        new()
        {
            Name = "Premiere Pro", ProcessName = "Adobe Premiere Pro", Press = PressAction.SwitchMode,
            Turn = new() { Mode = KnobMode.Scroll },
            SecondTurn = new() { Mode = KnobMode.Keys, Clockwise = new(0x27), CounterClockwise = new(0x25) },
        },
        // After Effects: Page Down / Page Up step one frame; press to zoom the timeline with Alt+scroll.
        new()
        {
            Name = "After Effects", ProcessName = "AfterFX", Press = PressAction.SwitchMode,
            Turn = new() { Mode = KnobMode.Keys, Clockwise = new(0x22), CounterClockwise = new(0x21) },
            SecondTurn = new() { Mode = KnobMode.Zoom, ZoomWith = ZoomModifier.Alt },
        },
        // CapCut (a Qt app): a plain scroll moves the track list, so move along the timeline with a sideways scroll;
        // press to zoom it with Ctrl+scroll.
        new()
        {
            Name = "CapCut", ProcessName = "CapCut", Press = PressAction.SwitchMode,
            Turn = new() { Mode = KnobMode.ScrollSideways },
            SecondTurn = new() { Mode = KnobMode.Zoom, ZoomWith = ZoomModifier.Ctrl },
        },
    };
}

public sealed class AppSettings
{
    const int CurrentVersion = 2;

    /// <summary>0 in files written before versioning existed; new settings start at the current version.</summary>
    public int Version { get; set; }
    public bool KnobEnabled { get; set; } = true;
    public bool Advanced { get; set; }
    public bool CloseToTray { get; set; } = true;
    /// <summary>Set from the tray menu: one mode for every app. Null means Automatic (per-app profiles).</summary>
    public KnobAction? ManualMode { get; set; }
    public List<KnobProfile> Profiles { get; set; } = KnobProfile.Defaults();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AulaStudio", "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (settings is not null)
                {
                    if (!settings.Profiles.Any(p => p.IsDefault)) settings.Profiles.Insert(0, KnobProfile.Defaults()[0]);
                    Migrate(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException) { App.Log(ex); }
        return new AppSettings { Version = CurrentVersion };
    }

    /// <summary>
    /// Version 1 shipped CapCut with a plain scroll, which only moves CapCut's track list. If the CapCut entry is still
    /// that untouched default, switch it to the sideways scroll that moves along the timeline.
    /// </summary>
    static void Migrate(AppSettings settings)
    {
        if (settings.Version >= CurrentVersion) return;
        var capcut = settings.Profiles.FirstOrDefault(p => p.ProcessName.Equals("CapCut", StringComparison.OrdinalIgnoreCase));
        if (capcut is { Turn: { Mode: KnobMode.Scroll, ScrollWith: HoldKey.None } }) capcut.Turn.Mode = KnobMode.ScrollSideways;
        settings.Version = CurrentVersion;
        settings.Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (IOException ex) { App.Log(ex); }
    }
}
