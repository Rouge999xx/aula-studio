using System.Windows.Threading;
using AulaStudio.Device;
using AulaStudio.Input;
using AulaStudio.Knob;
using AulaStudio.Settings;
using Microsoft.Win32;

namespace AulaStudio;

/// <summary>The app's long-lived pieces, created once on the UI thread at startup.</summary>
public static class Services
{
    public static AppSettings Settings { get; private set; } = null!;
    public static KeyboardService Keyboard { get; private set; } = null!;
    public static KeyboardHook Hook { get; private set; } = null!;
    public static ConsumerInput Consumer { get; private set; } = null!;
    public static KnobEngine Knob { get; private set; } = null!;

    /// <summary>Set by the main window; shows a short message at the bottom of it.</summary>
    public static Action<string, bool>? ToastHandler { get; set; }
    public static void Toast(string message, bool isError = false) => ToastHandler?.Invoke(message, isError);

    public static void Init(Dispatcher dispatcher)
    {
        Settings = AppSettings.Load();
        Keyboard = new KeyboardService();
        Hook = new KeyboardHook();
        Consumer = new ConsumerInput();
        Knob = new KnobEngine(Hook, Consumer, Settings, dispatcher);
    }

    public static void Dispose()
    {
        Hook?.Dispose();
        Consumer?.Dispose();
        Keyboard?.Dispose();
    }
}

/// <summary>"Start with Windows": a per-user Run entry that starts AULA Studio in the tray.</summary>
public static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AulaStudio";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(ValueName, false);
        }
    }
}
