using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AulaStudio.Backup;
using AulaStudio.Input;
using AulaStudio.Layout;
using AulaStudio.Protocol;

namespace AulaStudio.Views;

public partial class KeyboardView : UserControl
{
    /// <summary>A pick-list entry: what it's called and the slot value it stands for.</summary>
    sealed record Choice(string Name, KeyEntry Entry)
    {
        public override string ToString() => Name;
    }

    static readonly Brush KeyFace = Frozen(Color.FromRgb(0x2B, 0x2E, 0x39));
    static readonly Brush KeyFaceHover = Frozen(Color.FromRgb(0x34, 0x38, 0x45));
    static readonly Brush KeyEdge = Frozen(Color.FromRgb(0x3B, 0x3F, 0x4C));
    static readonly Brush KnobFace = Frozen(Color.FromRgb(0x3A, 0x3E, 0x4A));

    // [layer] → table. "loaded" is the keyboard as last read, "edited" is the working copy, "original" the initial backup.
    readonly KeyMatrix?[] loaded = new KeyMatrix?[2];
    readonly KeyMatrix?[] edited = new KeyMatrix?[2];
    readonly KeyMatrix?[] original = new KeyMatrix?[2];
    readonly Dictionary<int, Border> keyVisuals = new();
    readonly HashSet<int> keysDown = new();
    readonly HashSet<int> swallowRelease = new();
    readonly DispatcherTimer captureTimeout = new() { Interval = TimeSpan.FromSeconds(8) };
    int layer;
    KeyCap? selected;
    bool capturing, suppressChoiceEvent, saving;

    public KeyboardView()
    {
        InitializeComponent();
        var keys = HidUsages.Keys.Select(k => new Choice(k.Name, KeyEntry.Keyboard(k.Usage))).ToList();
        var modifiers = HidUsages.ModifierBits.Select(m => new Choice(m.Name, KeyEntry.Keyboard(0, m.Bit)));
        KeyChoice.ItemsSource = keys.Concat(modifiers).ToList();
        ShortcutKey.ItemsSource = keys;
        foreach (var media in HidUsages.MediaKeys)
        {
            var button = new Button { Content = media.Name, Margin = new Thickness(0, 0, 8, 8), Tag = media.Usage };
            button.Click += (_, _) => Assign(KeyEntry.Media((ushort)button.Tag));
            PanelMedia.Children.Add(button);
        }
        captureTimeout.Tick += (_, _) => StopCapture();

        // The main window makes a new page each time it's opened, so hook in only while this one is on screen.
        Loaded += (_, _) =>
        {
            Services.Keyboard.StateChanged -= OnStateChanged;
            Services.Keyboard.StateChanged += OnStateChanged;
            Services.Hook.Remove(OnKey);
            Services.Hook.AddFirst(OnKey);
        };
        Unloaded += (_, _) =>
        {
            Services.Keyboard.StateChanged -= OnStateChanged;
            Services.Hook.Remove(OnKey);
            if (capturing) StopCapture();
        };
        BuildKeys();
        LoadFromKeyboard();
    }

    void OnStateChanged() => Dispatcher.BeginInvoke(LoadFromKeyboard);

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    Brush Res(string key) => (Brush)FindResource(key);

    // ------------------------------------------------------------------ state

    void LoadFromKeyboard()
    {
        if (saving) return;
        var keyboard = Services.Keyboard;
        Disconnected.Visibility = keyboard.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        DisconnectedDetail.Text = keyboard.Status;
        if (!keyboard.IsConnected) { RefreshAll(); return; }

        bool hadEdits = HasPendingEdits();
        var fresh = new[] { keyboard.Base!.Clone(), keyboard.FnLayer!.Clone() };
        for (int i = 0; i < 2; i++)
        {
            // Pending edits survive a reconnect; they are saved as differences on top of whatever the keyboard has.
            if (!hadEdits || edited[i] is null) edited[i] = fresh[i].Clone();
            loaded[i] = fresh[i];
        }
        var initial = BackupStore.Initial();
        original[0] = initial?.BaseLayer ?? loaded[0];
        original[1] = initial?.FnLayer ?? loaded[1];
        RefreshAll();
    }

