using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AulaStudio.Views;

namespace AulaStudio;

public partial class MainWindow : Window
{
    const int WM_DEVICECHANGE = 0x0219;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    readonly Dictionary<string, UserControl> views = new();
    readonly DispatcherTimer toastTimer = new();
    readonly DispatcherTimer rescanTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };
    bool exiting;

    public MainWindow()
    {
        InitializeComponent();
        Services.ToastHandler = ShowToast;
        Services.Keyboard.StateChanged += () => Dispatcher.BeginInvoke(UpdateStatus);
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); ToastBox.Visibility = Visibility.Collapsed; };
        rescanTimer.Tick += (_, _) => { rescanTimer.Stop(); Rescan(); };
        SourceInitialized += (_, _) => OnSourceReady();
        Closing += OnClosing;
        ShowView("keyboard");
        UpdateStatus();
    }

    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void PrepareExit() => exiting = true;

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        if (Services.Settings.CloseToTray)
        {
            e.Cancel = true;   // keep running in the tray so the knob keeps working
            Hide();
            return;
        }
        exiting = true;
        Dispatcher.BeginInvoke(() => ((App)Application.Current).ExitApp());
    }

    void OnSourceReady()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));          // dark title bar
        int caption = 0x001C1716;                                         // #16171C, to blend with the window
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    // Any USB plug/unplug: check the keyboard again once things settle.
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE) { rescanTimer.Stop(); rescanTimer.Start(); }
        return IntPtr.Zero;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Body is not null) ShowView((string)((FrameworkElement)sender).Tag);
    }

    void ShowView(string name)
    {
        if (!views.TryGetValue(name, out var view))
        {
            view = name switch
            {
                "keyboard" => new KeyboardView(),
                "knob" => new KnobView(),
                "backups" => new BackupsView(),
                _ => new SettingsView(),
            };
            views[name] = view;
        }
        Body.Content = view;
    }

    void Rescan_Click(object sender, RoutedEventArgs e) => Rescan();

    void Rescan()
    {
        StatusTitle.Text = "Looking for the keyboard…";
        Task.Run(() => Services.Keyboard.Connect());
    }

    void UpdateStatus()
    {
        var keyboard = Services.Keyboard;
        bool connected = keyboard.IsConnected;
        StatusDot.Fill = (Brush)FindResource(connected ? "Good" : "Warn");
        StatusTitle.Text = connected ? "F75 connected" : "Not connected";
        StatusDetail.Text = connected ? "USB cable · ready to configure" : keyboard.Status;
        RescanButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
    }

    void ShowToast(string message, bool isError)
    {
        ToastText.Text = message;
        ToastBox.Background = isError ? (Brush)FindResource("Warn") : new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5));
        ToastBox.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Interval = TimeSpan.FromSeconds(isError ? 7 : 4);
        toastTimer.Start();
    }
}
