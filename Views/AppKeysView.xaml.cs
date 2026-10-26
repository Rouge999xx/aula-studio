using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AulaStudio.AppKeys;
using AulaStudio.Input;
using AulaStudio.Protocol;
using AulaStudio.Settings;

namespace AulaStudio.Views;

/// <summary>The App keys page: per-app key rules and macros, carried out by <see cref="AppKeyEngine"/>.</summary>
public partial class AppKeysView : UserControl
{
    static readonly (KeyActionKind Kind, string Label, string Glyph)[] Kinds =
    {
        (KeyActionKind.Key, "Key or shortcut", ""),
        (KeyActionKind.Macro, "Macro", ""),
        (KeyActionKind.Text, "Type text", ""),
        (KeyActionKind.Off, "Nothing", ""),
    };

    /// <summary>Keys a rule can press: every ordinary key, then modifiers, media and browser keys.</summary>
    static readonly List<(int Vk, string Name)> OutputKeys = BuildOutputKeys();

    /// <summary>What's waiting for a key press, so the right button can say "Press … now".</summary>
    enum Waiting { None, NewKey, Trigger, Output, NewStep, StepKeys }

    readonly KeyCapture capture;
    readonly DispatcherTimer saveSoon = new() { Interval = TimeSpan.FromMilliseconds(600) };
    KeyProfile? current;
    KeyRule? editing;
    Waiting waiting;
    MacroStep? waitingStep;
    bool focusNewText;

    AppSettings Settings => Services.Settings;

