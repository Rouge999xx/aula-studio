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

public enum KeyActionKind { Key, Macro, Text, Off }
public enum MacroStepKind { Keys, Text, Wait }

/// <summary>One step of a macro: press a key or shortcut, type some text, or wait.</summary>
public sealed class MacroStep
{
    public MacroStepKind Kind { get; set; }
    public Shortcut Keys { get; set; } = new();
    public string Text { get; set; } = "";
    public int Milliseconds { get; set; } = 100;

    public MacroStep Copy() => new() { Kind = Kind, Keys = Keys.Copy(), Text = Text, Milliseconds = Milliseconds };

    public string Describe() => Kind switch
    {
        MacroStepKind.Keys => "Press " + Keys,
        MacroStepKind.Text => $"Type “{KeyRule.Preview(Text)}”",
        _ => $"Wait {Milliseconds} ms",
    };
}

/// <summary>What one key does while a given app is in front.</summary>
public sealed class KeyRule
{
    /// <summary>The key as Windows sees it (after any remapping stored in the keyboard), as a virtual-key code.</summary>
    public int Trigger { get; set; }
    public KeyActionKind Kind { get; set; } = KeyActionKind.Key;
    /// <summary>For <see cref="KeyActionKind.Key"/>: held down as long as the trigger is.</summary>
    public Shortcut Output { get; set; } = new();
    public string Text { get; set; } = "";
    public List<MacroStep> Steps { get; set; } = new();

    /// <summary>A rule that isn't filled in yet leaves its key alone, so a half-made rule never blocks a key.</summary>
    [JsonIgnore] public bool IsSet => Kind switch
    {
        KeyActionKind.Key => Output.Vk != 0,
        KeyActionKind.Macro => Steps.Count > 0,
        KeyActionKind.Text => Text.Length > 0,
        _ => true,
    };

    public string Describe() => Kind switch
    {
        KeyActionKind.Key => Output.Vk == 0 ? "Not set yet" : Output.ToString(),
        KeyActionKind.Macro => Steps.Count == 0 ? "Empty macro"
            : "Macro: " + string.Join(",  ", Steps.Take(3).Select(s => s.Describe())) + (Steps.Count > 3 ? ",  …" : ""),
        KeyActionKind.Text => Text.Length == 0 ? "No text yet" : $"Types “{Preview(Text)}”",
        _ => "Does nothing",
    };

    public static string Preview(string text)
    {
        string flat = text.Replace("\r\n", "⏎").Replace('\n', '⏎');
        return flat.Length <= 28 ? flat : flat[..27] + "…";
    }
}

/// <summary>The key rules for one app. An empty ProcessName means "every app".</summary>
public sealed class KeyProfile
{
    public string Name { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public List<KeyRule> Rules { get; set; } = new();

    [JsonIgnore] public bool IsDefault => ProcessName.Length == 0;

    public static KeyProfile EveryApp() => new() { Name = "Every app" };
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
    public bool AppKeysEnabled { get; set; } = true;
    /// <summary>Per-app key rules (the App keys page). The first entry is "Every app".</summary>
    public List<KeyProfile> KeyProfiles { get; set; } = new() { KeyProfile.EveryApp() };

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
                    if (!settings.KeyProfiles.Any(p => p.IsDefault)) settings.KeyProfiles.Insert(0, KeyProfile.EveryApp());
                    Migrate(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            App.Log(ex);
            KeepUnreadable();
        }
        return new AppSettings { Version = CurrentVersion };
    }

    /// <summary>Set when the settings file couldn't be read; the main window shows it once.</summary>
    public static string? LoadProblem { get; private set; }

    /// <summary>
    /// AULA Studio is about to start with default settings. Keep the unreadable file next to the new one first,
    /// so the next save doesn't wipe out the knob and key rules in it for good.
    /// </summary>
    static void KeepUnreadable()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            string copy = Path.ChangeExtension(FilePath, $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(FilePath, copy, overwrite: true);
            LoadProblem = $"Your settings file couldn't be read, so AULA Studio started with default settings. The old file is kept as {Path.GetFileName(copy)}.";
        }
        catch (IOException ex) { App.Log(ex); }
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
            // Write a new file and swap it in, so a crash halfway through can't leave a broken settings file.
            string temp = FilePath + ".new";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (IOException ex) { App.Log(ex); }
    }
}
