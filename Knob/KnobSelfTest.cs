using System.Runtime.InteropServices;
using static AulaStudio.Input.NativeInput;

namespace AulaStudio.Knob;

/// <summary>
/// "--knobtest": checks the volume guard against Windows itself. It sends a volume key the way Windows does for the knob
/// and checks that the guard puts the level back, then sends one more without the guard (proof that Windows reacted) and
/// undoes it. The volume ends where it started. Results go to knob.log; the exit code is 0 when everything passed.
/// </summary>
public static class KnobSelfTest
{
    const int VolumeUp = 0xAF, VolumeDown = 0xAE;

    public static async void Run(App app)
    {
        int exitCode = 1;
        try
        {
            var guard = new VolumeGuard(app.Dispatcher);
            await Task.Delay(300);
            float start = guard.Level;
            int key = start > 0.95f ? VolumeDown : VolumeUp;   // step away from the end of the range
            KnobLog.Write($"knob test: volume is {start * 100:0}");

            guard.Hold();
            Tap(key);
            await Task.Delay(700);
            float guarded = guard.Level;
            bool undone = Math.Abs(guarded - start) < 0.005f;
            KnobLog.Write($"knob test: with the guard, Windows' change was {(undone ? "undone" : "NOT undone")} (volume {guarded * 100:0})");

            Tap(key);   // the hold has run out: this change should stay
            await Task.Delay(500);
            float unguarded = guard.Level;
            bool reacted = Math.Abs(unguarded - start) > 0.005f;
            KnobLog.Write($"knob test: without the guard, Windows {(reacted ? "changed" : "did NOT change")} the volume (volume {unguarded * 100:0})");
            if (reacted)
            {
                Tap(key == VolumeUp ? VolumeDown : VolumeUp);
                await Task.Delay(500);
            }
            KnobLog.Write($"knob test: volume back at {guard.Level * 100:0}; {(undone && reacted ? "PASSED" : "FAILED")}");
            exitCode = undone && reacted ? 0 : 1;
            guard.Dispose();
        }
        catch (Exception ex)
        {
            KnobLog.Write("knob test crashed: " + ex);
        }
        await Task.Delay(300);   // let the log thread finish writing
        app.Shutdown(exitCode);
    }

    /// <summary>A volume key without AULA Studio's marker, like the ones Windows makes from the knob's HID reports.</summary>
    static void Tap(int vk)
    {
        var down = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk } } };
        var up = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = KEYEVENTF_KEYUP } } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }
}
