using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;

namespace AulaStudio.Input;

/// <summary>One button press from a HID consumer-control collection: a media key, or one click of a volume knob.</summary>
public readonly record struct ConsumerPress(string Device, bool IsAula, ushort Usage);

/// <summary>
/// Reads HID consumer-control reports through Raw Input. The F75's knob and its Fn media keys come from its consumer-control
/// collection (COL02: report ID 2, one 16-bit usage per report, e.g. 0xE9 for a clockwise click). Windows turns those into
/// volume changes itself, and isn't guaranteed to pass them through keyboard hooks, so this gives the knob engine a way to
/// see the knob that doesn't depend on the hook. It only listens: Raw Input can't block anything.
/// </summary>
public sealed class ConsumerInput : IDisposable
{
    const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, GIDC_REMOVAL = 2;
    const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIDI_PREPARSEDDATA = 0x20000005;
    const uint RIM_TYPEHID = 2, RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000;
    const int HidP_Input = 0, HIDP_STATUS_SUCCESS = 0x00110000;
    const ushort ConsumerPage = 0x0C, ConsumerControl = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, byte[]? data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, char[]? data, ref uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, byte[]? data, ref uint size);
    [DllImport("hid.dll")]
    static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort[] usages, ref uint length,
                                     byte[] preparsedData, byte[] report, uint reportLength);

    sealed class Device
    {
        public string Name = "";
        public bool IsAula;
        public byte[]? Preparsed;
        public HashSet<ushort> Down = new();
    }

    readonly HwndSource window;
    readonly Dictionary<IntPtr, Device> devices = new();
    readonly ushort[] usages = new ushort[64];

    /// <summary>Raised on the UI thread for every newly pressed usage (a knob click is a press followed by a release).</summary>
    public event Action<ConsumerPress>? Pressed;

    public bool IsListening { get; }

    public ConsumerInput()
    {
        // A message-only window: it never shows up anywhere, it just receives WM_INPUT.
        window = new HwndSource(new HwndSourceParameters("AULA Studio knob input") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        window.AddHook(WndProc);
        var request = new RAWINPUTDEVICE
        {
            usUsagePage = ConsumerPage,
            usUsage = ConsumerControl,
            dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,   // also while another app is in front
            hwndTarget = window.Handle,
        };
        IsListening = RegisterRawInputDevices(new[] { request }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Never mark these handled: Windows needs DefWindowProc to clean up after WM_INPUT.
        if (message == WM_INPUT) Read(lParam);
        else if (message == WM_INPUT_DEVICE_CHANGE && (int)wParam == GIDC_REMOVAL) devices.Remove(lParam);
        return IntPtr.Zero;
    }

    void Read(IntPtr input)
    {
        uint headerSize = (uint)(8 + 2 * IntPtr.Size);   // RAWINPUTHEADER: type, size, device handle, wParam
        uint size = 0;
        GetRawInputData(input, RID_INPUT, null, ref size, headerSize);
        if (size <= headerSize + 8) return;
        var data = new byte[size];
        if (GetRawInputData(input, RID_INPUT, data, ref size, headerSize) != size) return;
        if (BitConverter.ToUInt32(data, 0) != RIM_TYPEHID) return;

        var device = Describe(IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(data, 8) : BitConverter.ToInt32(data, 8));
        if (device.Preparsed is null) return;
        // RAWHID: the size of one report, how many reports, then the reports back to back.
        int reportSize = BitConverter.ToInt32(data, (int)headerSize);
        int count = BitConverter.ToInt32(data, (int)headerSize + 4);
        for (int i = 0; i < count; i++)
        {
            int offset = (int)headerSize + 8 + i * reportSize;
            if (reportSize <= 0 || offset + reportSize > data.Length) return;
            Parse(device, data.AsSpan(offset, reportSize).ToArray());
        }
    }

    void Parse(Device device, byte[] report)
    {
        uint length = (uint)usages.Length;
        if (HidP_GetUsages(HidP_Input, ConsumerPage, 0, usages, ref length, device.Preparsed!, report, (uint)report.Length) != HIDP_STATUS_SUCCESS)
            return;   // a report without consumer buttons
        var down = new HashSet<ushort>(usages.Take((int)length).Where(u => u != 0));
        foreach (ushort usage in down)
            if (!device.Down.Contains(usage)) Pressed?.Invoke(new ConsumerPress(device.Name, device.IsAula, usage));
        device.Down = down;
    }

    Device Describe(IntPtr handle)
    {
        if (devices.TryGetValue(handle, out var device)) return device;
        device = new Device();
        uint chars = 0;
        GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, (char[]?)null, ref chars);
        var name = new char[chars + 1];
        string path = GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, name, ref chars) > 0 ? new string(name).TrimEnd('\0') : "";
        // Wired F75, or its 2.4 GHz receiver.
        device.IsAula = path.Contains("VID_258A&PID_010C", StringComparison.OrdinalIgnoreCase)
                     || path.Contains("VID_3554&PID_FA09", StringComparison.OrdinalIgnoreCase);
        var id = Regex.Match(path, @"VID_(\w{4})&PID_(\w{4})(?:&MI_\d+)?(?:&(Col\d+))?", RegexOptions.IgnoreCase);
        device.Name = id.Success ? $"{id.Groups[1].Value}:{id.Groups[2].Value} {id.Groups[3].Value}".Trim().ToUpperInvariant() : "unknown device";
        uint bytes = 0;
        GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, (byte[]?)null, ref bytes);
        var preparsed = new byte[bytes];
        if (bytes > 0 && GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, preparsed, ref bytes) == bytes) device.Preparsed = preparsed;
        devices[handle] = device;
        return device;
    }

    public void Dispose() => window.Dispose();
}
