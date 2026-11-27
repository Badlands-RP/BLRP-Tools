using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BLRP.LiveryPreview;

internal sealed class SceneSettingsForm : Form
{
    private readonly RenderSurface surface;
    private readonly Dictionary<PaintChannel, PaintCard> paintCards = new();
    private readonly CheckedListBox bodies = new() { BorderStyle = BorderStyle.None, CheckOnClick = true, Height = 155 };
    private readonly Dictionary<string, TrackBar> sliders = new();
    private SceneMenuButton? lightingChoice;
    private bool refreshing;

    public SceneSettingsForm(RenderSurface surface)
    {
        this.surface = surface;
        Text = "Scene settings"; Size = new Size(478, 750); MinimumSize = new Size(455, 580);
        ShowIcon = false; MinimizeBox = false; MaximizeBox = false;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(22, 14, 16, 0), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        header.Controls.Add(new Label { Text = "Scene settings", AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold) });
        header.Controls.Add(Note("Make the preview yours. Changes stay in the preview."));
        var tabs = new LiveryTabs { Dock = DockStyle.Fill };
        var paint = Page("Paint"); var lighting = Page("Lighting"); var scene = Page("Scene");
        foreach (PaintChannel channel in Enum.GetValues(typeof(PaintChannel)))
        {
            var card = new PaintCard(channel, surface); paintCards.Add(channel, card); Flow(paint).Controls.Add(card);
        }
        var reset = new LiveryButton { Text = "Reset all paint", Width = 390, Margin = new Padding(0, 4, 0, 12) };
        reset.Click += (_, _) => { surface.ResetPaint(); RefreshVehicle(); };
        Flow(paint).Controls.Add(reset);
        Flow(paint).Controls.Add(Note("Matte, classic, metallic, satin and chrome are studio approximations. Final reflections and paint can differ in FiveM."));
        var light = new SceneCard("Studio lighting");
        var preset = new SceneMenuButton("Preset", new[] { "Studio", "Daylight", "Soft", "Night" }, surface.LightingPreset);
        lightingChoice = preset;
        preset.Chosen += value => { surface.Lighting(value); RefreshLighting(); };
        light.Add(preset);
        AddSlider(light, "Direction", 360, () => (int)(surface.Azimuth * 180 / MathFCompat.Pi), value => surface.Azimuth = value * MathFCompat.Pi / 180);
        AddSlider(light, "Elevation", 90, () => (int)(surface.Elevation * 180 / MathFCompat.Pi), value => surface.Elevation = value * MathFCompat.Pi / 180);
        AddSlider(light, "Intensity", 250, () => (int)(surface.Intensity * 100), value => surface.Intensity = value / 100f);
        AddSlider(light, "Ambient light", 150, () => (int)(surface.Ambient * 100), value => surface.Ambient = value / 100f);
        var shadows = new CheckBox { Text = "Show shadows", Checked = surface.Shadows, AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
        shadows.CheckedChanged += (_, _) => surface.Shadows = shadows.Checked;
        light.Add(shadows); Flow(lighting).Controls.Add(light);
        var background = new SceneCard("Background");
        var backdrop = new SceneMenuButton("Backdrop", new[] { "Dark studio", "Light studio", "Solid colour…", "Image…", "Transparent" }, surface.BackgroundPreset);
        string previous = surface.BackgroundPreset;
        backdrop.Chosen += value =>
        {
            try
            {
                if (value == "Image…")
                {
                    using var dialog = new OpenFileDialog { Filter = "Background image|*.png;*.jpg;*.jpeg;*.bmp" };
                    if (dialog.ShowDialog(this) != DialogResult.OK) { backdrop.SetSelection(previous); return; }
                    surface.SetBackgroundImage(dialog.FileName);
                }
                else
                {
                    Color colour = value == "Light studio" ? Color.FromArgb(195, 201, 211) : LiveryTheme.Background;
                    if (value == "Solid colour…")
                    {
                        using var dialog = new ColorDialog { FullOpen = true, Color = surface.BackgroundColour };
                        if (dialog.ShowDialog(this) != DialogResult.OK) { backdrop.SetSelection(previous); return; }
                        colour = dialog.Color;
                    }
                    surface.SetBackground(colour, value == "Transparent");
                }
                previous = value;
            }
            catch (Exception ex)
            {
                backdrop.SetSelection(previous);
                MessageBox.Show(this, ex.Message, "Could not change background", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        background.Add(backdrop); background.Add(Note("Use a studio backdrop or your own image behind the vehicle.")); Flow(scene).Controls.Add(background);
        var capture = new SceneCard("Screenshots");
        var alpha = new CheckBox { Text = "Transparent PNG background", Checked = surface.ExportTransparent, AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        alpha.CheckedChanged += (_, _) => surface.ExportTransparent = alpha.Checked;
        capture.Add(alpha); capture.Add(Note("Use Save screenshot in the preview toolbar to save the current view.")); Flow(scene).Controls.Add(capture);
        var body = new SceneCard("Body parts"); body.Add(Note("Toggle individual model parts in this preview.")); body.Add(bodies); Flow(scene).Controls.Add(body);
        bodies.ItemCheck += (_, e) => { if (!refreshing) surface.SetBody(e.Index, e.NewValue == CheckState.Checked); };
        tabs.TabPages.AddRange(new[] { paint, lighting, scene });
        Controls.Add(tabs); Controls.Add(header);
        LiveryTheme.Apply(this);
        foreach (SceneCard card in AllControls(this).OfType<SceneCard>()) card.Restyle();
        foreach (Label label in AllControls(this).OfType<Label>().Where(label => Equals(label.Tag, "muted"))) label.ForeColor = LiveryTheme.Muted;
        surface.PaintChannelsChanged += RefreshVehicle;
        RefreshVehicle(); RefreshLighting();
    }

    private static TabPage Page(string name)
    {
        var page = new TabPage(name);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18, 6, 12, 12) };
        flow.Resize += (_, _) =>
        {
            int width = Math.Max(300, flow.ClientSize.Width - 44);
            foreach (Control item in flow.Controls) { item.Width = width; if (item is Label label) label.MaximumSize = new Size(width, 0); }
        };
        page.Controls.Add(flow); return page;
    }
    private static FlowLayoutPanel Flow(TabPage page) => (FlowLayoutPanel)page.Controls[0];
    internal static Label Note(string text) => new() { Text = text, Tag = "muted", AutoSize = true, MaximumSize = new Size(360, 0), Font = new Font("Segoe UI", 9), Margin = new Padding(0, 7, 0, 9) };
    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (Control descendant in AllControls(child)) yield return descendant; }
    }
    private void AddSlider(SceneCard card, string title, int maximum, Func<int> read, Action<int> update)
    {
        var caption = new Label { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var slider = new TrackBar { Minimum = 0, Maximum = maximum, Height = 30, TickStyle = TickStyle.None, AutoSize = false, AccessibleName = title };
        slider.Tag = read; sliders.Add(title, slider);
        slider.ValueChanged += (_, _) => { caption.Text = title + "  ·  " + slider.Value; if (!refreshing) { update(slider.Value); surface.LightingPreset = "Custom"; lightingChoice?.SetSelection("Custom"); } };
        card.Add(caption); card.Add(slider);
        refreshing = true;
        try { slider.Value = Math.Max(0, Math.Min(maximum, read())); caption.Text = title + "  ·  " + slider.Value; }
        finally { refreshing = false; }
    }
    private void RefreshLighting()
    {
        refreshing = true;
        try { foreach (var slider in sliders.Values) slider.Value = Math.Max(0, Math.Min(slider.Maximum, ((Func<int>)slider.Tag)())); }
        finally { refreshing = false; }
    }
    public void RefreshVehicle()
    {
        foreach (var card in paintCards.Values) card.RefreshPaint();
        refreshing = true;
        try { bodies.Items.Clear(); foreach (string name in surface.BodyNames) bodies.Items.Add(name, true); }
        finally { refreshing = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) surface.PaintChannelsChanged -= RefreshVehicle;
        base.Dispose(disposing);
    }

    private sealed class PaintCard : SceneCard
    {
        private readonly PaintChannel channel;
        private readonly RenderSurface surface;
        private readonly ColourButton colour = new();
        private readonly SceneMenuButton finish;
        private readonly Label availability = Note("");
        private readonly LiveryButton reset = new() { Text = "Reset this paint layer" };
        public PaintCard(PaintChannel channel, RenderSurface surface) : base(channel + " paint")
        {
            this.channel = channel; this.surface = surface;
            finish = new SceneMenuButton("Finish", Enum.GetNames(typeof(PaintFinish)).Select(name => name == "Original" ? "Model default" : name).ToArray(), "Model default");
            colour.AccessibleName = channel + " colour";
            colour.Click += (_, _) =>
            {
                using var dialog = new ColorDialog { FullOpen = true, Color = surface.GetPaint(channel).Colour ?? Color.White };
                if (dialog.ShowDialog(this) == DialogResult.OK) { surface.SetPaint(channel, dialog.Color, surface.GetPaint(channel).Finish); RefreshPaint(); }
            };
            finish.Chosen += value => { surface.SetPaint(channel, surface.GetPaint(channel).Colour, value == "Model default" ? PaintFinish.Original : (PaintFinish)Enum.Parse(typeof(PaintFinish), value)); RefreshPaint(); };
            reset.Click += (_, _) => { surface.SetPaint(channel, null, PaintFinish.Original); RefreshPaint(); };
            Add(availability); Add(colour); Add(finish); Add(reset);
        }
        public void RefreshPaint()
        {
            int count = surface.PaintMaterialCount(channel);
            availability.Text = count == 0 ? "This model has no " + channel.ToString().ToLowerInvariant() + " paint layer." : "Applies to this model's " + channel.ToString().ToLowerInvariant() + " paint surfaces.";
            colour.Enabled = finish.Enabled = reset.Enabled = count > 0;
            PaintSelection selection = surface.GetPaint(channel);
            colour.Colour = selection.Colour; colour.Invalidate();
            finish.SetSelection(selection.Finish == PaintFinish.Original ? "Model default" : selection.Finish.ToString());
        }
    }
}

internal class SceneCard : FlowLayoutPanel
{
    public SceneCard(string title)
    {
        Width = 390; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; MinimumSize = new Size(300, 0);
        FlowDirection = FlowDirection.TopDown; WrapContents = false; Padding = new Padding(16); Margin = new Padding(0, 0, 0, 14);
        Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 3) });
        SizeChanged += (_, _) => FitChildren();
    }
    public void Add(Control control) { control.Margin = new Padding(0, control.Margin.Top, 0, Math.Max(7, control.Margin.Bottom)); Controls.Add(control); FitChildren(); }
    private void FitChildren()
    {
        foreach (Control child in Controls)
        {
            int width = Math.Max(250, Width - Padding.Horizontal);
            if (child is Label label) label.MaximumSize = new Size(width, 0); else child.Width = width;
        }
    }
    public void Restyle() { BackColor = LiveryTheme.Surface; foreach (Control control in Controls) if (control is TrackBar) control.BackColor = LiveryTheme.Surface; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? LiveryTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = SceneShapes.Rounded(new Rectangle(1, 1, Width - 3, Height - 3));
        using var brush = new SolidBrush(LiveryTheme.Surface); e.Graphics.FillPath(brush, shape);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var pen = new Pen(LiveryTheme.Border);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = SceneShapes.Rounded(new Rectangle(1, 1, Width - 3, Height - 3)); e.Graphics.DrawPath(pen, shape);
    }
}

