using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace AulaStudio.Device;

/// <summary>
/// The F75's configuration channel: a vendor HID collection (usage page FF00, usage 1) that exchanges
/// 520-byte feature reports with report ID 6. Packet: <c>06 cmd sub 00 total idx sizeLo sizeHi</c> + data at offset 8.
/// Reads send the header with the read bit (0x80) set and then fetch the reply with GetFeature.
/// </summary>
public sealed class AulaDevice : IDisposable
{
    public const string WiredId = "vid_258a&pid_010c";
    public const string DongleId = "vid_3554&pid_fa09";
    const int ReportLength = 520;
    const int DataOffset = 8;
    public const int TableSize = 512;

    /// <summary>The only write command this app sends: the key matrix. Lighting, macros, resets and firmware updates are never sent.</summary>
    const byte WriteKeyMatrix = 0x03;

    readonly SafeFileHandle handle;
    readonly object gate = new();

    public string Path { get; }
    public static PacketLog Log { get; } = new();

    AulaDevice(string path, SafeFileHandle handle)
    {
        Path = path;
        this.handle = handle;
    }

    public static AulaDevice? TryOpen()
    {
        foreach (string path in HidNative.EnumerateHidPaths().Where(p => p.Contains(WiredId, StringComparison.OrdinalIgnoreCase)))
        {
            using (var query = HidNative.Open(path, readWrite: false))
            {
                var caps = query is null ? null : HidNative.GetCaps(query);
                if (caps is not { UsagePage: 0xFF00, Usage: 1, FeatureReportByteLength: ReportLength }) continue;
            }
            var handle = HidNative.Open(path, readWrite: true);
            if (handle is not null) return new AulaDevice(path, handle);
        }
        return null;
    }

    /// <summary>True when only the 2.4 GHz receiver is present (it uses a different, unsupported protocol).</summary>
    public static bool OnlyDonglePresent()
    {
        var paths = HidNative.EnumerateHidPaths();
        return paths.Any(p => p.Contains(DongleId, StringComparison.OrdinalIgnoreCase))
            && !paths.Any(p => p.Contains(WiredId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>AULA's own app writes the same memory, so the two must never write at the same time.</summary>
    public static bool VendorAppRunning() => Process.GetProcessesByName("OemDrv").Length > 0;

    public byte[] Read(byte command, byte sub, int size)
    {
        if ((command & 0x80) == 0) throw new ArgumentException("Read commands have the 0x80 bit set.");
        if (size is <= 0 or > TableSize) throw new ArgumentOutOfRangeException(nameof(size));
        lock (gate)
        {
            var request = Header(command, sub, size);
            Thread.Sleep(20);   // the keyboard needs a short pause before every packet
            HidNative.SetFeature(handle, request);
            Thread.Sleep(20);
            var reply = new byte[ReportLength];
            reply[0] = 6;
            HidNative.GetFeature(handle, reply);
            if (reply[1] != command || reply[2] != sub)
                throw new IOException($"Unexpected reply from the keyboard ({reply[1]:X2} {reply[2]:X2} instead of {command:X2} {sub:X2}).");
            var data = reply.AsSpan(DataOffset, size).ToArray();
            Log.Add("read ", request, data);
            return data;
        }
    }

    public void WriteKeyTable(byte layer, byte[] table)
    {
        if (layer > 1) throw new ArgumentOutOfRangeException(nameof(layer));
        if (table.Length != TableSize) throw new ArgumentException("A key table is exactly 512 bytes.");
        lock (gate)
        {
            var packet = Header(WriteKeyMatrix, layer, TableSize);
            table.CopyTo(packet, DataOffset);
            Thread.Sleep(20);
            HidNative.SetFeature(handle, packet);
            Thread.Sleep(30);   // let the keyboard commit to flash before anything else
            Log.Add("WRITE", packet, table);
        }
    }

    static byte[] Header(byte command, byte sub, int size)
    {
        var buffer = new byte[ReportLength];
        buffer[0] = 6;          // report ID
        buffer[1] = command;
        buffer[2] = sub;        // layer for key tables
        buffer[4] = 1;          // total packets
        buffer[5] = 0;          // packet index
        buffer[6] = (byte)(size & 0xFF);
        buffer[7] = (byte)(size >> 8);
        return buffer;
    }

    public void Dispose() => handle.Dispose();
}

/// <summary>Recent packets, shown in Settings for diagnosing problems.</summary>
public sealed class PacketLog
{
    readonly LinkedList<string> lines = new();
    readonly object gate = new();
    public event Action? Changed;

    public void Add(string direction, byte[] header, byte[] data)
    {
        string head = string.Join(' ', header.Take(8).Select(b => b.ToString("X2")));
        string preview = string.Join(' ', data.Take(16).Select(b => b.ToString("X2")));
        lock (gate)
        {
            lines.AddFirst($"{DateTime.Now:HH:mm:ss.fff}  {direction}  [{head}]  {preview}{(data.Length > 16 ? " …" : "")}");
            while (lines.Count > 300) lines.RemoveLast();
        }
        Changed?.Invoke();
    }

    public void Note(string message)
    {
        lock (gate) lines.AddFirst($"{DateTime.Now:HH:mm:ss.fff}  {message}");
        Changed?.Invoke();
    }

    public List<string> Snapshot() { lock (gate) return lines.ToList(); }
}
