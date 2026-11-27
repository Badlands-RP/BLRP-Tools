using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Shared only by the livery workspace and its embedded preview process.
internal static class LiveryTheme
{
    public static readonly Color Background = Color.FromArgb(16, 20, 26);
    public static readonly Color Surface = Color.FromArgb(23, 29, 37);
    public static readonly Color Input = Color.FromArgb(33, 41, 52);
    public static readonly Color Border = Color.FromArgb(48, 59, 73);
    public static readonly Color Text = Color.FromArgb(232, 237, 244);
    public static readonly Color Muted = Color.FromArgb(150, 163, 182);
    public static readonly Color Accent = Color.FromArgb(103, 207, 192);

    public static void Apply(Control root)
    {
        root.Font = new Font("Segoe UI", 10f);
        root.BackColor = Background;
        root.ForeColor = Text;
        StyleChildren(root);
        if (root is Form form)
        {
            form.HandleCreated += (_, _) => DarkWindow(form.Handle);
            if (form.IsHandleCreated) DarkWindow(form.Handle);
        }
    }

    private static void StyleChildren(Control root)
    {
        foreach (Control control in root.Controls)
        {
            control.Font = new Font("Segoe UI", control.Font.Size, control.Font.Style);
            control.ForeColor = Text;
            switch (control)
            {
                case TextBoxBase text:
                    text.BackColor = Input;
                    text.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox combo:
                    combo.BackColor = Input;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.ItemHeight = 25;
                    combo.DrawItem += (_, e) =>
                    {
                        using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Border : Input)) e.Graphics.FillRectangle(brush, e.Bounds);
                        string value = (e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text) ?? "";
                        TextRenderer.DrawText(e.Graphics, value, combo.Font, e.Bounds, Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    };
                    break;
                case ListBox list:
                    list.BackColor = Surface;
                    list.BorderStyle = BorderStyle.None;
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = Surface;
                    grid.BorderStyle = BorderStyle.None;
                    grid.GridColor = Border;
                    grid.EnableHeadersVisualStyles = false;
                    grid.RowHeadersVisible = false;
                    grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                    grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
                    grid.ColumnHeadersHeight = 52;
                    grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                    grid.DefaultCellStyle.BackColor = Surface;
                    grid.DefaultCellStyle.ForeColor = Text;
                    grid.DefaultCellStyle.SelectionBackColor = Input;
                    grid.DefaultCellStyle.SelectionForeColor = Text;
                    grid.DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
                    grid.RowTemplate.Height = 52;
                    grid.AllowUserToResizeRows = false;
                    break;
                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.BackColor = Input;
                    button.ForeColor = Text;
                    button.Padding = new Padding(10, 4, 10, 4);
                    button.Cursor = Cursors.Hand;
                    break;
                case TabControl tabs:
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tabs.SizeMode = TabSizeMode.Fixed;
                    tabs.ItemSize = new Size(145, 40);
                    tabs.DrawItem += (_, e) =>
                    {
                        bool selected = tabs.SelectedIndex == e.Index;
                        using (var brush = new SolidBrush(selected ? Surface : Background)) e.Graphics.FillRectangle(brush, e.Bounds);
                        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, selected ? Accent : Muted,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        if (selected) using (var pen = new Pen(Accent, 2)) e.Graphics.DrawLine(pen, e.Bounds.Left + 16, e.Bounds.Bottom - 2, e.Bounds.Right - 16, e.Bounds.Bottom - 2);
                    };
                    break;
                case Label:
                case CheckBox:
                    control.BackColor = Color.Transparent;
                    break;
                default:
                    control.BackColor = Background;
                    break;
            }
            if (control is TextBoxBase || control is ListBox || control is DataGridView || control is ComboBox || control is TreeView || control is ScrollableControl)
            {
                control.HandleCreated += (_, _) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
                if (control.IsHandleCreated) SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            if (control is not DataGridView) StyleChildren(control);
        }
    }

    private static void DarkWindow(IntPtr handle)
    {
        int enabled = 1;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        int rounded = 2;
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr handle, string app, string? id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}

internal sealed class LiveryButton : Button
{
    public bool Primary { get; set; }
    public bool Selected { get; set; }
    private bool hovered;
    public LiveryButton()
    {
        AutoSize = true;
        MinimumSize = new Size(80, 38);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? LiveryTheme.Background);
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var shape = new GraphicsPath();
        const int diameter = 16;
        shape.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        shape.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        shape.CloseFigure();
        Color fill = !Enabled ? LiveryTheme.Surface : Primary ? LiveryTheme.Accent : hovered ? LiveryTheme.Border : Selected ? LiveryTheme.Surface : LiveryTheme.Input;
        using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
        if (Focused) using (var pen = new Pen(LiveryTheme.Accent)) e.Graphics.DrawPath(pen, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, !Enabled ? LiveryTheme.Muted : Primary ? LiveryTheme.Background : Selected ? LiveryTheme.Accent : LiveryTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// A lightweight page switcher avoids the native tab control's unthemeable white strip.
internal sealed class LiveryTabs : UserControl
{
    private readonly FlowLayoutPanel navigation = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8, 4, 8, 8), WrapContents = false };
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly List<TabPage> pages = new();
    private readonly List<Panel> panels = new();
    private readonly List<LiveryButton> buttons = new();
    private int selectedIndex = -1;
    public LiveryTabs() { Controls.Add(content); Controls.Add(navigation); TabPages = new PageCollection(this); }
    public PageCollection TabPages { get; }
    public event EventHandler? SelectedIndexChanged;
    public TabPage? SelectedTab => selectedIndex < 0 ? null : pages[selectedIndex];
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= pages.Count || value == selectedIndex) return;
            selectedIndex = value;
            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].Visible = i == value;
                buttons[i].Selected = i == value;
                buttons[i].Invalidate();
            }
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public sealed class PageCollection
    {
        private readonly LiveryTabs owner;
        internal PageCollection(LiveryTabs owner) => this.owner = owner;
        public void AddRange(TabPage[] added)
        {
            foreach (TabPage page in added)
            {
                int index = owner.pages.Count;
                var panel = new Panel { Dock = DockStyle.Fill, Padding = page.Padding, Visible = false };
                Control[] children = page.Controls.Cast<Control>().ToArray();
                // Keep docking z-order when transferring the page's controls.
                panel.Controls.AddRange(children);
                owner.content.Controls.Add(panel);
                owner.panels.Add(panel); owner.pages.Add(page);
                var button = new LiveryButton { Text = page.Text, MinimumSize = new Size(125, 38) };
                button.Click += (_, _) => owner.SelectedIndex = index;
                owner.buttons.Add(button); owner.navigation.Controls.Add(button);
            }
            if (owner.SelectedIndex < 0 && owner.pages.Count > 0) owner.SelectedIndex = 0;
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (TabPage page in pages) page.Dispose();
        base.Dispose(disposing);
    }
}
