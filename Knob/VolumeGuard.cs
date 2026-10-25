using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AulaStudio.Knob;

/// <summary>
/// Puts the volume back when Windows changes it for a knob click that AULA Studio already turned into something else.
/// Normally the keyboard hook stops the click before Windows acts on it and this has nothing to do; it's the fallback for
/// when Windows reacts to the knob's HID report directly. Windows still flashes its volume pop-up then, but the level ends
/// up where it was.
/// </summary>
public sealed class VolumeGuard : IDisposable
{
    // Windows reacts to a click within milliseconds, but KnobEngine may handle a click up to its pairing window (250 ms)
    // later, so look back a bit further than that for the change it caused.
    static readonly long LookBack = Stopwatch.Frequency * 400 / 1000;
    static readonly long HoldFor = Stopwatch.Frequency * 400 / 1000;

    Guid ownChange = Guid.NewGuid();   // tags AULA Studio's own volume changes in the notifications
    readonly Dispatcher dispatcher;
    readonly Callback callback;
    IMMDeviceEnumerator? enumerator;
    IAudioEndpointVolume? volume;
    string deviceId = "";
    float level;
    bool muted;
    // Changes made by someone else, with the values from just before each one.
    readonly List<(long Ticks, float Level, bool Muted)> before = new();
    long holdUntil;
    float heldLevel;
    bool heldMuted;

    public VolumeGuard(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        callback = new Callback(this);
        Connect();
    }

    /// <summary>
    /// A knob click was just used for something other than volume: undo whatever Windows did or does with it.
    /// Call on the UI thread, outside the keyboard hook.
    /// </summary>
    public void Hold()
    {
        long now = Stopwatch.GetTimestamp();
        try
        {
            if (!Connect()) return;
            if (now > holdUntil)
            {
                // Start from the level before any outside change this click may already have caused.
                var first = before.FirstOrDefault(b => now - b.Ticks <= LookBack);
                (heldLevel, heldMuted) = first.Ticks != 0 ? (first.Level, first.Muted) : (level, muted);
            }
            holdUntil = now + HoldFor;
            Restore();
        }
        catch (Exception ex)   // never let the fallback get in the way of the knob itself
        {
            KnobLog.Write("volume guard failed: " + ex.Message);
        }
    }

    /// <summary>The default playback device's current level (0–1), for the self-test.</summary>
    public float Level => level;

    void Restore()
    {
        if (volume is null) return;
        try
        {
            if (Math.Abs(level - heldLevel) > 0.001f)
            {
                volume.SetMasterVolumeLevelScalar(heldLevel, ref ownChange);
                KnobLog.Write($"volume put back from {level * 100:0} to {heldLevel * 100:0}");
                level = heldLevel;
            }
            if (muted != heldMuted)
            {
                volume.SetMute(heldMuted, ref ownChange);
                KnobLog.Write($"mute put back to {(heldMuted ? "on" : "off")}");
                muted = heldMuted;
            }
        }
        catch (COMException ex) { KnobLog.Write("couldn't set the volume: " + ex.Message); }
    }

    // On the UI thread, for every volume or mute change on the default playback device.
    void Changed(long ticks, float newLevel, bool newMuted, Guid context)
    {
        if (context == ownChange) return;
        before.RemoveAll(b => ticks - b.Ticks > LookBack * 4);
        before.Add((ticks, level, muted));
        level = newLevel;
        muted = newMuted;
        if (ticks <= holdUntil) Restore();
    }

    /// <summary>Follows the default playback device, so the guard keeps working after switching to headphones.</summary>
    bool Connect()
    {
        try
        {
            enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0 /* render */, 0 /* console */, out var device);
            device.GetId(out string id);
            if (volume is not null && id == deviceId) return true;
            Disconnect();
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object activated);
            var endpoint = (IAudioEndpointVolume)activated;
            endpoint.GetMasterVolumeLevelScalar(out level);
            endpoint.GetMute(out muted);
            endpoint.RegisterControlChangeNotify(callback);
            volume = endpoint;
            deviceId = id;
            before.Clear();
            return true;
        }
        catch (COMException ex)
        {
            // No playback device (all unplugged or disabled): nothing to guard.
            KnobLog.Write("no playback device to guard: " + ex.Message);
            Disconnect();
            return false;
        }
    }

    void Disconnect()
    {
        if (volume is null) return;
        try { volume.UnregisterControlChangeNotify(callback); } catch (COMException) { }
        Marshal.ReleaseComObject(volume);
        volume = null;
        deviceId = "";
    }

    public void Dispose() => Disconnect();

    // Windows calls this on one of its own threads.
    sealed class Callback : IAudioEndpointVolumeCallback
    {
        readonly VolumeGuard guard;
        public Callback(VolumeGuard guard) => this.guard = guard;

        public int OnNotify(IntPtr data)
        {
            long ticks = Stopwatch.GetTimestamp();
            // AUDIO_VOLUME_NOTIFICATION_DATA: event context GUID, BOOL muted, float master volume, ...
            var context = Marshal.PtrToStructure<Guid>(data);
            bool muted = Marshal.ReadInt32(data, 16) != 0;
            float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
            guard.dispatcher.BeginInvoke(() => guard.Changed(ticks, level, muted, context));
            return 0;
        }
    }

    // Core Audio (mmdeviceapi.h, endpointvolume.h). Only the methods up to the last one used are declared, in vtable order.
    // Public, because Windows has to reach the callback interface through COM.
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        void Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        void OpenPropertyStore(int access, out IntPtr properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }
}
