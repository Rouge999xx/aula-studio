using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AulaStudio.Knob;

/// <summary>A small pill near the bottom of the screen saying which mode the knob just switched to. Never takes focus.</summary>
public sealed class OsdWindow : Window
{
    const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    readonly TextBlock title = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9D, 0xA8)) };
    readonly TextBlock mode = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    readonly DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };

    public OsdWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
        });
        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(mode);
        stack.Children.Add(text);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1D, 0x1F, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 12, 22, 12),
            Margin = new Thickness(10),
            Child = stack,
        };
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); Hide(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }

    public void Flash(string app, string modeName)
    {
        title.Text = app;
        mode.Text = modeName;
        Show();
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Bottom - ActualHeight - 60;
        hideTimer.Stop();
        hideTimer.Start();
    }
}
