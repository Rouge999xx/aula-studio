using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AulaStudio.Input;
using AulaStudio.Protocol;
using AulaStudio.Settings;

namespace AulaStudio.Views;

public partial class KnobView : UserControl
{
    static readonly (KnobMode Mode, string Label, string Glyph)[] Modes =
    {
        (KnobMode.Volume, "Volume", ""),
        (KnobMode.Scroll, "Scroll", ""),
        (KnobMode.ScrollSideways, "Scroll sideways", ""),
        (KnobMode.Zoom, "Zoom", ""),
        (KnobMode.Keys, "Keys", ""),
    };

    AppSettings Settings => Services.Settings;
    KnobProfile? current;

    public KnobView()
    {
        InitializeComponent();
        EnabledSwitch.IsChecked = Settings.KnobEnabled;
        // Only while this page is on screen; when it comes back it shows the latest knob event it missed.
        Loaded += (_, _) =>
        {
            Services.Knob.Activity -= ShowActivity;
            Services.Knob.Activity += ShowActivity;
            Services.Knob.ModeChanged -= RefreshBanner;
            Services.Knob.ModeChanged += RefreshBanner;
            if (Services.Knob.LastActivity is { } last) ShowActivity(last.App, last.What);
        };
        Unloaded += (_, _) =>
        {
            Services.Knob.Activity -= ShowActivity;
            Services.Knob.ModeChanged -= RefreshBanner;
        };
        RefreshList(Settings.Profiles[0]);
        RefreshBanner();
        IsVisibleChanged += (_, _) => { if (IsVisible) { EnabledSwitch.IsChecked = Settings.KnobEnabled; RefreshBanner(); } };
    }

    void ShowActivity(string app, string what)
    {
        ActivityText.Text = $"{app}:  {what}";
        ActivityText.Foreground = (System.Windows.Media.Brush)FindResource("Text");
    }

    void RefreshBanner()
    {
        var manual = Settings.ManualMode;
        ManualBanner.Visibility = manual is null ? Visibility.Collapsed : Visibility.Visible;
        if (manual is not null)
            ManualText.Text = $"Manual mode from the tray menu: the knob does “{manual.Describe()}” in every app. The per-app settings below are paused until you switch back.";
    }

    void BackToAutomatic_Click(object sender, RoutedEventArgs e) => Services.Knob.SetManualMode(null);

    void Save() => Settings.Save();

    // ------------------------------------------------------------------ profile list

