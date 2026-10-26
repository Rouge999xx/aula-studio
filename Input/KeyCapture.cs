using System.Windows.Threading;
using AulaStudio.Settings;

namespace AulaStudio.Input;

/// <summary>
/// "Press a key" for editors: grabs the next key or shortcut, before any app (or an App keys rule) sees it.
/// While it's waiting, keys are swallowed, and so are the releases of the keys it grabbed.
/// </summary>
public sealed class KeyCapture
{
    readonly KeyboardHook hook;
    readonly Dispatcher dispatcher;
    readonly DispatcherTimer timeout = new() { Interval = TimeSpan.FromSeconds(8) };
    readonly HashSet<int> swallowRelease = new();
    readonly HashSet<int> modifiers = new();
    Action<Shortcut>? done;
    Action? cancelled;
    bool wantShortcut;
    bool attached;

    public KeyCapture(KeyboardHook hook, Dispatcher dispatcher)
    {
        this.hook = hook;
        this.dispatcher = dispatcher;
        timeout.Tick += (_, _) => Cancel();
    }

    public bool IsActive => done is not null;

    /// <param name="shortcut">
    /// True: modifiers combine with the next key (Ctrl+Shift+Z); a modifier pressed and released on its own counts as the key.
    /// False: the very first key, modifiers included, as a single key.
    /// </param>
    public void Start(bool shortcut, Action<Shortcut> onDone, Action onCancelled)
    {
        Cancel();
        Attach();
        wantShortcut = shortcut;
        done = onDone;
        cancelled = onCancelled;
        modifiers.Clear();
        timeout.Start();
    }

    public void Cancel()
    {
        if (done is null) return;
        var callback = cancelled;
        Finish();
        callback?.Invoke();
    }

    /// <summary>Hooks in ahead of everything else. Call while the editor is on screen.</summary>
    public void Attach()
    {
        if (attached) return;
        hook.AddFirst(OnKey);
        attached = true;
    }

    public void Detach()
    {
        Cancel();
        hook.Remove(OnKey);
        attached = false;
        swallowRelease.Clear();
    }

    bool OnKey(KeyEvent e)
    {
        if (e.Ours) return false;
        if (!e.Down)
        {
            if (done is not null && wantShortcut && modifiers.Contains(e.Vk))
                Complete(new Shortcut(e.Vk));   // a modifier on its own, e.g. Right Ctrl
            return swallowRelease.Remove(e.Vk);
        }
        if (done is null) return false;
        swallowRelease.Add(e.Vk);
        if (wantShortcut && IsModifier(e.Vk))
        {
            modifiers.Add(e.Vk);
            return true;
        }
        var result = new Shortcut(e.Vk);
        if (wantShortcut)
        {
            result.Ctrl = modifiers.Overlaps(new[] { 0xA2, 0xA3 });
            result.Shift = modifiers.Overlaps(new[] { 0xA0, 0xA1 });
            result.Alt = modifiers.Overlaps(new[] { 0xA4, 0xA5 });
            result.Win = modifiers.Overlaps(new[] { 0x5B, 0x5C });
        }
        Complete(result);
        return true;
    }

    public static bool IsModifier(int vk) => vk is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    void Complete(Shortcut result)
    {
        var callback = done;
        Finish();
        // Not inside the hook: the editor rebuilds itself, and the hook has to answer Windows quickly.
        if (callback is not null) dispatcher.BeginInvoke(() => callback(result));
    }

    void Finish()
    {
        done = null;
        cancelled = null;
        timeout.Stop();
        modifiers.Clear();
    }
}
