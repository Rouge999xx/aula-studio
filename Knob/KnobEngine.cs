using System.Diagnostics;
using System.Windows.Threading;
using AulaStudio.Input;
using AulaStudio.Settings;

namespace AulaStudio.Knob;

/// <summary>
/// The knob's rotation is hard-wired in the F75's firmware to send Volume Up/Down (and its press sends Mute), from the
/// keyboard's consumer-control collection. This engine intercepts those clicks and, depending on the app in front, turns
/// them into scrolling, zooming or shortcuts. A mode picked in the tray menu overrides the per-app profiles until it's
/// switched back to Automatic. Fn+F11/F12 send the same volume keys, so they follow the same rules.
///
/// Windows can deliver a click two ways, so the engine watches both and pairs them up to handle each click once:
/// - The keyboard hook, as a volume key Windows synthesized from the HID report (so it's flagged as injected).
///   Swallowing it there stops the volume change, which makes this the preferred route.
/// - Raw Input, straight from the keyboard's HID report (<see cref="ConsumerInput"/>). That always arrives, but can't stop
///   Windows from changing the volume too, so <see cref="VolumeGuard"/> puts the volume back.
/// If clicks arrive by Raw Input but never through the hook, the engine reinstalls the hook once (Windows silently drops
/// hooks that were slow to answer), and if that doesn't help, it switches to the Raw Input route for good.
/// </summary>
public sealed class KnobEngine
{
    const int VolumeUp = 0xAF, VolumeDown = 0xAE, Mute = 0xAD, PlayPause = 0xB3;
    // Both copies of a click reach the UI thread within a few milliseconds of each other; this leaves plenty of slack.
    static readonly TimeSpan PairWindow = TimeSpan.FromMilliseconds(250);

    enum Route { Unknown, Hook, Raw }

    /// <summary>One physical click, as seen by the hook and/or Raw Input.</summary>
    sealed class Click
    {
        public int Vk;
        public long Ticks;
        public bool ByHook, ByRaw, Handled, Swallow;
    }

    readonly KeyboardHook hook;
    readonly AppSettings settings;
    readonly Dispatcher dispatcher;
    readonly VolumeGuard? guard;
    readonly HashSet<int> swallowed = new();
    readonly HashSet<KnobProfile> onSecondMode = new();
    readonly List<Click> clicks = new();
    readonly OsdWindow osd = new();
    Route route;
    bool hookReinstalled;
    int misses;
    long lastTurnTicks;
    int lastDirection;

    /// <summary>Raised on the UI thread for every knob event: (app it was in, what happened).</summary>
    public event Action<string, string>? Activity;
    /// <summary>Raised when the tray's manual mode changes.</summary>
    public event Action? ModeChanged;

    /// <summary>The latest knob event, for a Knob page opened after it happened.</summary>
    public (string App, string What)? LastActivity { get; private set; }

    public KnobEngine(KeyboardHook hook, ConsumerInput consumer, AppSettings settings, Dispatcher dispatcher)
    {
        this.hook = hook;
        this.settings = settings;
        this.dispatcher = dispatcher;
        guard = CreateGuard(dispatcher);
        hook.Add(OnKey);
        consumer.Pressed += OnConsumer;
        KnobLog.Write($"started: keyboard hook {(hook.IsActive ? "on" : "FAILED")}, knob HID reader {(consumer.IsListening ? "on" : "FAILED")}, "
                      + $"volume guard {(guard is null ? "unavailable" : "on")}");
    }

    static VolumeGuard? CreateGuard(Dispatcher dispatcher)
    {
        try { return new VolumeGuard(dispatcher); }
        catch (Exception ex)   // it's only the fallback; the knob works without it
        {
            App.Log(ex);
            return null;
        }
    }

    /// <summary>One mode for every app (from the tray), or null to go back to the per-app profiles.</summary>
    public void SetManualMode(KnobAction? mode)
    {
        settings.ManualMode = mode?.Copy();
        settings.Save();
        osd.Flash("Knob mode", mode is null ? "Automatic (per app)" : mode.Describe() + "  ·  every app");
        ModeChanged?.Invoke();
    }

    // ------------------------------------------------------------------ the two routes