    void RefreshList(KnobProfile? select)
    {
        ProfileList.Items.Clear();
        foreach (var profile in Settings.Profiles)
        {
            var item = new ListBoxItem { Tag = profile };
            System.Windows.Automation.AutomationProperties.SetName(item, profile.Name);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold });
            string summary = profile.Turn.Describe() + (profile.Press == PressAction.SwitchMode ? "  ·  press: " + profile.SecondTurn.Describe() : "");
            stack.Children.Add(new TextBlock { Text = summary, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 2, 0, 0) });
            item.Content = stack;
            ProfileList.Items.Add(item);
            if (profile == select) ProfileList.SelectedItem = item;
        }
    }

    void Profile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is ListBoxItem { Tag: KnobProfile profile })
        {
            current = profile;
            BuildEditor();
        }
    }

    void Enabled_Click(object sender, RoutedEventArgs e)
    {
        Settings.KnobEnabled = EnabledSwitch.IsChecked == true;
        Save();
    }

    void Changed()
    {
        Save();
        RefreshList(current);
    }

    // ------------------------------------------------------------------ editor

    void BuildEditor()
    {
        Editor.Children.Clear();
        if (current is not { } p) return;

        var header = new DockPanel();
        if (!p.IsDefault)
        {
            var delete = new Button { Content = "Remove", Style = (Style)FindResource("Ghost"), Padding = new Thickness(12, 6, 12, 6) };
            delete.Click += (_, _) => { Settings.Profiles.Remove(p); Save(); RefreshList(Settings.Profiles[0]); };
            DockPanel.SetDock(delete, Dock.Right);
            header.Children.Add(delete);
        }
        header.Children.Add(new TextBlock { Text = p.Name, FontSize = 19, FontWeight = FontWeights.SemiBold });
        Editor.Children.Add(header);
        Editor.Children.Add(Hint(p.IsDefault
            ? "Used in every app that doesn't have its own entry."
            : $"Used while {p.ProcessName}.exe is the app in front."));

        Editor.Children.Add(Section("Turning the knob"));
        Editor.Children.Add(ModeEditor(p.Turn));

        Editor.Children.Add(Section("Pressing the knob"));
        var press = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        press.Items.Add("Mute (the knob's normal press)");
        press.Items.Add("Switch to a second mode");
        press.Items.Add("Play / Pause");
        press.SelectedIndex = (int)p.Press;
        press.SelectionChanged += (_, _) => { p.Press = (PressAction)press.SelectedIndex; Changed(); BuildEditor(); };
        Editor.Children.Add(press);

        if (p.Press == PressAction.SwitchMode)
        {
            Editor.Children.Add(Section("Second mode (press the knob to switch)"));
            Editor.Children.Add(ModeEditor(p.SecondTurn));
        }

        Editor.Children.Add(Section("Feel"));
        var speedRow = new DockPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        var speedLabel = new TextBlock { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        var speed = new Slider { Minimum = 1, Maximum = 5, IsSnapToTickEnabled = true, TickFrequency = 1, Value = p.Speed };
        void ShowSpeed() => speedLabel.Text = "Scroll amount: " + (int)speed.Value switch { 1 => "tiny", 2 => "small", 3 => "normal", 4 => "large", _ => "huge" };
        ShowSpeed();
        speed.ValueChanged += (_, _) => { p.Speed = (int)speed.Value; ShowSpeed(); Save(); };
        DockPanel.SetDock(speedLabel, Dock.Left);
        speedRow.Children.Add(speedLabel);
        speedRow.Children.Add(speed);
        Editor.Children.Add(speedRow);
        Editor.Children.Add(Switch("Reverse direction", p.Reverse, v => p.Reverse = v));
        Editor.Children.Add(Switch("Go further when turning fast", p.Accelerate, v => p.Accelerate = v));
    }

    UIElement ModeEditor(KnobAction action)
    {
        var panel = new StackPanel();
        var modes = new WrapPanel();
        string group = Guid.NewGuid().ToString();
        foreach (var (mode, label, glyph) in Modes)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), Margin = new Thickness(0, 1, 8, 0) });
            content.Children.Add(new TextBlock { Text = label });
            var radio = new RadioButton { Style = (Style)FindResource("Segment"), Content = content, GroupName = group, IsChecked = action.Mode == mode };
            radio.Checked += (_, _) => { action.Mode = mode; Changed(); BuildEditor(); };
            modes.Children.Add(radio);
        }
        panel.Children.Add(modes);

        switch (action.Mode)
        {
            case KnobMode.Volume:
                panel.Children.Add(Hint("Normal volume control."));
                break;
            case KnobMode.Scroll:
                panel.Children.Add(HoldRow(action));
                panel.Children.Add(Hint("Like the mouse wheel: clockwise scrolls down. In Premiere Pro this moves along the timeline. In CapCut a plain scroll moves the track list; use Scroll sideways (or Alt) to move along the timeline."));
                break;
            case KnobMode.ScrollSideways:
                panel.Children.Add(HoldRow(action));
                panel.Children.Add(Hint("Clockwise scrolls right, like a horizontal scroll wheel. In CapCut this moves along the timeline."));
                break;
            case KnobMode.Zoom:
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(new TextBlock { Text = "Hold", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                var modifier = new ComboBox { Width = 110, ItemsSource = Enum.GetValues<ZoomModifier>(), SelectedItem = action.ZoomWith };
                modifier.SelectionChanged += (_, _) => { action.ZoomWith = (ZoomModifier)modifier.SelectedItem; Changed(); };
                row.Children.Add(modifier);
                row.Children.Add(new TextBlock { Text = "while scrolling.  Clockwise zooms in.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Dim") });
                panel.Children.Add(row);
                panel.Children.Add(Hint("Premiere Pro and After Effects zoom the timeline with Alt+scroll; CapCut uses Ctrl+scroll."));
                break;
            case KnobMode.Keys:
                panel.Children.Add(KeysEditor(action));
                break;
        }
        return panel;
    }

    /// <summary>"Hold [key] while scrolling": some apps scroll sideways or zoom with a modifier held.</summary>
    UIElement HoldRow(KnobAction action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Hold", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var hold = new ComboBox { Width = 120 };
        foreach (var key in Enum.GetValues<HoldKey>()) hold.Items.Add(key == HoldKey.None ? "nothing" : key.ToString());
        hold.SelectedIndex = (int)action.ScrollWith;
        hold.SelectionChanged += (_, _) => { action.ScrollWith = (HoldKey)hold.SelectedIndex; Changed(); };
        row.Children.Add(hold);
        row.Children.Add(new TextBlock { Text = "while scrolling.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Dim") });
        return row;
    }

    UIElement KeysEditor(KnobAction action)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var presets = new ComboBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var preset in KeyPresets.All) presets.Items.Add(preset.Name);
        presets.Items.Add("Custom keys…");
        var found = KeyPresets.Find(action);
        int match = found is null ? -1 : KeyPresets.All.ToList().IndexOf(found);
        presets.SelectedIndex = match >= 0 ? match : KeyPresets.All.Count;
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex < KeyPresets.All.Count)
            {
                action.Clockwise = KeyPresets.All[presets.SelectedIndex].Clockwise.Copy();
                action.CounterClockwise = KeyPresets.All[presets.SelectedIndex].CounterClockwise.Copy();
                Changed();
            }
            BuildEditor();
        };
        panel.Children.Add(presets);

        if (match < 0)
        {
            panel.Children.Add(ShortcutEditor("Clockwise", action.Clockwise));
            panel.Children.Add(ShortcutEditor("Counter-clockwise", action.CounterClockwise));
        }
        else
        {
            panel.Children.Add(Hint($"Clockwise presses {action.Clockwise}, counter-clockwise presses {action.CounterClockwise}."));
        }
        return panel;
    }

    UIElement ShortcutEditor(string label, Shortcut shortcut)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
        foreach (var (name, get, set) in new (string, Func<bool>, Action<bool>)[]
        {
            ("Ctrl", () => shortcut.Ctrl, v => shortcut.Ctrl = v),
            ("Shift", () => shortcut.Shift, v => shortcut.Shift = v),
            ("Alt", () => shortcut.Alt, v => shortcut.Alt = v),
        })
        {
            var chip = new ToggleButton { Style = (Style)FindResource("Chip"), Content = name, IsChecked = get() };
            chip.Click += (_, _) => { set(chip.IsChecked == true); Changed(); };
            row.Children.Add(chip);
        }
        var keys = HidUsages.Keys.Where(k => KeyNames.VkFromHid(k.Usage) is not null).ToList();
        var key = new ComboBox { Width = 170, Margin = new Thickness(6, 0, 0, 0), ItemsSource = keys.Select(k => k.Name).ToList() };
        key.SelectedIndex = keys.FindIndex(k => KeyNames.VkFromHid(k.Usage) == shortcut.Vk);
        key.SelectionChanged += (_, _) =>
        {
            if (key.SelectedIndex >= 0) { shortcut.Vk = KeyNames.VkFromHid(keys[key.SelectedIndex].Usage) ?? 0; Changed(); }
        };
        row.Children.Add(key);
        return row;
    }

    TextBlock Section(string text) => new() { Text = text, Style = (Style)FindResource("H2"), Margin = new Thickness(0, 22, 0, 10) };
    TextBlock Hint(string text) => new() { Text = text, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };

    CheckBox Switch(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Style = (Style)FindResource("Switch"), Content = label, IsChecked = value, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        box.Click += (_, _) => { set(box.IsChecked == true); Save(); };
        return box;
    }

    // ------------------------------------------------------------------ add app

    void AddApp_Click(object sender, RoutedEventArgs e)
    {
        RunningApps.Items.Clear();
        var known = Settings.Profiles.Select(p => p.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apps = Process.GetProcesses()
            .Where(p => p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0)
            .Select(p => p.ProcessName)
            .Where(n => !known.Contains(n) && !n.Equals("AulaStudio", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n);
        foreach (var name in apps) RunningApps.Items.Add(new ListBoxItem { Content = name, Tag = name });
        if (RunningApps.Items.Count == 0) RunningApps.Items.Add(new ListBoxItem { Content = "Open the app first, then try again.", IsEnabled = false });
        AddPopup.IsOpen = true;
    }

    void RunningApp_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (RunningApps.SelectedItem is not ListBoxItem { Tag: string process }) return;
        AddPopup.IsOpen = false;
        var profile = new KnobProfile { Name = process, ProcessName = process, Turn = new KnobAction { Mode = KnobMode.Scroll } };
        Settings.Profiles.Add(profile);
        Save();
        RefreshList(profile);
    }
}
