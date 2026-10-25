using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AulaStudio.Device;

/// <summary>Win32 HID plumbing: enumerate HID interfaces, open them, read capabilities, send feature reports.</summary>
internal static class HidNative
{
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    const uint ShareReadWrite = 0x3, OpenExisting = 3;
    const int DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct HidpCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct InterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct InterfaceDetail { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string DevicePath; }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, ref InterfaceDetail detail, int size, IntPtr required, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    public static List<string> EnumerateHidPaths()
    {
        var paths = new List<string>();
        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        try
        {
            var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                var detail = new InterfaceDetail { Size = IntPtr.Size == 8 ? 8 : 6 };
                if (SetupDiGetDeviceInterfaceDetail(set, ref data, ref detail, Marshal.SizeOf<InterfaceDetail>(), IntPtr.Zero, IntPtr.Zero))
                    paths.Add(detail.DevicePath);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    /// <summary>Opens a device. Without readWrite the handle can only query capabilities (nothing is sent).</summary>
    public static SafeFileHandle? Open(string path, bool readWrite)
    {
        var handle = CreateFile(path, readWrite ? GenericRead | GenericWrite : 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        return null;
    }

    public static HidpCaps? GetCaps(SafeFileHandle device)
    {
        if (!HidD_GetPreparsedData(device, out IntPtr data)) return null;
        try { return HidP_GetCaps(data, out var caps) == 0x110000 ? caps : null; }   // HIDP_STATUS_SUCCESS
        finally { HidD_FreePreparsedData(data); }
    }

    public static void SetFeature(SafeFileHandle device, byte[] buffer)
    {
        if (!HidD_SetFeature(device, buffer, buffer.Length))
            throw new IOException($"Sending to the keyboard failed (Windows error {Marshal.GetLastWin32Error()}).");
    }

    public static void GetFeature(SafeFileHandle device, byte[] buffer)
    {
        if (!HidD_GetFeature(device, buffer, buffer.Length))
            throw new IOException($"Reading from the keyboard failed (Windows error {Marshal.GetLastWin32Error()}).");
    }
}
