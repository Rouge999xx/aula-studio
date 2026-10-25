using System.Windows;
using AulaStudio.Tray;

namespace AulaStudio;

public partial class App : Application
{
    Mutex? mutex;
    TrayIcon? tray;
    MainWindow? window;
    bool windowClosed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ev) => { Log(ev.Exception); ev.Handled = true; };

        bool selftest = e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase);
        if (selftest || e.Args.Contains("--dump", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.Run(writeTests: selftest));
            return;
        }
        if (e.Args.Contains("--knobtest", StringComparer.OrdinalIgnoreCase))
        {
            Knob.KnobSelfTest.Run(this);   // shuts the app down when it's done
            return;
        }

        // --tray: start hidden (used by Start with Windows). --exit: ask a running copy to quit cleanly.
        bool startHidden = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        bool exitRequest = e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        mutex = new Mutex(true, @"Local\AulaStudio", out bool firstInstance);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AulaStudio.Show");
        var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AulaStudio.Exit");
        if (!firstInstance)
        {
            if (exitRequest) exitSignal.Set();
            else if (!startHidden) showSignal.Set();
            Shutdown();
            return;
        }
        if (exitRequest) { Shutdown(); return; }

        Services.Init(Dispatcher);
        window = new MainWindow();
        window.Closed += (_, _) => windowClosed = true;
        MainWindow = window;
        tray = new TrayIcon(window);
        if (!startHidden) window.Show();

        new Thread(() => { while (showSignal.WaitOne()) Dispatcher.BeginInvoke(() => window.BringToFront()); }) { IsBackground = true }.Start();
        new Thread(() => { if (exitSignal.WaitOne()) Dispatcher.BeginInvoke(ExitApp); }) { IsBackground = true }.Start();
        Task.Run(() => Services.Keyboard.Connect());
    }

    public void ExitApp()
    {
        window?.PrepareExit();
        if (!windowClosed) window?.Close();
        tray?.Dispose();
        tray = null;
        Services.Dispose();
        Shutdown();
    }

    public static void Log(Exception ex)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AulaStudio");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "error.log"), $"{DateTime.Now:s}  {ex}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}
