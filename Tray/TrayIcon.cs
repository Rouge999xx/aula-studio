using System.Runtime.InteropServices;
using AulaStudio.Settings;
using Gdi = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AulaStudio.Tray;

/// <summary>Tray icon: left-click opens AULA Studio, right-click shows a dark menu (same look as the brightness widget).</summary>
public sealed class TrayIcon : IDisposable
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);

    readonly WinForms.NotifyIcon icon;
    readonly WinForms.ContextMenuStrip menu;
    readonly IntPtr iconHandle;
    readonly MainWindow window;

    public TrayIcon(MainWindow window)
    {
        this.window = window;
        menu = new WinForms.ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        BuildMenu();
        menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };
        icon = new WinForms.NotifyIcon { Icon = CreateIcon(out iconHandle), ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) window.BringToFront(); };
        Services.Knob.ModeChanged += UpdateTooltip;
        UpdateTooltip();
    }

    void UpdateTooltip()
    {
        var manual = Services.Settings.ManualMode;
        icon.Text = "AULA Studio  ·  knob: " + (manual is null ? "automatic (per app)" : manual.Describe());
    }

    void BuildMenu()
    {
        menu.Items.Clear();
        var open = new WinForms.ToolStripMenuItem("Open AULA Studio", null, (_, _) => window.BringToFront());
        open.Font = new Gdi.Font(menu.Font, Gdi.FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        // Knob modes, one click each. Automatic follows the per-app settings; the others apply in every app.
        menu.Items.Add(new WinForms.ToolStripMenuItem("KNOB MODE") { Enabled = false, Font = new Gdi.Font(menu.Font.FontFamily, 7.5f, Gdi.FontStyle.Bold) });
        var manual = Services.Settings.ManualMode;
        menu.Items.Add(ModeItem("Automatic (per app)", manual is null, null));
        foreach (var (label, action) in ManualModes.All)
            menu.Items.Add(ModeItem(label, manual?.SameAs(action) == true, action));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var knob = new WinForms.ToolStripMenuItem("Knob actions") { Checked = Services.Settings.KnobEnabled, CheckOnClick = true };
        knob.CheckedChanged += (_, _) => { Services.Settings.KnobEnabled = knob.Checked; Services.Settings.Save(); };
        menu.Items.Add(knob);
        var startup = new WinForms.ToolStripMenuItem("Start with Windows") { Checked = Startup.IsEnabled, CheckOnClick = true };
        startup.CheckedChanged += (_, _) => Startup.IsEnabled = startup.Checked;
        menu.Items.Add(startup);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Exit", null, (_, _) => ((App)System.Windows.Application.Current).ExitApp()));
        foreach (WinForms.ToolStripItem item in menu.Items)
            if (item is not WinForms.ToolStripSeparator) item.Padding = new WinForms.Padding(4, 4, 8, 4);
    }

    static WinForms.ToolStripMenuItem ModeItem(string label, bool active, KnobAction? mode)
    {
        var item = new WinForms.ToolStripMenuItem(label) { Checked = active };
        item.Click += (_, _) => Services.Knob.SetManualMode(mode);
        return item;
    }

    /// <summary>A white knob (ring + pointer) drawn at the tray's exact size, matching Windows' own tray icons.</summary>
    static Gdi.Icon CreateIcon(out IntPtr handle)
    {
        int size = WinForms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Gdi.Bitmap(size, size);
        using (var g = Gdi.Graphics.FromImage(bmp))
        using (var pen = new Gdi.Pen(Gdi.Color.White, Math.Max(1.4f, size / 10f)))
        {
            g.SmoothingMode = Gdi.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Gdi.Color.Transparent);
            float c = size / 2f, r = size * 0.36f;
            g.DrawEllipse(pen, c - r, c - r, 2 * r, 2 * r);
            pen.StartCap = pen.EndCap = Gdi.Drawing2D.LineCap.Round;
            double a = -2.2;
            g.DrawLine(pen, c + (float)Math.Cos(a) * r * 0.15f, c + (float)Math.Sin(a) * r * 0.15f, c + (float)Math.Cos(a) * r * 0.72f, c + (float)Math.Sin(a) * r * 0.72f);
        }
        handle = bmp.GetHicon();
        return Gdi.Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        icon.Visible = false;   // otherwise a dead icon lingers until the mouse passes over it
        icon.Dispose();
        menu.Dispose();
        DestroyIcon(iconHandle);
    }
}

sealed class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    static readonly Gdi.Color Text = Gdi.Color.FromArgb(243, 243, 245);
    static readonly Gdi.Color Dim = Gdi.Color.FromArgb(150, 150, 160);
    static readonly Gdi.Color Accent = Gdi.Color.FromArgb(96, 205, 255);

    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
    {
        var box = e.ImageRectangle;
        using var pen = new Gdi.Pen(Accent, 2f);
        e.Graphics.SmoothingMode = Gdi.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[]
        {
            new Gdi.PointF(box.Left + box.Width * 0.2f, box.Top + box.Height * 0.52f),
            new Gdi.PointF(box.Left + box.Width * 0.42f, box.Top + box.Height * 0.74f),
            new Gdi.PointF(box.Left + box.Width * 0.82f, box.Top + box.Height * 0.28f),
        });
    }

    sealed class DarkColors : WinForms.ProfessionalColorTable
    {
        static readonly Gdi.Color Back = Gdi.Color.FromArgb(32, 32, 36);
        static readonly Gdi.Color Hover = Gdi.Color.FromArgb(55, 55, 62);
        static readonly Gdi.Color Edge = Gdi.Color.FromArgb(62, 62, 70);
        public override Gdi.Color ToolStripDropDownBackground => Back;
        public override Gdi.Color ImageMarginGradientBegin => Back;
        public override Gdi.Color ImageMarginGradientMiddle => Back;
        public override Gdi.Color ImageMarginGradientEnd => Back;
        public override Gdi.Color MenuBorder => Edge;
        public override Gdi.Color MenuItemBorder => Hover;
        public override Gdi.Color MenuItemSelected => Hover;
        public override Gdi.Color MenuItemSelectedGradientBegin => Hover;
        public override Gdi.Color MenuItemSelectedGradientEnd => Hover;
        public override Gdi.Color SeparatorDark => Edge;
        public override Gdi.Color SeparatorLight => Back;
        public override Gdi.Color CheckBackground => Back;
        public override Gdi.Color CheckSelectedBackground => Hover;
        public override Gdi.Color CheckPressedBackground => Hover;
    }
}