internal sealed class SceneMenuButton : Button
{
    private readonly string caption;
    private readonly ContextMenuStrip menu = new() { ShowImageMargin = false, BackColor = LiveryTheme.Surface, ForeColor = LiveryTheme.Text };
    public event Action<string>? Chosen;
    public SceneMenuButton(string caption, string[] values, string initial)
    {
        this.caption = caption; Height = 40; FlatStyle = FlatStyle.Flat; TextAlign = ContentAlignment.MiddleLeft;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColours());
        foreach (string value in values)
        {
            var item = new ToolStripMenuItem(value) { ForeColor = LiveryTheme.Text, Padding = new Padding(10, 7, 10, 7) };
            item.Click += (_, _) => { SetSelection(value); Chosen?.Invoke(value); }; menu.Items.Add(item);
        }
        Click += (_, _) => menu.Show(this, new Point(0, Height)); SetSelection(initial);
    }
    public void SetSelection(string value) { Text = caption + "  ·  " + value + "   ▾"; foreach (ToolStripMenuItem item in menu.Items) item.Checked = item.Text == value; }
    protected override void OnPaint(PaintEventArgs e)
    {
        SceneShapes.Button(e, this);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(14, 0, Width - 28, Height), Enabled ? LiveryTheme.Text : LiveryTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing) { if (disposing) menu.Dispose(); base.Dispose(disposing); }
    private sealed class MenuColours : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => LiveryTheme.Surface;
        public override Color MenuItemSelected => LiveryTheme.Input;
        public override Color MenuItemBorder => LiveryTheme.Border;
        public override Color MenuBorder => LiveryTheme.Border;
        public override Color CheckBackground => LiveryTheme.Input;
        public override Color CheckSelectedBackground => LiveryTheme.Border;
    }
}

