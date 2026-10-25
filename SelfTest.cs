using AulaStudio.Backup;
using AulaStudio.Device;
using AulaStudio.Protocol;

namespace AulaStudio;

/// <summary>
/// Command-line checks, run with <c>AulaStudio.exe --dump</c> (read-only) or <c>--selftest</c>.
/// The self-test proves the safety net before the app is trusted with real edits:
/// identical rewrites change nothing, a real remap sticks, and restoring a backup brings back every byte.
/// </summary>
public static class SelfTest
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AulaStudio", "selftest.log");

    public static int Run(bool writeTests)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        using var log = new StreamWriter(LogPath, append: false);
        void Say(string line) { log.WriteLine(line); log.Flush(); }

        using var keyboard = new KeyboardService();
        if (!keyboard.Connect()) { Say("FAIL connect: " + keyboard.Status); return 2; }
        Say("Connected: " + keyboard.DevicePath);
        var original = (Base: keyboard.Base!.Clone(), Fn: keyboard.FnLayer!.Clone());
        Say($"Base: Esc={original.Base[0].Describe()}  RCtrl={original.Base[59].Describe()}  Fn={original.Base[53].Describe()}  knob={original.Base[84].Describe()}");
        Say($"Fn layer: F3={original.Fn[24].Describe()}  F11={original.Fn[72].Describe()}  F12={original.Fn[78].Describe()}  Win={original.Fn[11].Describe()}");
        if (!writeTests) return 0;

        var backup = BackupStore.Save(new KeyboardState(original.Base, original.Fn, null, null), "Self-test reference", "selftest");
        Say("Reference backup: " + backup.FilePath);
        bool allPassed = true;
        void Check(string step, OperationResult result, bool expectation)
        {
            bool pass = result.Ok && expectation;
            allPassed &= pass;
            Say($"{(pass ? "PASS" : "FAIL")}  {step}: {result.Message}");
        }

        // 1. Rewriting identical content (a restore of what's already there) must leave both layers byte-identical.
        var unchanged = keyboard.Restore(backup);
        Check("rewrite both layers unchanged", unchanged, keyboard.Base!.SameAs(original.Base) && keyboard.FnLayer!.SameAs(original.Fn));
        if (!allPassed) { Say("Stopping before any real change."); return 1; }

        // 2. A real change on a harmless key: Right Ctrl becomes F13, and nothing else moves.
        var edited = original.Base.Clone();
        edited[59] = KeyEntry.Keyboard(0x68);
        var remap = keyboard.SaveChanges((original.Base, edited), (original.Fn, original.Fn), advanced: false);
        bool onlyThatSlot = keyboard.Base!.DifferencesFrom(original.Base).SequenceEqual(new[] { 59 }) && keyboard.FnLayer!.SameAs(original.Fn);
        Check($"remap Right Ctrl to F13 (now {keyboard.Base[59].Describe()})", remap, onlyThatSlot && keyboard.Base[59] == KeyEntry.Keyboard(0x68));

        // 3. Restoring the reference backup must bring back every byte of both layers.
        var restore = keyboard.Restore(backup);
        Check("restore from backup", restore, keyboard.Base!.SameAs(original.Base) && keyboard.FnLayer!.SameAs(original.Fn));

        // 4. Guards: the Fn key and the trailer refuse to change without Advanced.
        var badFn = original.Base.Clone();
        badFn[KeyMatrix.FnSlot] = KeyEntry.Keyboard(0x04);
        var guarded = keyboard.SaveChanges((original.Base, badFn), (original.Fn, original.Fn), advanced: false);
        allPassed &= !guarded.Ok && keyboard.Base!.SameAs(original.Base);
        Say($"{(!guarded.Ok ? "PASS" : "FAIL")}  Fn key is protected: {guarded.Message}");

        // Final state must equal the original.
        keyboard.Connect();
        bool same = keyboard.Base!.SameAs(original.Base) && keyboard.FnLayer!.SameAs(original.Fn);
        allPassed &= same;
        Say($"{(same ? "PASS" : "FAIL")}  keyboard ends exactly as it started");
        Say(allPassed ? "ALL PASSED" : "SOME CHECKS FAILED");
        return allPassed ? 0 : 1;
    }
}
