using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AulaStudio.Device;

namespace AulaStudio.Views;

public partial class SettingsView : UserControl
{
    readonly DispatcherTimer logRefresh = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public SettingsView()
    {
        InitializeComponent();
        AboutText.Text =
            $"AULA Studio {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} · settings and backups live in %APPDATA%\\AulaStudio.\n" +
            "The F75's protocol comes from community reverse-engineering of AULA's driver (open-aula by paulcoding810, openaula by not-ayan) " +
            "and was checked against this keyboard's own memory before AULA Studio wrote anything.";
        Services.Keyboard.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        AulaDevice.Log.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) logRefresh.Start(); });
        logRefresh.Tick += (_, _) => { logRefresh.Stop(); LogBox.Text = string.Join(Environment.NewLine, AulaDevice.Log.Snapshot()); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Refresh();
    }

    void Refresh()
    {
        StartupSwitch.IsChecked = Startup.IsEnabled;
        TraySwitch.IsChecked = Services.Settings.CloseToTray;
        AdvancedSwitch.IsChecked = Services.Settings.Advanced;
        var keyboard = Services.Keyboard;
        DeviceText.Text = keyboard.IsConnected ? "AULA F75 connected by USB cable (258A:010C), configuration interface found." : keyboard.Status;
        DevicePath.Text = keyboard.DevicePath ?? "";
        LogBox.Text = string.Join(Environment.NewLine, AulaDevice.Log.Snapshot());
    }

    void Startup_Click(object sender, RoutedEventArgs e) => Startup.IsEnabled = StartupSwitch.IsChecked == true;

    void Tray_Click(object sender, RoutedEventArgs e)
    {
        Services.Settings.CloseToTray = TraySwitch.IsChecked == true;
        Services.Settings.Save();
    }

    void Advanced_Click(object sender, RoutedEventArgs e)
    {
        Services.Settings.Advanced = AdvancedSwitch.IsChecked == true;
        Services.Settings.Save();
    }

    void Rescan_Click(object sender, RoutedEventArgs e) => Task.Run(() => Services.Keyboard.Connect());

    void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LogBox.Text);
        Services.Toast("Log copied.");
    }
}
