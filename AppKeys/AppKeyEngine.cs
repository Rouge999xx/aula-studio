using System.Diagnostics;
using System.Windows.Threading;
using AulaStudio.Input;
using AulaStudio.Settings;

namespace AulaStudio.AppKeys;

/// <summary>
/// Per-app key rules (the App keys page). While an app is in front, a key can send a different key or shortcut,
/// run a macro, type text, or do nothing. The keyboard hook swallows the key and AULA Studio sends the replacement,
/// tagged so that nothing it sends is ever remapped again. A rule for the app in front wins over an "Every app" rule
/// for the same key. Nothing here is stored in the keyboard, so the rules only work while AULA Studio is running.
/// </summary>
public sealed class AppKeyEngine
{
    // Windows repeats a held key after at most 1 s, then every ~30 ms. A "repeat" later than this means the release was missed.
    static readonly long RepeatGap = Stopwatch.Frequency * 1100 / 1000;
    // A short pause between macro steps, so apps keep up with them.
    const int StepGapMs = 20;

    /// <summary>A key that's down right now, and what it was turned into, so its release matches even if the app changed meanwhile.</summary>
    sealed class Held
    {
        public required KeyRule Rule;
        public required Shortcut Output;
        public long LastDown;
    }

    readonly AppSettings settings;
    readonly Dispatcher dispatcher;
    readonly Dictionary<int, Held> held = new();
    readonly HashSet<KeyRule> running = new();   // macros that haven't finished yet

    /// <summary>Raised on the UI thread for every rule used: (app it was in, what happened).</summary>
    public event Action<string, string>? Activity;
    /// <summary>The latest rule used, for an App keys page opened after it happened.</summary>
    public (string App, string What)? LastActivity { get; private set; }

    public AppKeyEngine(KeyboardHook hook, AppSettings settings, Dispatcher dispatcher)
    {
        this.settings = settings;
        this.dispatcher = dispatcher;
        hook.Add(OnKey);
    }

    /// <summary>The volume and mute keys are the knob's; the Knob page decides what they do.</summary>
    public static bool IsKnobKey(int vk) => vk is 0xAD or 0xAE or 0xAF;

    bool OnKey(KeyEvent e)
    {
        if (e.Ours || IsKnobKey(e.Vk)) return false;
        if (!e.Down) return Release(e.Vk);

        long now = Stopwatch.GetTimestamp();
        if (held.TryGetValue(e.Vk, out var key))
        {
            if (now - key.LastDown < RepeatGap)
            {
                key.LastDown = now;
                if (key.Rule.Kind == KeyActionKind.Key) dispatcher.BeginInvoke(() => Inject.KeyRepeat(key.Output));
                return true;
            }
            Release(e.Vk);   // its release never arrived (e.g. the PC locked while it was held): let go first
        }
        if (!settings.AppKeysEnabled || !settings.KeyProfiles.Any(p => p.Rules.Any(r => r.Trigger == e.Vk))) return false;

        var (rule, app) = Find(e.Vk);
        if (rule is null) return false;
        var output = rule.Output.Copy();
        held[e.Vk] = new Held { Rule = rule, Output = output, LastDown = now };
        string trigger = KeyNames.NameOfVk(e.Vk);
        dispatcher.BeginInvoke(() => Press(rule, output, trigger, app));
        return true;
    }

    bool Release(int vk)
    {
        if (!held.Remove(vk, out var key)) return false;
        if (key.Rule.Kind == KeyActionKind.Key) dispatcher.BeginInvoke(() => Inject.KeyUp(key.Output));
        return true;
    }

    void Press(KeyRule rule, Shortcut output, string trigger, string app)
    {
        switch (rule.Kind)
        {
            case KeyActionKind.Key:
                Inject.KeyDown(output);
                Report(app, $"{trigger}  →  {output}");
                break;
            case KeyActionKind.Macro:
            case KeyActionKind.Text:
                var steps = rule.Kind == KeyActionKind.Text
                    ? new List<MacroStep> { new() { Kind = MacroStepKind.Text, Text = rule.Text } }
                    : rule.Steps.Select(s => s.Copy()).ToList();
                if (Run(rule, steps)) Report(app, $"{trigger}  →  {rule.Describe()}");
                break;
            default:
                Report(app, $"{trigger}  →  nothing (blocked here)");
                break;
        }
    }

    /// <summary>Runs a macro once, off the UI thread. A second press while it's still running is ignored.</summary>
    bool Run(KeyRule rule, List<MacroStep> steps)
    {
        if (!running.Add(rule)) return false;
        Task.Run(async () =>
        {
            foreach (var step in steps)
            {
                switch (step.Kind)
                {
                    case MacroStepKind.Keys: Inject.Press(step.Keys); break;
                    case MacroStepKind.Text: Inject.Type(step.Text); break;
                    case MacroStepKind.Wait: await Task.Delay(Math.Clamp(step.Milliseconds, 0, 60_000)); break;
                }
                await Task.Delay(StepGapMs);
            }
        }).ContinueWith(_ => dispatcher.BeginInvoke(() => running.Remove(rule)));
        return true;
    }

    void Report(string app, string what)
    {
        LastActivity = (app, what);
        Activity?.Invoke(app, what);
    }

    /// <summary>The rule for this key in the app in front, falling back to "Every app". Never inside AULA Studio itself.</summary>
    (KeyRule? Rule, string App) Find(int vk)
    {
        NativeInput.GetWindowThreadProcessId(NativeInput.GetForegroundWindow(), out uint pid);
        string process = pid == 0 ? "" : NativeInput.ProcessName(pid);
        if (process.Equals("AulaStudio", StringComparison.OrdinalIgnoreCase)) return (null, "");   // keeps its own editors usable

        var profile = settings.KeyProfiles.FirstOrDefault(p => !p.IsDefault && p.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase));
        var rule = profile?.Rules.FirstOrDefault(r => r.Trigger == vk && r.IsSet);
        if (rule is not null) return (rule, profile!.Name);
        rule = settings.KeyProfiles.FirstOrDefault(p => p.IsDefault)?.Rules.FirstOrDefault(r => r.Trigger == vk && r.IsSet);
        return (rule, process.Length > 0 ? process : "Desktop");
    }
}
