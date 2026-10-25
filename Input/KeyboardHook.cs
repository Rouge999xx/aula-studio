using System.Runtime.InteropServices;
using static AulaStudio.Input.NativeInput;

namespace AulaStudio.Input;

/// <summary>
/// A key as the hook saw it. <see cref="Injected"/> means software produced it rather than a keyboard driver. Windows does
/// that for media keys from consumer-control collections (the F75's knob), so only <see cref="Ours"/>, AULA Studio's own
/// input, may be ignored.
/// </summary>
public readonly record struct KeyEvent(int Vk, bool Down, bool Injected, bool Ours, uint ScanCode, uint Flags);

/// <summary>
/// System-wide low-level keyboard hook. It sees keys exactly as the keyboard sends them (after any remapping
/// stored in the keyboard), which is what the knob engine, key capture and the live key tester need.
/// Handlers run on the UI thread in the order they were added; returning true swallows the key.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    /// <summary>Tags input that AULA Studio injects, so its own scrolls and shortcuts are never re-processed.</summary>
    public static readonly IntPtr Marker = new(0x4B4E4F42);

    readonly LowLevelProc proc;
    readonly List<Func<KeyEvent, bool>> handlers = new();
    IntPtr hook;

    public KeyboardHook()
    {
        proc = Callback;   // keep the delegate alive for as long as the hook exists
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
    }

    public bool IsActive => hook != IntPtr.Zero;

    public void Add(Func<KeyEvent, bool> handler) => handlers.Add(handler);
    /// <summary>For handlers that must see keys before anything can swallow them (key capture).</summary>
    public void AddFirst(Func<KeyEvent, bool> handler) => handlers.Insert(0, handler);
    public void Remove(Func<KeyEvent, bool> handler) => handlers.Remove(handler);

    /// <summary>
    /// Windows silently removes a low-level hook that once took too long to answer, and there's no way to ask whether it
    /// did. Installing it again brings it back. The old one goes first, so no key is ever handled twice.
    /// </summary>
    public void Reinstall()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int message = (int)wParam;
            var e = new KeyEvent((int)info.vkCode, message is WM_KEYDOWN or WM_SYSKEYDOWN, (info.flags & LLKHF_INJECTED) != 0,
                                 info.dwExtraInfo == Marker, info.scanCode, info.flags);
            foreach (var handler in handlers.ToArray())
            {
                try
                {
                    if (handler(e)) return 1;
                }
                catch (Exception ex) { App.Log(ex); }
            }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }
}