internal sealed class ColourButton : Button
{
    public Color? Colour;
    public ColourButton() { Height = 54; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e)
    {
        SceneShapes.Button(e, this);
        using (var brush = new SolidBrush(Colour ?? Color.White)) e.Graphics.FillEllipse(brush, 13, 12, 28, 28);
        string value = Colour.HasValue ? $"#{Colour.Value.R:X2}{Colour.Value.G:X2}{Colour.Value.B:X2}" : "Model default";
        TextRenderer.DrawText(e.Graphics, "Colour  ·  " + value, Font, new Rectangle(54, 0, Width - 65, Height), Enabled ? LiveryTheme.Text : LiveryTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal static class SceneShapes
{
    public static GraphicsPath Rounded(Rectangle bounds)
    {
        var shape = new GraphicsPath(); const int radius = 14;
        shape.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
        shape.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
        shape.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        shape.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90); shape.CloseFigure(); return shape;
    }
    public static void Button(PaintEventArgs e, Button button)
    {
        e.Graphics.Clear(button.Parent?.BackColor ?? LiveryTheme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Rounded(new Rectangle(1, 1, button.Width - 3, button.Height - 3));
        using (var brush = new SolidBrush(button.Enabled ? LiveryTheme.Input : LiveryTheme.Surface)) e.Graphics.FillPath(brush, shape);
        using var pen = new Pen(button.Focused ? LiveryTheme.Accent : LiveryTheme.Border); e.Graphics.DrawPath(pen, shape);
    }
}