    bool HasPendingEdits() => PendingSlots(0).Any() || PendingSlots(1).Any();

    IEnumerable<int> PendingSlots(int l) =>
        loaded[l] is { } a && edited[l] is { } b ? b.DifferencesFrom(a) : Enumerable.Empty<int>();

    void RefreshAll()
    {
        RenderKeys();
        RefreshEditor();
        int pending = PendingSlots(0).Count() + PendingSlots(1).Count();
        PendingText.Text = pending == 0 ? "" : $"{pending} unsaved change{(pending == 1 ? "" : "s")}";
        SaveButton.IsEnabled = pending > 0 && Services.Keyboard.IsConnected && !saving;
        UndoButton.IsEnabled = pending > 0 && !saving;
    }

    // ------------------------------------------------------------------ keyboard picture

    void BuildKeys()
    {
        foreach (var key in F75Layout.Keys)
        {
            double pad = key.IsKnob ? 0 : 6;
            var legend = new TextBlock { Text = key.Label, FontSize = key.Label.Length > 3 ? 7.6 : 9.5, Foreground = Res("Text") };
            var function = new TextBlock { FontSize = 6.6, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            var stack = new StackPanel { Margin = new Thickness(key.IsKnob ? 0 : 5, key.IsKnob ? 0 : 4, 4, 0) };
            if (key.IsKnob)
            {
                stack.HorizontalAlignment = HorizontalAlignment.Center;
                stack.VerticalAlignment = VerticalAlignment.Center;
                legend.Text = "";
                legend.FontFamily = (FontFamily)FindResource("IconFont");
                legend.FontSize = 11;
                legend.HorizontalAlignment = HorizontalAlignment.Center;
                function.HorizontalAlignment = HorizontalAlignment.Center;
            }
            stack.Children.Add(legend);
            stack.Children.Add(function);
            var border = new Border
            {
                Width = key.X2 - key.X1 + 2 * pad,
                Height = key.Y2 - key.Y1 + 2 * pad,
                CornerRadius = new CornerRadius(key.IsKnob ? 20 : 5),
                Background = key.IsKnob ? KnobFace : KeyFace,
                BorderBrush = KeyEdge,
                BorderThickness = new Thickness(1),
                Child = stack,
                Tag = key,
                ToolTip = F75Layout.FullName(key),
            };
            // A real button, so keys can be reached with Tab/Enter and are named for screen readers.
            var button = new Button { Style = (Style)FindResource("KeyButton"), Content = border };
            AutomationProperties.SetName(button, F75Layout.FullName(key));
            button.Click += (_, _) => Select(key);
            button.MouseEnter += (_, _) => { if (selected != key) border.Background = KeyFaceHover; };
            button.MouseLeave += (_, _) => border.Background = key.IsKnob ? KnobFace : KeyFace;
            button.GotKeyboardFocus += (_, _) => border.Background = KeyFaceHover;
            button.LostKeyboardFocus += (_, _) => border.Background = key.IsKnob ? KnobFace : KeyFace;
            Canvas.SetLeft(button, key.X1 - pad);
            Canvas.SetTop(button, key.Y1 - pad);
            KeyCanvas.Children.Add(button);
            keyVisuals[key.Slot] = border;
        }
    }

    void RenderKeys()
    {
        foreach (var key in F75Layout.Keys)
        {
            var border = keyVisuals[key.Slot];
            var function = (TextBlock)((StackPanel)border.Child).Children[1];
            var entry = edited[layer]?[key.Slot];
            var loadedEntry = loaded[layer]?[key.Slot];
            var originalEntry = original[layer]?[key.Slot];
            bool pending = entry is not null && entry != loadedEntry;
            bool remapped = entry is not null && entry != originalEntry;

            // Base layer: only show a function when it differs from the original. Fn layer: show every assigned function.
            string text = entry is not { } e ? ""
                : layer == 1 ? (e.Kind == EntryKind.Off ? "" : e.Describe())
                : remapped ? e.Describe() : "";
            function.Text = text;
            function.Foreground = pending ? Res("Warn") : Res("Accent");
            border.ToolTip = text.Length > 0 ? $"{F75Layout.FullName(key)}: {text}" : F75Layout.FullName(key);

            bool isSelected = selected == key;
            border.BorderBrush = isSelected ? Res("Accent") : pending ? Res("Warn") : KeyEdge;
            border.BorderThickness = new Thickness(isSelected || pending ? 1.6 : 1);
            border.Opacity = Services.Keyboard.IsConnected ? 1 : 0.5;
        }
    }

    void Layer_Checked(object sender, RoutedEventArgs e)
    {
        layer = FnTab?.IsChecked == true ? 1 : 0;
        if (KeyCanvas is not null) RefreshAll();
    }

    void Select(KeyCap key)
    {
        selected = key;
        StopCapture();
        RefreshAll();
    }

    // ------------------------------------------------------------------ editor

    void RefreshEditor()
    {
        if (selected is null || edited[layer] is null)
        {
            EditorBody.Visibility = Visibility.Collapsed;
            LockedBox.Visibility = Visibility.Collapsed;
            KeyTitle.Text = Services.Keyboard.IsConnected ? "Click a key to change what it does" : "Waiting for the keyboard";
            KeySubtitle.Text = layer == 1 ? "The Fn layer is what keys do while you hold Fn." : "";
            SlotText.Text = "";
            return;
        }
        var key = selected;
        var entry = edited[layer]![key.Slot];
        var before = loaded[layer]![key.Slot];
        var originalEntry = original[layer]![key.Slot];
        string layerName = layer == 1 ? "Fn + " : "";
        KeyTitle.Text = layerName + F75Layout.FullName(key);
        SlotText.Text = $"slot {key.Slot} · {entry.Hex}";
        KeySubtitle.Text = entry != before
            ? $"Will change from {before.Describe()} to {entry.Describe()} when you save."
            : $"Currently: {entry.Describe()}" + (entry != originalEntry ? $"  (originally {originalEntry.Describe()})" : "");

        bool locked = KeyMatrix.IsProtected(key.Slot) && !(Services.Settings.Advanced && key.Slot != KeyMatrix.TrailerSlot);
        if (key.IsKnob && layer == 0)
        {
            LockedText.Text = "Turning the knob is handled on the Knob page. Its press is the keyboard's own knob function (mute, or hold to switch to lighting), and it's locked to keep the knob working.";
            locked = true;
        }
        else if (locked)
        {
            LockedText.Text = "The Fn key is locked: without it you'd lose every Fn shortcut. Turn on Advanced in Settings if you really need to move it.";
        }
        LockedBox.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        EditorBody.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        OriginalText.Text = $"Originally: {originalEntry.Describe()}";

        suppressChoiceEvent = true;
        KeyChoice.SelectedItem = ((IEnumerable<Choice>)KeyChoice.ItemsSource).FirstOrDefault(c => c.Entry == entry);
        suppressChoiceEvent = false;
    }

    void EditorTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelKey is null) return;
        PanelKey.Visibility = TabKey.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelShortcut.Visibility = TabShortcut.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelMedia.Visibility = TabMedia.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelOff.Visibility = TabOff.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelOriginal.Visibility = TabOriginal.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void Assign(KeyEntry entry)
    {
        if (selected is null || edited[layer] is null) return;
        edited[layer]![selected.Slot] = entry;
        RefreshAll();
    }

