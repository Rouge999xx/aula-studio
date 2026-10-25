using AulaStudio.Backup;
using AulaStudio.Protocol;

namespace AulaStudio.Device;

public sealed record OperationResult(bool Ok, string Message)
{
    public static OperationResult Success(string message) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

/// <summary>
/// Everything that touches the keyboard goes through here. Writes follow one fixed routine:
/// fresh read → refuse unsafe changes → backup → write → read back → roll back automatically on any mismatch.
/// </summary>
public sealed class KeyboardService : IDisposable
{
    const byte ReadKeyMatrix = 0x83, ReadLedBlock = 0x84, ReadColorTable = 0x8A;

    readonly object operation = new();
    AulaDevice? device;

    public KeyMatrix? Base { get; private set; }
    public KeyMatrix? FnLayer { get; private set; }
    public string Status { get; private set; } = "Not connected";
    public bool IsConnected => device is not null && Base is not null;
    public string? DevicePath => device?.Path;
    public event Action? StateChanged;

    /// <summary>(Re)connects and reads the keyboard. Takes a backup whenever the keyboard differs from the latest one.</summary>
    public bool Connect()
    {
        lock (operation)
        {
            device?.Dispose();
            device = null;
            Base = FnLayer = null;
            try
            {
                device = AulaDevice.TryOpen();
                if (device is null)
                {
                    Status = AulaDevice.OnlyDonglePresent()
                        ? "Connected by the 2.4 GHz receiver. Plug in the USB cable to configure the keyboard."
                        : "Keyboard not found. Plug the F75 in with its USB cable.";
                    return false;
                }
                var state = ReadState();
                if (!state.Base.HasTrailer || !state.Fn.HasTrailer)
                {
                    Status = "The keyboard sent data AULA Studio doesn't recognise, so it won't write anything.";
                    device.Dispose();
                    device = null;
                    return false;
                }
                Base = state.Base;
                FnLayer = state.Fn;
                if (!BackupStore.MatchesLatest(state))
                    BackupStore.Save(state, "Automatic backup when AULA Studio connected", BackupStore.List().Count == 0 ? "initial" : "connect");
                Status = "Connected";
                AulaDevice.Log.Note("connected: " + device.Path);
                return true;
            }
            catch (IOException ex)
            {
                device?.Dispose();
                device = null;
                Status = "Couldn't talk to the keyboard: " + ex.Message;
                return false;
            }
            finally { StateChanged?.Invoke(); }
        }
    }

    KeyboardState ReadState()
    {
        var d = device ?? throw new InvalidOperationException("Not connected.");
        var baseLayer = new KeyMatrix(d.Read(ReadKeyMatrix, 0, KeyMatrix.Size));
        var fnLayer = new KeyMatrix(d.Read(ReadKeyMatrix, 1, KeyMatrix.Size));
        var led = d.Read(ReadLedBlock, 0, 128);
        var colors = d.Read(ReadColorTable, 0, 512);
        return new KeyboardState(baseLayer, fnLayer, led, colors);
    }

    /// <summary>
    /// Writes the user's edits. Only slots that differ between the loaded and edited tables are applied,
    /// on top of a fresh read, so anything changed on the keyboard meanwhile is preserved.
    /// </summary>
    public OperationResult SaveChanges((KeyMatrix Loaded, KeyMatrix Edited) baseLayer, (KeyMatrix Loaded, KeyMatrix Edited) fnLayer, bool advanced)
    {
        lock (operation)
        {
            var result = Guard();
            if (result is not null) return result;
            try
            {
                var fresh = ReadState();
                var targets = new KeyMatrix?[2];
                var layers = new[] { baseLayer, fnLayer };
                int changedSlots = 0;
                for (int layer = 0; layer < 2; layer++)
                {
                    var changes = layers[layer].Edited.DifferencesFrom(layers[layer].Loaded).ToList();
                    if (changes.Count == 0) continue;
                    if (changes.Contains(KeyMatrix.TrailerSlot))
                        return OperationResult.Fail("That change would damage the key table, so nothing was written.");
                    if (!advanced && changes.Any(KeyMatrix.IsProtected))
                        return OperationResult.Fail("The Fn key and the knob are locked. Turn on Advanced in Settings to change them.");
                    var target = (layer == 0 ? fresh.Base : fresh.Fn).Clone();
                    foreach (int slot in changes) target[slot] = layers[layer].Edited[slot];
                    targets[layer] = target;
                    changedSlots += changes.Count;
                }
                if (changedSlots == 0) return OperationResult.Success("Nothing to save.");
                if (targets[0] is { } newBase && newBase[KeyMatrix.FnSlot] != KeyEntry.FnKey && !advanced)
                    return OperationResult.Fail("The Fn key must stay on its key, so nothing was written.");

                var backup = BackupStore.Save(fresh, "Automatic backup before saving key changes", "before-save");
                var outcome = WriteAndVerify(fresh, targets);
                return outcome.Ok
                    ? OperationResult.Success($"Saved {changedSlots} key change{(changedSlots == 1 ? "" : "s")} to the keyboard. Backup: {Path.GetFileName(backup.FilePath)}")
                    : outcome;
            }
            catch (IOException ex) { return OperationResult.Fail("Couldn't talk to the keyboard: " + ex.Message); }
            finally { StateChanged?.Invoke(); }
        }
    }

