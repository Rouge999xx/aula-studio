using System.Runtime.InteropServices;
using AulaStudio.Settings;
using static AulaStudio.Input.NativeInput;

namespace AulaStudio.Input;

/// <summary>Synthesizes scrolls and shortcuts. Every event carries <see cref="KeyboardHook.Marker"/>.</summary>
public static class Inject
{
    /// <summary>Scrolls like a mouse wheel (wherever the pointer is), optionally holding a key such as Alt or Ctrl.</summary>
    public static void Wheel(int delta, bool sideways = false, int holdVk = 0)
    {
        var wheel = Mouse(sideways ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, delta);
        if (holdVk == 0) Send(wheel);
        else Send(Key(holdVk, up: false), wheel, Key(holdVk, up: true));
    }

    public static int VkFor(HoldKey hold) => hold switch { HoldKey.Ctrl => 0xA2, HoldKey.Alt => 0xA4, HoldKey.Shift => 0xA0, _ => 0 };
    public static int VkFor(ZoomModifier zoom) => zoom switch { ZoomModifier.Alt => 0xA4, ZoomModifier.Shift => 0xA0, _ => 0xA2 };

    public static void Press(Shortcut shortcut)
    {
        if (shortcut.Vk == 0) return;
        var mods = new List<int>();
        if (shortcut.Ctrl) mods.Add(0xA2);
        if (shortcut.Shift) mods.Add(0xA0);
        if (shortcut.Alt) mods.Add(0xA4);
        if (shortcut.Win) mods.Add(0x5B);
        var inputs = new List<INPUT>();
        inputs.AddRange(mods.Select(m => Key(m, up: false)));
        inputs.Add(Key(shortcut.Vk, up: false));
        inputs.Add(Key(shortcut.Vk, up: true));
        inputs.AddRange(Enumerable.Reverse(mods).Select(m => Key(m, up: true)));
        Send(inputs.ToArray());
    }

    static INPUT Mouse(uint flags, int delta) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)delta), dwExtraInfo = KeyboardHook.Marker } },
    };

    static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (KeyNames.IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0),
                dwExtraInfo = KeyboardHook.Marker,
            },
        },
    };

    static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
}