    void KeyChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!suppressChoiceEvent && KeyChoice.SelectedItem is Choice choice) Assign(choice.Entry);
    }

    void UseShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutKey.SelectedItem is not Choice choice) { Services.Toast("Pick the key for the shortcut first.", true); return; }
        byte mods = 0;
        if (ModCtrl.IsChecked == true) mods |= 0x01;
        if (ModShift.IsChecked == true) mods |= 0x02;
        if (ModAlt.IsChecked == true) mods |= 0x04;
        if (ModWin.IsChecked == true) mods |= 0x08;
        Assign(KeyEntry.Keyboard(choice.Entry.Usage, mods));
    }

    void TurnOff_Click(object sender, RoutedEventArgs e) => Assign(KeyEntry.Off);

    void ResetOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (selected is not null && original[layer] is { } o) Assign(o[selected.Slot]);
    }

    // ------------------------------------------------------------------ capture and tester

    void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (capturing) { StopCapture(); return; }
        capturing = true;
        CaptureText.Text = "Press any key now…";
        captureTimeout.Start();
    }

    void StopCapture()
    {
        capturing = false;
        captureTimeout.Stop();
        CaptureText.Text = "Press a key to use it";
    }

    /// <summary>Runs before the knob engine, so capture can grab any key, including the knob's volume keys.</summary>
    bool OnKey(KeyEvent e)
    {
        if (e.Ours) return false;
        if (!e.Down)
        {
            keysDown.Remove(e.Vk);
            return swallowRelease.Remove(e.Vk);
        }
        if (capturing)
        {
            swallowRelease.Add(e.Vk);
            Dispatcher.BeginInvoke(() => FinishCapture(e.Vk));
            return true;
        }
        // Only while AULA Studio itself is in front, so typing in other apps (passwords included) never shows up here.
        if (keysDown.Add(e.Vk))   // ignore auto-repeat
            Dispatcher.BeginInvoke(() => { if (IsVisible && Window.GetWindow(this)?.IsActive == true) AddTesterChip(e.Vk); });
        return false;
    }

    void FinishCapture(int vk)
    {
        StopCapture();
        KeyEntry? entry = vk switch
        {
            0xAF => KeyEntry.Media(0x00E9), 0xAE => KeyEntry.Media(0x00EA), 0xAD => KeyEntry.Media(0x00E2),
            0xB3 => KeyEntry.Media(0x00CD), 0xB0 => KeyEntry.Media(0x00B5), 0xB1 => KeyEntry.Media(0x00B6), 0xB2 => KeyEntry.Media(0x00B7),
            _ when KeyNames.ModifierBit(vk) is byte bit and > 0 => KeyEntry.Keyboard(0, bit),
            _ when KeyNames.HidFromVk(vk) is byte usage => KeyEntry.Keyboard(usage),
            _ => null,
        };
        if (entry is { } value) Assign(value);
        else Services.Toast($"AULA Studio can't assign {KeyNames.NameOfVk(vk)} from capture. Pick it from the list instead.", true);
    }

    void AddTesterChip(int vk)
    {
        var chip = new Border
        {
            Background = Res("Card"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 6, 6),
            Child = new TextBlock { Text = KeyNames.NameOfVk(vk), FontWeight = FontWeights.SemiBold },
        };
        TesterChips.Children.Insert(0, chip);
        while (TesterChips.Children.Count > 14) TesterChips.Children.RemoveAt(TesterChips.Children.Count - 1);
        if (TesterChips.Children.Count > 1) ((Border)TesterChips.Children[1]).Opacity = 0.75;
    }

    // ------------------------------------------------------------------ save / undo

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (loaded[0] is null || loaded[1] is null) return;
        saving = true;
        SaveText.Text = "Saving…";
        RefreshAll();
        var baseLayer = (loaded[0]!, edited[0]!);
        var fnLayer = (loaded[1]!, edited[1]!);
        bool advanced = Services.Settings.Advanced;
        var result = await Task.Run(() => Services.Keyboard.SaveChanges(baseLayer, fnLayer, advanced));
        saving = false;
        SaveText.Text = "Save to keyboard";
        if (result.Ok)
        {
            // Start fresh from what the keyboard now holds.
            edited[0] = null;
            edited[1] = null;
        }
        LoadFromKeyboard();
        Services.Toast(result.Message, !result.Ok);
    }

    void Undo_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 2; i++) edited[i] = loaded[i]?.Clone();
        RefreshAll();
    }
}