    public AppKeysView()
    {
        InitializeComponent();
        capture = new KeyCapture(Services.Hook, Dispatcher);
        saveSoon.Tick += (_, _) => { saveSoon.Stop(); Settings.Save(); };
        EnabledSwitch.IsChecked = Settings.AppKeysEnabled;
        Loaded += (_, _) =>
        {
            capture.Attach();
            Services.AppKeys.Activity -= ShowActivity;
            Services.AppKeys.Activity += ShowActivity;
            if (Services.AppKeys.LastActivity is { } last) ShowActivity(last.App, last.What);
        };
        Unloaded += (_, _) =>
        {
            capture.Detach();
            Services.AppKeys.Activity -= ShowActivity;
            if (saveSoon.IsEnabled) { saveSoon.Stop(); Settings.Save(); }
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) EnabledSwitch.IsChecked = Settings.AppKeysEnabled; };
        RefreshList(Settings.KeyProfiles[0]);
    }

    void ShowActivity(string app, string what)
    {
        ActivityText.Text = $"{app}:  {what}";
        ActivityText.Foreground = (Brush)FindResource("Text");
    }

    void Enabled_Click(object sender, RoutedEventArgs e)
    {
        Settings.AppKeysEnabled = EnabledSwitch.IsChecked == true;
        Settings.Save();
    }

    /// <summary>After a change to the list of rules: save, and rebuild the list and the editor.</summary>
    void Changed()
    {
        saveSoon.Stop();
        Settings.Save();
        RefreshList(current);
    }

    /// <summary>After typing in a box: save a moment later, without rebuilding anything (that would steal the focus).</summary>
    void SaveSoon()
    {
        saveSoon.Stop();
        saveSoon.Start();
    }

    // ------------------------------------------------------------------ app list

    void RefreshList(KeyProfile? select)
    {
        AppList.Items.Clear();
        foreach (var profile in Settings.KeyProfiles)
        {
            var item = new ListBoxItem { Tag = profile };
            AutomationProperties.SetName(item, profile.Name);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold });
            int count = profile.Rules.Count(r => r.IsSet);
            stack.Children.Add(new TextBlock
            {
                Text = count == 0 ? "No keys yet" : count == 1 ? "1 key" : $"{count} keys",
                Style = (Style)FindResource("Hint"),
                Margin = new Thickness(0, 2, 0, 0),
            });
            item.Content = stack;
            AppList.Items.Add(item);
            if (profile == select) AppList.SelectedItem = item;   // rebuilds the editor through App_Selected
        }
    }

    void App_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (AppList.SelectedItem is not ListBoxItem { Tag: KeyProfile profile }) return;
        if (profile != current)
        {
            editing = null;
            capture.Cancel();
        }
        current = profile;
        BuildEditor();
    }

    void AddApp_Click(object sender, RoutedEventArgs e)
    {
        RunningApps.Items.Clear();
        var known = Settings.KeyProfiles.Select(p => p.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        // Reuse the knob's friendly name for apps it already knows ("After Effects" rather than "AfterFX").
        string name = Settings.Profiles.FirstOrDefault(p => p.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase))?.Name ?? process;
        var profile = new KeyProfile { Name = name, ProcessName = process };
        Settings.KeyProfiles.Add(profile);
        Settings.Save();
        RefreshList(profile);
    }

    // ------------------------------------------------------------------ editor

    void BuildEditor()
    {
        Editor.Children.Clear();
        if (current is not { } p) return;

        var header = new DockPanel();
        if (!p.IsDefault)
        {
            var remove = new Button { Content = "Remove", Style = (Style)FindResource("Ghost"), Padding = new Thickness(12, 6, 12, 6) };
            remove.Click += (_, _) =>
            {
                Settings.KeyProfiles.Remove(p);
                current = null;
                editing = null;
                Settings.Save();
                RefreshList(Settings.KeyProfiles[0]);
            };
            DockPanel.SetDock(remove, Dock.Right);
            header.Children.Add(remove);
        }
        header.Children.Add(new TextBlock { Text = p.Name, FontSize = 19, FontWeight = FontWeights.SemiBold });
        Editor.Children.Add(header);
        Editor.Children.Add(Hint(p.IsDefault
            ? "Used in every app, unless the app in front has its own rule for the same key. Never inside AULA Studio itself."
            : $"Used while {p.ProcessName}.exe is the app in front. Its rules win over Every app."));

        Editor.Children.Add(Section("Keys"));
        if (p.Rules.Count == 0)
            Editor.Children.Add(Hint("No keys yet. Add one, then choose what it does in this app.", top: 0));
        foreach (var rule in p.Rules) Editor.Children.Add(RuleCard(p, rule));

        var add = new Button
        {
            Style = (Style)FindResource("Ghost"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            Content = IconText("", waiting == Waiting.NewKey ? "Press the key you want to change…" : "Add a key"),
        };
        add.Click += (_, _) => AddKey(p);
        Editor.Children.Add(add);
    }

    UIElement RuleCard(KeyProfile profile, KeyRule rule)
    {
        bool open = rule == editing;
        var card = new Border
        {
            Background = (Brush)FindResource("Card"),
            BorderBrush = open ? (Brush)FindResource("Accent") : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var stack = new StackPanel();

        var row = new DockPanel();
        var delete = IconButton("", "Delete this rule", () =>
        {
            profile.Rules.Remove(rule);
            if (editing == rule) editing = null;
            Changed();
        });
        var edit = new Button { Content = open ? "Done" : "Edit", Style = (Style)FindResource("Ghost"), Padding = new Thickness(12, 5, 12, 5) };
        edit.Click += (_, _) =>
        {
            editing = open ? null : rule;
            capture.Cancel();
            Changed();
        };
        DockPanel.SetDock(delete, Dock.Right);
        DockPanel.SetDock(edit, Dock.Right);
        row.Children.Add(delete);
        row.Children.Add(edit);

        var summary = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(Keycap(KeyNames.NameOfVk(rule.Trigger)));
        summary.Children.Add(new TextBlock { Text = "→", Margin = new Thickness(10, 0, 10, 0), Foreground = (Brush)FindResource("Dim"), VerticalAlignment = VerticalAlignment.Center });
        summary.Children.Add(new TextBlock
        {
            Text = rule.Describe(),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource(rule.IsSet ? "Text" : "Dim"),
        });
        row.Children.Add(summary);
        stack.Children.Add(row);

        if (open) stack.Children.Add(RuleEditor(profile, rule));
        card.Child = stack;
        return card;
    }

    UIElement RuleEditor(KeyProfile profile, KeyRule rule)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 4) };

        var when = new StackPanel { Orientation = Orientation.Horizontal };
        when.Children.Add(new TextBlock { Text = "When I press", Width = 110, VerticalAlignment = VerticalAlignment.Center });
        var trigger = new Button
        {
            Style = (Style)FindResource("Ghost"),
            Padding = new Thickness(12, 6, 12, 6),
            Content = waiting == Waiting.Trigger ? "Press the key now…" : KeyNames.NameOfVk(rule.Trigger),
            ToolTip = "Click, then press a different key to use for this rule",
        };
        trigger.Click += (_, _) => Capture(Waiting.Trigger, shortcut: false, key => SetTrigger(profile, rule, key.Vk));
        when.Children.Add(trigger);
        panel.Children.Add(when);

        panel.Children.Add(new TextBlock { Text = "It does", Margin = new Thickness(0, 14, 0, 6), Foreground = (Brush)FindResource("Dim") });
        var kinds = new WrapPanel();
        string group = Guid.NewGuid().ToString();
        foreach (var (kind, label, glyph) in Kinds)
        {
            var radio = new RadioButton { Style = (Style)FindResource("Segment"), Content = IconText(glyph, label), GroupName = group, IsChecked = rule.Kind == kind };
            radio.Checked += (_, _) => { rule.Kind = kind; Changed(); };
            kinds.Children.Add(radio);
        }
        panel.Children.Add(kinds);

        panel.Children.Add(rule.Kind switch
        {
            KeyActionKind.Key => OutputEditor(rule),
            KeyActionKind.Macro => MacroEditor(rule),
            KeyActionKind.Text => TextEditor(rule),
            _ => Hint("The key does nothing at all in this app."),
        });
        return panel;
    }

    UIElement OutputEditor(KeyRule rule)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var press = new Button
        {
            Style = (Style)FindResource("Ghost"),
            Padding = new Thickness(12, 6, 12, 6),
            Content = IconText("", waiting == Waiting.Output ? "Press the key or shortcut now…" : "Press a key or shortcut"),
        };
        press.Click += (_, _) => Capture(Waiting.Output, shortcut: true, key => { rule.Output = key; Changed(); });
        row.Children.Add(press);
        row.Children.Add(new TextBlock { Text = "or pick one below", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Dim") });
        panel.Children.Add(row);
        panel.Children.Add(ShortcutRow(rule.Output));
        panel.Children.Add(Hint("Holding the key holds this one down too, so it repeats like a normal key. Keys you hold with it, like Shift, still count."));
        return panel;
    }

    UIElement ShortcutRow(Shortcut shortcut)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (name, get, set) in new (string, Func<bool>, Action<bool>)[]
        {
            ("Ctrl", () => shortcut.Ctrl, v => shortcut.Ctrl = v),
            ("Shift", () => shortcut.Shift, v => shortcut.Shift = v),
            ("Alt", () => shortcut.Alt, v => shortcut.Alt = v),
            ("Win", () => shortcut.Win, v => shortcut.Win = v),
        })
        {
            var chip = new ToggleButton { Style = (Style)FindResource("Chip"), Content = name, IsChecked = get() };
            chip.Click += (_, _) => { set(chip.IsChecked == true); Changed(); };
            row.Children.Add(chip);
        }
        var key = new ComboBox { Width = 190, Margin = new Thickness(6, 0, 0, 0), ItemsSource = OutputKeys.Select(k => k.Name).ToList() };
        key.SelectedIndex = OutputKeys.FindIndex(k => k.Vk == shortcut.Vk);
        key.SelectionChanged += (_, _) =>
        {
            if (key.SelectedIndex >= 0) { shortcut.Vk = OutputKeys[key.SelectedIndex].Vk; Changed(); }
        };
        AutomationProperties.SetName(key, "Key");
        row.Children.Add(key);
        return row;
    }

    UIElement MacroEditor(KeyRule rule)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (rule.Steps.Count == 0) panel.Children.Add(Hint("No steps yet. Add what the macro should do, in order.", top: 0));
        for (int i = 0; i < rule.Steps.Count; i++) panel.Children.Add(StepRow(rule, i));

        var adds = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        adds.Children.Add(SmallButton("", waiting == Waiting.NewStep ? "Press the keys now…" : "Press keys", () =>
            Capture(Waiting.NewStep, shortcut: true, key => { rule.Steps.Add(new MacroStep { Kind = MacroStepKind.Keys, Keys = key }); Changed(); })));
        adds.Children.Add(SmallButton("", "Type text", () =>
        {
            rule.Steps.Add(new MacroStep { Kind = MacroStepKind.Text });
            focusNewText = true;
            Changed();
        }));
        adds.Children.Add(SmallButton("", "Wait", () => { rule.Steps.Add(new MacroStep { Kind = MacroStepKind.Wait }); Changed(); }));
        panel.Children.Add(adds);
        panel.Children.Add(Hint("Runs once each time you press the key, one step after another."));
        return panel;
    }

    UIElement StepRow(KeyRule rule, int index)
    {
        var step = rule.Steps[index];
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var remove = IconButton("", "Remove this step", () => { rule.Steps.RemoveAt(index); Changed(); });
        var down = IconButton("", "Move this step down", () => Move(rule, index, +1));
        var up = IconButton("", "Move this step up", () => Move(rule, index, -1));
        down.IsEnabled = index < rule.Steps.Count - 1;
        up.IsEnabled = index > 0;
        foreach (var button in new[] { remove, down, up })
        {
            button.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        var number = new TextBlock { Text = $"{index + 1}.", Width = 26, Foreground = (Brush)FindResource("Dim"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(number, Dock.Left);
        row.Children.Add(number);

        var body = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        switch (step.Kind)
        {
            case MacroStepKind.Keys:
                var keys = new Button
                {
                    Style = (Style)FindResource("Ghost"),
                    Padding = new Thickness(10, 5, 10, 5),
                    Content = waiting == Waiting.StepKeys && waitingStep == step ? "Press the keys now…" : "Press " + step.Keys,
                    ToolTip = "Click, then press the key or shortcut for this step",
                };
                keys.Click += (_, _) => Capture(Waiting.StepKeys, shortcut: true, key => { step.Keys = key; Changed(); }, step);
                body.Children.Add(keys);
                break;
            case MacroStepKind.Text:
                body.Children.Add(new TextBlock { Text = "Type", Width = 46, VerticalAlignment = VerticalAlignment.Center });
                var text = new TextBox { Width = 300, Padding = new Thickness(8, 4, 8, 4), Text = step.Text };
                text.TextChanged += (_, _) => { step.Text = text.Text; SaveSoon(); };
                AutomationProperties.SetName(text, "Text to type");
                if (focusNewText && index == rule.Steps.Count - 1)
                {
                    focusNewText = false;
                    text.Loaded += (_, _) => text.Focus();
                }
                body.Children.Add(text);
                break;
            default:
                body.Children.Add(new TextBlock { Text = "Wait", Width = 46, VerticalAlignment = VerticalAlignment.Center });
                var ms = new TextBox { Width = 70, Padding = new Thickness(8, 4, 8, 4), Text = step.Milliseconds.ToString() };
                ms.TextChanged += (_, _) =>
                {
                    if (int.TryParse(ms.Text, out int value)) { step.Milliseconds = Math.Clamp(value, 0, 60_000); SaveSoon(); }
                };
                AutomationProperties.SetName(ms, "Milliseconds to wait");
                body.Children.Add(ms);
                body.Children.Add(new TextBlock { Text = "ms", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Dim") });
                break;
        }
        row.Children.Add(body);
        return row;
    }

    void Move(KeyRule rule, int index, int by)
    {
        int to = index + by;
        if (to < 0 || to >= rule.Steps.Count) return;
        (rule.Steps[index], rule.Steps[to]) = (rule.Steps[to], rule.Steps[index]);
        Changed();
    }

    UIElement TextEditor(KeyRule rule)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var box = new TextBox
        {
            Text = rule.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 76,
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        box.TextChanged += (_, _) => { rule.Text = box.Text; SaveSoon(); };
        AutomationProperties.SetName(box, "Text to type");
        panel.Children.Add(box);
        panel.Children.Add(Hint("Typed into the app, character by character. A new line presses Enter."));
        return panel;
    }

    // ------------------------------------------------------------------ capturing keys

    /// <summary>Waits for a key press (shown on the button that asked), then hands it over.</summary>
    void Capture(Waiting what, bool shortcut, Action<Shortcut> done, MacroStep? step = null)
    {
        capture.Cancel();
        waiting = what;
        waitingStep = step;
        capture.Start(shortcut,
            key => { waiting = Waiting.None; waitingStep = null; done(key); },
            () => { waiting = Waiting.None; waitingStep = null; BuildEditor(); });
        BuildEditor();
    }

    void AddKey(KeyProfile profile) => Capture(Waiting.NewKey, shortcut: false, key =>
    {
        if (!Allowed(key.Vk)) { BuildEditor(); return; }
        var existing = profile.Rules.FirstOrDefault(r => r.Trigger == key.Vk);
        if (existing is not null)
        {
            Services.Toast($"{KeyNames.NameOfVk(key.Vk)} already has a rule in {profile.Name}. Here it is.");
            editing = existing;
            BuildEditor();
            return;
        }
        var rule = new KeyRule { Trigger = key.Vk };
        profile.Rules.Add(rule);
        editing = rule;
        Changed();
    });

    void SetTrigger(KeyProfile profile, KeyRule rule, int vk)
    {
        if (!Allowed(vk)) { BuildEditor(); return; }
        if (profile.Rules.Any(r => r != rule && r.Trigger == vk))
        {
            Services.Toast($"{KeyNames.NameOfVk(vk)} already has a rule in {profile.Name}.", isError: true);
            BuildEditor();
            return;
        }
        rule.Trigger = vk;
        Changed();
    }

    static bool Allowed(int vk)
    {
        if (!AppKeyEngine.IsKnobKey(vk)) return true;
        Services.Toast("Those are the knob's keys. Set what the knob does on the Knob page.", isError: true);
        return false;
    }

    // ------------------------------------------------------------------ small building blocks

    TextBlock Section(string text) => new() { Text = text, Style = (Style)FindResource("H2"), Margin = new Thickness(0, 22, 0, 10) };

    TextBlock Hint(string text, double top = 6) => new()
    {
        Text = text,
        Style = (Style)FindResource("Hint"),
        Margin = new Thickness(0, top, 0, 0),
        MaxWidth = 640,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    StackPanel IconText(string glyph, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    Button IconButton(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Style = (Style)FindResource("Ghost"),
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(6, 0, 0, 0),
            Content = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12 },
            ToolTip = tip,
        };
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    Button SmallButton(string glyph, string text, Action click)
    {
        var button = new Button { Style = (Style)FindResource("Ghost"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), Content = IconText(glyph, text) };
        button.Click += (_, _) => click();
        return button;
    }

    Border Keycap(string name) => new()
    {
        Background = (Brush)FindResource("CardHover"),
        BorderBrush = (Brush)FindResource("Edge"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 4, 10, 4),
        MinWidth = 46,
        Child = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
    };

    static List<(int Vk, string Name)> BuildOutputKeys()
    {
        var keys = HidUsages.Keys
            .Select(k => (Vk: KeyNames.VkFromHid(k.Usage) ?? 0, k.Name))
            .Where(k => k.Vk != 0)
            .ToList();
        int[] more = { 0xA2, 0xA3, 0xA0, 0xA1, 0xA4, 0xA5, 0x5B, 0x5C, 0xB3, 0xB0, 0xB1, 0xB2, 0xAD, 0xAE, 0xAF, 0xA6, 0xA7, 0xAC, 0xB4, 0xB7 };
        keys.AddRange(more.Where(vk => keys.All(k => k.Vk != vk)).Select(vk => (vk, KeyNames.NameOfVk(vk))));
        return keys;
    }
}