    bool OnKey(KeyEvent e)
    {
        if (e.Ours || e.Vk is not (VolumeUp or VolumeDown or Mute)) return false;
        if (!e.Down) return swallowed.Remove(e.Vk);   // swallow the release of a press that was swallowed

        long now = Stopwatch.GetTimestamp();
        var click = Twin(e.Vk, now, fromHook: true);
        KnobLog.Write($"hook: {KeyNames.NameOfVk(e.Vk)}, {(e.Injected ? "synthesized by Windows" : "from a keyboard driver")}, "
                      + $"scan {e.ScanCode:X2}, flags {e.Flags:X2}{(click is null ? "" : ", same click as the HID report")}");
        if (click is null) clicks.Add(click = new Click { Vk = e.Vk, Ticks = now });
        click.ByHook = true;
        if (click.ByRaw) HookWorks();
        if (!click.Handled)
        {
            click.Handled = true;
            click.Swallow = Handle(click.Vk);
        }
        if (click.Swallow) swallowed.Add(e.Vk);
        return click.Swallow;
    }

    void OnConsumer(ConsumerPress press)
    {
        int vk = press.Usage switch { 0xE9 => VolumeUp, 0xEA => VolumeDown, 0xE2 => Mute, _ => 0 };
        if (vk == 0) return;   // other media keys aren't the knob's business
        KnobLog.Write($"HID: {KeyNames.NameOfVk(vk)} from {press.Device}{(press.IsAula ? " (F75)" : "")}");
        if (!press.IsAula) return;   // other devices' volume keys only take the hook route

        long now = Stopwatch.GetTimestamp();
        var click = Twin(vk, now, fromHook: false);
        if (click is not null)   // the hook already has this one
        {
            click.ByRaw = true;
            HookWorks();
            return;
        }
        clicks.Add(click = new Click { Vk = vk, Ticks = now, ByRaw = true });
        if (route == Route.Raw)
        {
            click.Handled = true;
            click.Swallow = Handle(vk);
            return;
        }
        // Give the hook a moment to deliver its copy of this click.
        var wait = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = PairWindow };
        wait.Tick += (_, _) => { wait.Stop(); Missed(click); };
        wait.Start();
    }

    void Missed(Click click)
    {
        if (click.ByHook || click.Handled) return;
        misses++;
        KnobLog.Write($"the hook didn't see that click ({misses} in a row)");
        if (!hookReinstalled)
        {
            hookReinstalled = true;
            hook.Reinstall();
            KnobLog.Write($"reinstalled the keyboard hook: {(hook.IsActive ? "ok" : "FAILED")}");
        }
        else if (route != Route.Raw && misses >= 2)
        {
            route = Route.Raw;
            KnobLog.Write("route: Raw Input from now on (Windows doesn't pass the knob through keyboard hooks here)");
        }
        click.Handled = true;
        click.Swallow = Handle(click.Vk);   // late, but the click still does its job
    }

    void HookWorks()
    {
        misses = 0;
        if (route == Route.Hook) return;
        route = Route.Hook;
        KnobLog.Write("route: keyboard hook (it sees the knob's clicks)");
    }

    /// <summary>The other route's copy of this click: the same key, close in time, not yet seen by this route.</summary>
    Click? Twin(int vk, long now, bool fromHook)
    {
        long window = (long)(PairWindow.TotalSeconds * Stopwatch.Frequency);
        clicks.RemoveAll(c => now - c.Ticks > window * 4);
        return clicks.FirstOrDefault(c => c.Vk == vk && (fromHook ? !c.ByHook : !c.ByRaw) && now - c.Ticks <= window);
    }

    // ------------------------------------------------------------------ what a click does

    /// <summary>
    /// Decides what a click does in the app in front. Returns true if it's used for something other than volume, in which
    /// case the hook swallows it and the volume guard undoes anything Windows does with it anyway.
    /// Runs inside the keyboard hook, so the actual work is queued.
    /// </summary>
    bool Handle(int vk)
    {
        if (!settings.KnobEnabled) return false;

        var (profile, app) = CurrentProfile();
        var manual = settings.ManualMode;
        if (vk == Mute)
        {
            if (manual is null)
            {
                switch (profile.Press)
                {
                    case PressAction.SwitchMode:
                        dispatcher.BeginInvoke(() => { SwitchMode(profile, app); guard?.Hold(); });
                        return true;
                    case PressAction.PlayPause:
                        dispatcher.BeginInvoke(() => { Inject.Press(new Shortcut(PlayPause)); Report(app, "Play / Pause"); guard?.Hold(); });
                        return true;
                }
            }
            dispatcher.BeginInvoke(() => Report(app, "Mute"));
            return false;
        }

        var action = manual ?? (onSecondMode.Contains(profile) ? profile.SecondTurn : profile.Turn);
        int direction = vk == VolumeUp ? 1 : -1;   // +1 = clockwise
        string source = manual is not null ? "manual mode" : profile.Name;
        if (action.Mode == KnobMode.Volume)
        {
            dispatcher.BeginInvoke(() => Report(app, $"Volume {Arrow(direction)}   ({source})"));
            return false;   // let Windows change the volume as usual
        }

        if (profile.Reverse) direction = -direction;
        int repeat = Acceleration(direction, profile.Accelerate);
        dispatcher.BeginInvoke(() => { Perform(action, direction, profile.Speed, repeat, app, source); guard?.Hold(); });
        return true;
    }

    /// <summary>Turning quickly moves further: up to 3× when clicks arrive in quick succession.</summary>
    int Acceleration(int direction, bool enabled)
    {
        long now = Stopwatch.GetTimestamp();
        double gapMs = (now - lastTurnTicks) * 1000.0 / Stopwatch.Frequency;
        bool sameWay = direction == lastDirection;
        lastTurnTicks = now;
        lastDirection = direction;
        if (!enabled || !sameWay) return 1;
        return gapMs < 25 ? 3 : gapMs < 50 ? 2 : 1;
    }

    void Perform(KnobAction action, int direction, int speed, int repeat, string app, string source)
    {
        int notch = speed switch { 1 => 30, 2 => 60, 4 => 240, 5 => 360, _ => 120 };
        switch (action.Mode)
        {
            case KnobMode.Scroll:
                Inject.Wheel(-direction * notch * repeat, holdVk: Inject.VkFor(action.ScrollWith));   // clockwise scrolls down / forward
                break;
            case KnobMode.ScrollSideways:
                Inject.Wheel(direction * notch * repeat, sideways: true, holdVk: Inject.VkFor(action.ScrollWith));   // clockwise scrolls right
                break;
            case KnobMode.Zoom:
                Inject.Wheel(direction * 120 * repeat, holdVk: Inject.VkFor(action.ZoomWith));   // clockwise zooms in
                break;
            case KnobMode.Keys:
                var shortcut = direction > 0 ? action.Clockwise : action.CounterClockwise;
                for (int i = 0; i < repeat; i++) Inject.Press(shortcut);
                break;
        }
        Report(app, $"{action.Describe()} {Arrow(direction)}   ({source})");
    }

    void SwitchMode(KnobProfile profile, string app)
    {
        bool second = !onSecondMode.Remove(profile);
        if (second) onSecondMode.Add(profile);
        var action = second ? profile.SecondTurn : profile.Turn;
        osd.Flash(app, action.Describe());
        Report(app, "Switched to " + action.Describe());
    }

    void Report(string app, string what)
    {
        LastActivity = (app, what);
        KnobLog.Write($"  -> {app}: {what}");
        Activity?.Invoke(app, what);
    }

    static string Arrow(int direction) => direction > 0 ? "↻" : "↺";

    (KnobProfile Profile, string App) CurrentProfile()
    {
        NativeInput.GetWindowThreadProcessId(NativeInput.GetForegroundWindow(), out uint pid);
        string process = pid == 0 ? "" : NativeInput.ProcessName(pid);
        var match = settings.Profiles.FirstOrDefault(p => !p.IsDefault && string.Equals(p.ProcessName, process, StringComparison.OrdinalIgnoreCase));
        var profile = match ?? settings.Profiles.First(p => p.IsDefault);
        return (profile, match?.Name ?? (process.Length > 0 ? process : "Desktop"));
    }
}
