using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AulaStudio.Backup;
using AulaStudio.Device;
using AulaStudio.Protocol;

namespace AulaStudio.Views;

public partial class BackupsView : UserControl
{
    public BackupsView()
    {
        InitializeComponent();
        Services.Keyboard.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Refresh();
    }

    void Refresh()
    {
        BackupList.Items.Clear();
        var backups = BackupStore.List();
        var latest = backups.FirstOrDefault();
        foreach (var backup in backups)
        {
            var row = new DockPanel();
            var restore = new Button { Content = "Restore", Padding = new Thickness(14, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center, Tag = backup };
            restore.Click += Restore_Click;
            DockPanel.SetDock(restore, Dock.Right);
            row.Children.Add(restore);

            var text = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = backup.Created.LocalDateTime.ToString("ddd, MMM d  ·  h:mm:ss tt"), FontWeight = FontWeights.SemiBold });
            if (backup.IsInitial) title.Children.Add(Badge("Original", "Accent"));
            if (backup == latest) title.Children.Add(Badge("Latest", "Good"));
            text.Children.Add(title);
            text.Children.Add(new TextBlock { Text = Describe(backup), Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 3, 0, 0) });
            row.Children.Add(text);
            BackupList.Items.Add(new ListBoxItem { Content = row });
        }
        if (backups.Count == 0)
            BackupList.Items.Add(new ListBoxItem { Content = new TextBlock { Text = "No backups yet. One is made automatically when the keyboard connects." }, IsEnabled = false });
    }

    /// <summary>The backup's note plus how many keys differ from the original.</summary>
    static string Describe(KeyboardBackup backup)
    {
        string note = backup.Note;
        var initial = BackupStore.Initial();
        if (initial is null || backup.IsInitial || !backup.IsValid(out _)) return note;
        int changed = backup.BaseLayer.DifferencesFrom(initial.BaseLayer).Count() + backup.FnLayer.DifferencesFrom(initial.FnLayer).Count();
        return note + (changed == 0 ? "  ·  same as original" : $"  ·  {changed} key{(changed == 1 ? "" : "s")} remapped");
    }

    Border Badge(string text, string brush) => new()
    {
        Background = (Brush)FindResource(brush),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 1, 7, 2),
        Margin = new Thickness(10, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentInk") },
    };

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var backup = (KeyboardBackup)((Button)sender).Tag;
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            $"Put every key back the way it was on {backup.Created.LocalDateTime:MMM d, h:mm tt}?\n\nThe keyboard's current setup is backed up first, so you can undo this.",
            "Restore backup", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        await Run(() => Services.Keyboard.Restore(backup));
    }

    async void BackupNow_Click(object sender, RoutedEventArgs e) => await Run(() => Services.Keyboard.BackupNow());
    async void RepairFn_Click(object sender, RoutedEventArgs e) => await Run(() => Services.Keyboard.RepairFnKey());
    async void RepairWin_Click(object sender, RoutedEventArgs e) => await Run(() => Services.Keyboard.RepairWinKey());

    async Task Run(Func<OperationResult> work)
    {
        IsEnabled = false;
        var result = await Task.Run(work);
        IsEnabled = true;
        Services.Toast(result.Message, !result.Ok);
        Refresh();
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupStore.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", BackupStore.Folder) { UseShellExecute = true });
    }
}
