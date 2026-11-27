using System.Drawing.Drawing2D;

namespace Badlands.LiveryTool;

internal sealed record LiveryChoice(string Key, string Title, string Detail, string Badge, ExistingLivery? Existing = null, bool Pending = false)
{
    public static LiveryChoice From(ExistingLivery livery, bool replacing = false) => new(livery.Key, livery.DisplayName,
        $"Slot {livery.Slot}  ·  {livery.Model}.yft  ·  {livery.Kit.Name}",
        livery.Paths.Length > 1 ? "Duplicate files" : livery.Paths.Length == 0 ? "Preview only" : replacing ? "Replaceable" : "Repository", livery);
    public override string ToString() => $"{Title} · {Detail} · {Badge}";
}

// Both selectors share the same searchable, keyboard-accessible popup and two-line rows.
internal sealed class LiveryPicker : UserControl
{
    private readonly PickerFace face = new() { Dock = DockStyle.Fill };
    private readonly ToolStripDropDown popup = new() { AutoSize = false, Padding = new Padding(1), Margin = Padding.Empty, BackColor = LiveryTheme.Border };
    private readonly Panel content = new() { BackColor = LiveryTheme.Surface, Padding = new Padding(10) };
    private readonly TextBox search = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Search name, slot or filename…", BackColor = LiveryTheme.Input, ForeColor = LiveryTheme.Text };
    private readonly Label count = new() { Dock = DockStyle.Top, Height = 28, ForeColor = LiveryTheme.Muted };
    private readonly Label empty = new() { Dock = DockStyle.Fill, ForeColor = LiveryTheme.Muted, TextAlign = ContentAlignment.MiddleCenter, Text = "No matching liveries" };
    private readonly ListBox list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 60,
        BackColor = LiveryTheme.Surface, ForeColor = LiveryTheme.Text, AccessibleName = "Livery choices" };
    private readonly ToolStripControlHost host;
    private LiveryChoice[] choices = [];
    private LiveryChoice? selected;
    public IReadOnlyList<LiveryChoice> Choices => choices;
    public LiveryChoice? SelectedChoice => selected;
    public string Context { get; set; } = "Liveries";
    public string Placeholder { get => face.Placeholder; set { face.Placeholder = value; face.Invalidate(); } }
    public event Action? SelectionChanged;

    public LiveryPicker()
    {
        Height = 64;
        MinimumSize = new Size(220, 64);
        Margin = new Padding(0, 6, 0, 10);
        Controls.Add(face);
        host = new ToolStripControlHost(content) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        popup.Items.Add(host);
        var searchArea = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = LiveryTheme.Surface };
        searchArea.Controls.Add(search); searchArea.Controls.Add(count);
        content.Controls.Add(list); content.Controls.Add(empty); content.Controls.Add(searchArea);
        LiveryTheme.Apply(content);
        content.BackColor = searchArea.BackColor = LiveryTheme.Surface;
        face.Click += (_, _) => ShowChoices();
        face.KeyDown += (_, e) => { if (e.KeyCode is Keys.Down or Keys.Up) { ShowChoices(); e.Handled = true; } };
        search.TextChanged += (_, _) => Filter();
        search.KeyDown += Navigate;
        list.KeyDown += Navigate;
        list.MouseClick += (_, e) => { int index = list.IndexFromPoint(e.Location); if (index >= 0) Commit((LiveryChoice)list.Items[index]); };
        list.DrawItem += DrawChoice;
    }

    public void SetChoices(IEnumerable<LiveryChoice> values, string? selectedKey)
    {
        choices = values.ToArray();
        SetSelected(choices.FirstOrDefault(choice => choice.Key == selectedKey));
        if (popup.Visible) Filter();
    }

    public void Select(string key)
    {
        LiveryChoice choice = choices.Single(choice => choice.Key == key);
        if (selected?.Key == choice.Key) return;
        SetSelected(choice); SelectionChanged?.Invoke();
    }

    private void SetSelected(LiveryChoice? value)
    {
        selected = value; face.Choice = value;
        face.AccessibleName = AccessibleName ?? "Select livery";
        face.AccessibleDescription = value?.ToString() ?? Placeholder;
        face.Text = value?.Title ?? Placeholder;
        face.Invalidate();
    }

    private void ShowChoices()
    {
        if (popup.Visible) { popup.Close(); return; }
        var area = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(Math.Max(Width, 620), Math.Max(220, area.Width - 24));
        int height = PopupHeight(choices.Length);
        popup.Size = new Size(width, height);
        host.Size = new Size(width - 2, height - 2); content.Size = host.Size;
        search.Font = list.Font = count.Font = Font;
        search.Text = ""; Filter();
        Point position = PointToScreen(new Point(0, Height + 4));
        position.X = Math.Clamp(position.X, area.Left + 8, Math.Max(area.Left + 8, area.Right - width - 8));
        if (position.Y + height > area.Bottom) position.Y = Math.Max(area.Top + 8, PointToScreen(Point.Empty).Y - height - 4);
        popup.Show(position);
        search.Focus();
    }

    private void Filter()
    {
        string query = search.Text.Trim();
        LiveryChoice[] matches = choices.Where(choice => choice.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(matches);
        list.SelectedItem = matches.FirstOrDefault(choice => choice.Key == selected?.Key) ?? matches.FirstOrDefault();
        list.EndUpdate();
        count.Text = $"{Context}  ·  {matches.Length} of {choices.Length}";
        empty.Text = choices.Length == 0 ? "No livery slots found for this vehicle" : "No matching liveries";
        empty.Visible = matches.Length == 0;
        if (empty.Visible) empty.BringToFront(); else list.BringToFront();
        if (popup.Visible)
        {
            int height = PopupHeight(matches.Length);
            popup.Height = height;
            host.Height = content.Height = height - 2;
        }
    }

    private int PopupHeight(int matches) => Math.Min(Math.Min(520, Math.Max(180, Screen.FromControl(this).WorkingArea.Height - 80)), Math.Max(180, 88 + Math.Min(matches, 7) * list.ItemHeight));

    private void Navigate(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { popup.Close(); face.Focus(); }
        else if (e.KeyCode == Keys.Enter && list.SelectedItem is LiveryChoice choice) Commit(choice);
        else if (sender == search && e.KeyCode is Keys.Down or Keys.Up)
        {
            if (list.Items.Count > 0) list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, list.Items.Count - 1);
        }
        else return;
        e.Handled = true; e.SuppressKeyPress = true;
    }

    private void Commit(LiveryChoice choice)
    {
        popup.Close(); face.Focus(); Select(choice.Key);
    }

    private void DrawChoice(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var choice = (LiveryChoice)list.Items[e.Index];
        bool active = (e.State & DrawItemState.Selected) != 0;
        using (var brush = new SolidBrush(active ? LiveryTheme.Input : LiveryTheme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
        if (choice.Key == selected?.Key) using (var brush = new SolidBrush(LiveryTheme.Accent)) e.Graphics.FillRectangle(brush, e.Bounds.Left, e.Bounds.Top + 9, 3, e.Bounds.Height - 18);
        PaintChoice(e.Graphics, e.Bounds, choice, list.Font, false);
    }

    private static void PaintChoice(Graphics graphics, Rectangle bounds, LiveryChoice choice, Font font, bool arrow)
    {
        using var title = new Font(font.FontFamily, font.Size, FontStyle.Bold);
        using var detail = new Font(font.FontFamily, Math.Max(8, font.Size - 1));
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        int inset = 14, right = arrow ? 30 : 10;
        int badgeWidth = TextRenderer.MeasureText(choice.Badge, detail).Width + 18;
        var badge = new Rectangle(bounds.Right - right - badgeWidth, bounds.Top + 10, badgeWidth, 23);
        using (var brush = new SolidBrush(LiveryTheme.Border)) graphics.FillRectangle(brush, badge);
        Color badgeColor = choice.Existing?.CanReplace == false ? LiveryTheme.Muted : LiveryTheme.Accent;
        TextRenderer.DrawText(graphics, choice.Badge, detail, badge, badgeColor, flags | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(graphics, choice.Title, title, new Rectangle(bounds.Left + inset, bounds.Top + 7, Math.Max(10, badge.Left - bounds.Left - inset - 8), 25), LiveryTheme.Text, flags);
        TextRenderer.DrawText(graphics, choice.Detail, detail, new Rectangle(bounds.Left + inset, bounds.Top + 32, bounds.Width - inset - right, 21), LiveryTheme.Muted, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) popup.Dispose();
        base.Dispose(disposing);
    }

    private sealed class PickerFace : Button
    {
        public LiveryChoice? Choice;
        public string Placeholder = "Choose a livery…";
        private bool hovered;
        public PickerFace()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Cursor = Cursors.Hand;
            MouseEnter += (_, _) => { hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { hovered = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? LiveryTheme.Background);
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using var shape = new GraphicsPath();
            const int radius = 14;
            shape.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
            shape.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
            shape.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
            shape.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90); shape.CloseFigure();
            using (var brush = new SolidBrush(!Enabled ? LiveryTheme.Surface : hovered ? LiveryTheme.Border : LiveryTheme.Input)) e.Graphics.FillPath(brush, shape);
            using (var pen = new Pen(Focused ? LiveryTheme.Accent : LiveryTheme.Border)) e.Graphics.DrawPath(pen, shape);
            if (Choice != null) PaintChoice(e.Graphics, bounds, Choice, Font, true);
            else TextRenderer.DrawText(e.Graphics, Placeholder, Font, new Rectangle(14, 0, Width - 45, Height), LiveryTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using var arrow = new Pen(Enabled ? LiveryTheme.Muted : LiveryTheme.Border, 2);
            e.Graphics.DrawLines(arrow, new Point[] { new(Width - 23, Height / 2 - 2), new(Width - 18, Height / 2 + 3), new(Width - 13, Height / 2 - 2) });
        }
    }
}
