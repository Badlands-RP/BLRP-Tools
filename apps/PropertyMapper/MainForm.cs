using System.Diagnostics;

namespace BLRP.PropertyMapper;

internal sealed class MainForm : Form
{
    private readonly Label _fileLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label _mapLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
    private readonly MapPreview _preview = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _manifest = new() { Text = "Also create manifest (.ymf)", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly Button _export;
    private readonly Button _preview3d;
    private PropertyMapDocument? _document;

    public MainForm(string? initialPath)
    {
        Text = "BLRP Property Mapper";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 650);
        ClientSize = new Size(1180, 760);
        AllowDrop = true;

        var import = new Button { Text = "IMPORT XML", Dock = DockStyle.Fill };
        import.Click += (_, _) => ImportClicked();
        _export = new Button { Text = "EXPORT YMAP", Dock = DockStyle.Fill, Enabled = false };
        _export.Click += (_, _) => ExportClicked();
        _preview3d = new Button { Text = "OPEN GTA 3D PREVIEW", Dock = DockStyle.Fill, Enabled = false };
        _preview3d.Click += (_, _) => Preview3dClicked();

        BuildGrid();
        Controls.Add(BuildLayout(import));
        BlrpTheme.Apply(this);

        _grid.SelectionChanged += (_, _) =>
        {
            _preview.SelectedIndex = _grid.SelectedRows.Count == 0 ? -1 : _grid.SelectedRows[0].Index;
            _preview.Invalidate();
        };
        _preview.ItemSelected += index =>
        {
            if (index >= 0 && index < _grid.Rows.Count)
            {
                _grid.ClearSelection();
                _grid.Rows[index].Selected = true;
                _grid.FirstDisplayedScrollingRowIndex = index;
            }
        };
        DragEnter += (_, eventArgs) => eventArgs.Effect = HasXmlFile(eventArgs.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, eventArgs) =>
        {
            string? path = (eventArgs.Data?.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault();
            if (path is not null) LoadXml(path);
        };
        Shown += (_, _) =>
        {
            if (initialPath is not null) LoadXml(initialPath);
        };
    }

    private Control BuildLayout(Button import)
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        page.Controls.Add(new Label { Text = "PROPERTY MAPPER\nImport a panel XML, review every object, then export deployment files.", Dock = DockStyle.Fill, Font = new Font("Cascadia Mono", 13F, FontStyle.Bold) }, 0, 0);

        var source = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 5, 0, 5) };
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        source.Controls.Add(import, 0, 0);
        source.Controls.Add(_fileLabel, 1, 0);
        source.Controls.Add(new Label { Text = "MAP NAME", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Cascadia Mono", 9F, FontStyle.Bold) }, 2, 0);
        source.Controls.Add(_mapLabel, 3, 0);
        page.Controls.Add(source, 0, 1);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 760, BackColor = BlrpTheme.Card };
        split.Panel1.Padding = new Padding(0, 0, 6, 0);
        split.Panel2.Padding = new Padding(6, 0, 0, 0);
        split.Panel1.Controls.Add(_grid);
        var previewPane = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        previewPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        previewPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPane.Controls.Add(_preview3d, 0, 0);
        previewPane.Controls.Add(_preview, 0, 1);
        split.Panel2.Controls.Add(previewPane);
        page.Controls.Add(split, 0, 2);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 8, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        footer.Controls.Add(_statusLabel, 0, 0);
        footer.Controls.Add(_manifest, 1, 0);
        footer.Controls.Add(_export, 2, 0);
        page.Controls.Add(footer, 0, 3);
        return page;
    }

    private void BuildGrid()
    {
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Number", HeaderText = "#", Width = 42 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Model", HeaderText = "MODEL", Width = 235 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "X", HeaderText = "X", Width = 86 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Y", HeaderText = "Y", Width = 86 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Z", HeaderText = "Z", Width = 78 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tint", HeaderText = "TINT", Width = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Review", HeaderText = "REVIEW", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 145 });
    }

    private void ImportClicked()
    {
        using var dialog = new OpenFileDialog { Filter = "Property map XML (*.ymap.xml;*.xml)|*.ymap.xml;*.xml|All files (*.*)|*.*", Title = "Import property mapping XML" };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadXml(dialog.FileName);
    }

    private void LoadXml(string path)
    {
        try
        {
            PropertyMapDocument document = PropertyMapDocument.Load(path);
            _document = document;
            _fileLabel.Text = path;
            _fileLabel.ForeColor = Color.White;
            _mapLabel.Text = document.Name;
            _grid.Rows.Clear();
            foreach (MapItem item in document.Items)
            {
                int index = _grid.Rows.Add(item.Number, item.Model, item.X.ToString("0.###"), item.Y.ToString("0.###"), item.Z.ToString("0.###"), item.Tint, item.Review);
                _grid.Rows[index].DefaultCellStyle.ForeColor = item.Level switch
                {
                    ReviewLevel.Error => Color.Salmon,
                    ReviewLevel.Warning => Color.Gold,
                    _ => Color.White
                };
            }
            _preview.SetItems(document.Items);
            _export.Enabled = document.CanExport;
            _preview3d.Enabled = document.CanExport;
            if (document.Errors.Count > 0)
            {
                _statusLabel.Text = "ERROR  " + string.Join("  ", document.Errors);
                _statusLabel.ForeColor = Color.Salmon;
            }
            else
            {
                int errors = document.Items.Count(item => item.Level == ReviewLevel.Error);
                _statusLabel.Text = errors > 0
                    ? $"{document.Items.Count} OBJECTS  /  {errors} ERRORS"
                    : $"{document.Items.Count} OBJECTS  /  {(document.WarningCount == 0 ? "READY TO EXPORT" : $"{document.WarningCount} WARNINGS")}";
                _statusLabel.ForeColor = errors > 0 ? Color.Salmon : document.WarningCount > 0 ? Color.Gold : Color.LightGreen;
            }
        }
        catch (Exception exception)
        {
            _document = null;
            _grid.Rows.Clear();
            _preview.SetItems([]);
            _export.Enabled = false;
            _preview3d.Enabled = false;
            _fileLabel.Text = path;
            _statusLabel.Text = "ERROR  " + exception.Message;
            _statusLabel.ForeColor = Color.Salmon;
            MessageBox.Show(this, exception.Message, "Could not import XML", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportClicked()
    {
        if (_document is null || !_document.CanExport) return;
        string inputName = Path.GetFileName(_document.SourcePath);
        string outputName = inputName.EndsWith(".ymap.xml", StringComparison.OrdinalIgnoreCase)
            ? inputName[..^4]
            : Path.ChangeExtension(inputName, ".ymap");
        using var dialog = new SaveFileDialog
        {
            Filter = "YMAP files (*.ymap)|*.ymap",
            FileName = outputName,
            InitialDirectory = Path.GetDirectoryName(_document.SourcePath),
            Title = "Export deployable YMAP"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _document.BuildYmap());
            string? manifestPath = null;
            if (_manifest.Checked)
            {
                manifestPath = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName) + "_manifest.ymf");
                File.WriteAllBytes(manifestPath, _document.BuildManifest());
            }
            _statusLabel.Text = manifestPath is null ? "EXPORTED  " + dialog.FileName : "EXPORTED YMAP + MANIFEST  " + Path.GetDirectoryName(dialog.FileName);
            _statusLabel.ForeColor = Color.LightGreen;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dialog.FileName}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Preview3dClicked()
    {
        if (_document is null || !_document.CanExport) return;
        string? launcher = FindPreviewLauncher();
        if (launcher is null)
        {
            MessageBox.Show(this, "The CodeWalker preview helper is packaged beside BadWalker. Build the release package, then run Property Mapper from BLRP Tools.", "3D preview unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string previewDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BLRP Tools", "Property Mapper", "Preview");
            Directory.CreateDirectory(previewDirectory);
            string mapName = string.Concat(_document.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            string previewPath = Path.Combine(previewDirectory, mapName + ".ymap");
            File.WriteAllBytes(previewPath, _document.BuildYmap());

            float minX = _document.Items.Min(item => item.X), maxX = _document.Items.Max(item => item.X);
            float minY = _document.Items.Min(item => item.Y), maxY = _document.Items.Max(item => item.Y);
            float minZ = _document.Items.Min(item => item.Z), maxZ = _document.Items.Max(item => item.Z);
            float radius = Math.Max(40f, Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) / 2f + 20f);
            var startInfo = new ProcessStartInfo(launcher) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launcher)! };
            startInfo.ArgumentList.Add(previewPath);
            startInfo.ArgumentList.Add(((minX + maxX) / 2f).ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(((minY + maxY) / 2f).ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(((minZ + maxZ) / 2f).ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(radius.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Process.Start(startInfo);
            _statusLabel.Text = "OPENED GTA 3D PREVIEW  " + previewPath;
            _statusLabel.ForeColor = Color.LightGreen;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not start 3D preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? FindPreviewLauncher()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; directory is not null && depth < 8; depth++, directory = Directory.GetParent(directory)?.FullName)
        {
            foreach (string candidate in new[]
            {
                Path.Combine(directory, "tools", "BadWalker", "BLRP.PropertyMapPreview.exe"),
                Path.Combine(directory, "..", "BadWalker", "BLRP.PropertyMapPreview.exe"),
                Path.Combine(directory, "external", "BadWalker", "bin", "Release", "BLRP.PropertyMapPreview.exe")
            })
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static bool HasXmlFile(IDataObject? data) =>
        (data?.GetData(DataFormats.FileDrop) as string[])?.Any(path => Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase)) == true;
}

internal sealed class MapPreview : Control
{
    private IReadOnlyList<MapItem> _items = [];
    public event Action<int>? ItemSelected;
    public int SelectedIndex { get; set; } = -1;

    public MapPreview()
    {
        DoubleBuffered = true;
        BackColor = BlrpTheme.Input;
        ForeColor = Color.White;
        Cursor = Cursors.Cross;
    }

    public void SetItems(IReadOnlyList<MapItem> items)
    {
        _items = items;
        SelectedIndex = items.Count > 0 ? 0 : -1;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (_items.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "TOP-DOWN PREVIEW\n\nDrop a .ymap.xml here to begin.", Font, ClientRectangle, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        const float padding = 32f;
        float minX = _items.Min(item => item.X), maxX = _items.Max(item => item.X);
        float minY = _items.Min(item => item.Y), maxY = _items.Max(item => item.Y);
        float spanX = Math.Max(10f, maxX - minX), spanY = Math.Max(10f, maxY - minY);
        float centerX = (minX + maxX) / 2f, centerY = (minY + maxY) / 2f;
        float scale = Math.Min(Math.Max(1f, Width - (padding * 2)) / spanX, Math.Max(1f, Height - (padding * 2)) / spanY);
        PointF Point(MapItem item) => new(Width / 2f + ((item.X - centerX) * scale), Height / 2f - ((item.Y - centerY) * scale));

        using var gridPen = new Pen(Color.FromArgb(60, BlrpTheme.AccentLight));
        for (int i = 1; i < 5; i++)
        {
            float x = padding + ((Width - (padding * 2)) * i / 5f);
            float y = padding + ((Height - (padding * 2)) * i / 5f);
            e.Graphics.DrawLine(gridPen, x, padding, x, Height - padding);
            e.Graphics.DrawLine(gridPen, padding, y, Width - padding, y);
        }

        for (int i = 0; i < _items.Count; i++)
        {
            MapItem item = _items[i];
            PointF point = Point(item);
            Color color = item.Level switch { ReviewLevel.Error => Color.Salmon, ReviewLevel.Warning => Color.Gold, _ => BlrpTheme.AccentLight };
            using var pen = new Pen(color, i == SelectedIndex ? 3f : 2f);
            float radius = i == SelectedIndex ? 8f : 6f;
            e.Graphics.DrawEllipse(pen, point.X - radius, point.Y - radius, radius * 2, radius * 2);
            e.Graphics.DrawLine(pen, point, new PointF(point.X + (MathF.Sin(item.Heading) * 18f), point.Y - (MathF.Cos(item.Heading) * 18f)));
            TextRenderer.DrawText(e.Graphics, item.Number.ToString(), Font, new Point((int)point.X + 8, (int)point.Y + 7), color);
        }

        TextRenderer.DrawText(e.Graphics, $"TOP-DOWN  X {minX:0.##}…{maxX:0.##}  Y {minY:0.##}…{maxY:0.##}", Font, new Point(10, 10), Color.LightGray);
        if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
        {
            MapItem selected = _items[SelectedIndex];
            TextRenderer.DrawText(e.Graphics, $"#{selected.Number}  {selected.Model}\n{selected.X:0.###}, {selected.Y:0.###}, {selected.Z:0.###}", Font,
                new Rectangle(10, Height - 48, Width - 20, 42), ForeColor, TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_items.Count == 0) return;
        float minX = _items.Min(item => item.X), maxX = _items.Max(item => item.X);
        float minY = _items.Min(item => item.Y), maxY = _items.Max(item => item.Y);
        float spanX = Math.Max(10f, maxX - minX), spanY = Math.Max(10f, maxY - minY);
        float scale = Math.Min(Math.Max(1f, Width - 64f) / spanX, Math.Max(1f, Height - 64f) / spanY);
        float centerX = (minX + maxX) / 2f, centerY = (minY + maxY) / 2f;
        int nearest = Enumerable.Range(0, _items.Count).OrderBy(index =>
        {
            float x = Width / 2f + ((_items[index].X - centerX) * scale);
            float y = Height / 2f - ((_items[index].Y - centerY) * scale);
            return ((x - e.X) * (x - e.X)) + ((y - e.Y) * (y - e.Y));
        }).First();
        SelectedIndex = nearest;
        ItemSelected?.Invoke(nearest);
        Invalidate();
    }
}