    public OperationResult Restore(KeyboardBackup backup)
    {
        lock (operation)
        {
            var result = Guard();
            if (result is not null) return result;
            if (!backup.IsValid(out string problem)) return OperationResult.Fail($"That backup can't be used: {problem}.");
            try
            {
                var fresh = ReadState();
                BackupStore.Save(fresh, "Automatic backup before restoring " + Path.GetFileName(backup.FilePath), "before-restore");
                var outcome = WriteAndVerify(fresh, new KeyMatrix?[] { backup.BaseLayer, backup.FnLayer });
                return outcome.Ok ? OperationResult.Success($"Restored the keyboard from the backup of {backup.Created:MMM d, h:mm tt}.") : outcome;
            }
            catch (IOException ex) { return OperationResult.Fail("Couldn't talk to the keyboard: " + ex.Message); }
            finally { StateChanged?.Invoke(); }
        }
    }

    public OperationResult BackupNow()
    {
        lock (operation)
        {
            if (device is null) return OperationResult.Fail("The keyboard isn't connected.");
            try
            {
                var backup = BackupStore.Save(ReadState(), "Backup made from the Backups page", "manual");
                return OperationResult.Success("Backed up: " + Path.GetFileName(backup.FilePath));
            }
            catch (IOException ex) { return OperationResult.Fail("Couldn't read the keyboard: " + ex.Message); }
        }
    }

    /// <summary>Puts Fn back on its key (slot 53), the documented fix when a bad write disables Fn.</summary>
    public OperationResult RepairFnKey() => RepairSlot(KeyMatrix.FnSlot, KeyEntry.FnKey, "Fn");

    /// <summary>Puts Left Win back on its key (slot 11).</summary>
    public OperationResult RepairWinKey() => RepairSlot(11, KeyEntry.Keyboard(0, 0x08), "Win");

    OperationResult RepairSlot(int slot, KeyEntry entry, string name)
    {
        lock (operation)
        {
            var result = Guard();
            if (result is not null) return result;
            try
            {
                var fresh = ReadState();
                if (fresh.Base[slot] == entry) return OperationResult.Success($"The {name} key is already fine.");
                var target = fresh.Base.Clone();
                target[slot] = entry;
                BackupStore.Save(fresh, $"Automatic backup before repairing the {name} key", "before-repair");
                var outcome = WriteAndVerify(fresh, new KeyMatrix?[] { target, null });
                return outcome.Ok ? OperationResult.Success($"The {name} key is repaired.") : outcome;
            }
            catch (IOException ex) { return OperationResult.Fail("Couldn't talk to the keyboard: " + ex.Message); }
            finally { StateChanged?.Invoke(); }
        }
    }

    OperationResult? Guard()
    {
        if (device is null || Base is null) return OperationResult.Fail("The keyboard isn't connected.");
        if (AulaDevice.VendorAppRunning()) return OperationResult.Fail("AULA's own app is open. Close it first; both apps write the same memory.");
        return null;
    }

    /// <summary>
    /// Writes the given layers, then reads both back. If anything differs, the previous contents are written back.
    /// The base layer is always written last, so normal typing ends up correct whatever happens to the Fn layer.
    /// </summary>
    OperationResult WriteAndVerify(KeyboardState before, KeyMatrix?[] targets)
    {
        var d = device!;
        var expected = new[] { targets[0] ?? before.Base, targets[1] ?? before.Fn };
        for (int layer = 1; layer >= 0; layer--)
            if (targets[layer] is { } table) d.WriteKeyTable((byte)layer, table.ToArray());

        var actual = new[] { new KeyMatrix(d.Read(ReadKeyMatrix, 0, KeyMatrix.Size)), new KeyMatrix(d.Read(ReadKeyMatrix, 1, KeyMatrix.Size)) };
        if (actual[0].SameAs(expected[0]) && actual[1].SameAs(expected[1]))
        {
            Base = actual[0];
            FnLayer = actual[1];
            return OperationResult.Success("Verified.");
        }

        AulaDevice.Log.Note("read-back mismatch, rolling back");
        d.WriteKeyTable(1, before.Fn.ToArray());
        d.WriteKeyTable(0, before.Base.ToArray());
        var check = new[] { new KeyMatrix(d.Read(ReadKeyMatrix, 0, KeyMatrix.Size)), new KeyMatrix(d.Read(ReadKeyMatrix, 1, KeyMatrix.Size)) };
        Base = check[0];
        FnLayer = check[1];
        bool rolledBack = check[0].SameAs(before.Base) && check[1].SameAs(before.Fn);
        return OperationResult.Fail(rolledBack
            ? "The keyboard didn't store the change correctly, so AULA Studio put everything back as it was."
            : "The keyboard didn't store the change correctly and the automatic rollback didn't fully apply. Use Backups › Restore.");
    }

    public void Dispose()
    {
        lock (operation)
        {
            device?.Dispose();
            device = null;
        }
    }
}
