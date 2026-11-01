using SkiaSharp;

namespace BLRP.LoadingScreenTool;

internal sealed class CleanupTab : UserControl
{
    private readonly Func<string> _projectFolder;
    private readonly ListBox _images = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended,
        BackColor = Color.FromArgb(25, 25, 52), ForeColor = Color.White };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 212,
        BackColor = Color.FromArgb(25, 25, 52), ForeColor = Color.White };
    private readonly NumericUpDown _brushSize = new() { Minimum = 2, Maximum = 30, Value = 8, Width = 48,
        BackColor = Color.FromArgb(25, 25, 52), ForeColor = Color.White };
    private readonly Button _previewButton = ActionButton("PREVIEW REMOVAL", 154);
    private readonly Button _saveButton = ActionButton("SAVE CHANGES", 136);
    private readonly Button _resetButton = ActionButton("CLEAR MASK", 116);
    private readonly Button _originalButton = ActionButton("SHOW ORIGINAL", 140);
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.LightSkyBlue };
    private readonly TextBox _changes = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(25, 25, 52), ForeColor = Color.White };
    private readonly List<MaskCircle> _painted = [];
    private readonly Dictionary<string, LoadingScreenWorkflow.PreviewEdit> _pending = new(StringComparer.OrdinalIgnoreCase);
    private SKRect? _rectangle;
    private Point? _dragStart;
    private bool _painting;
    private bool _showOriginal;

    internal CleanupTab(Func<string> projectFolder)
    {
        _projectFolder = projectFolder;
        BackColor = Color.FromArgb(12, 12, 28);
        ForeColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2,
            RowCount = 4, BackColor = BackColor, ForeColor = ForeColor };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        var refresh = ActionButton("REFRESH IMAGES", 0);
        refresh.Dock = DockStyle.Fill;
        refresh.Click += (_, _) => RefreshImages();
        layout.Controls.Add(refresh, 0, 0);
        layout.Controls.Add(new Label { Text = "Paint only over the UI element. Preview, compare with the original, then Save before exporting.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        layout.Controls.Add(_images, 0, 1);
        layout.Controls.Add(_preview, 1, 1);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, BackColor = BackColor, ForeColor = ForeColor };
        _mode.Items.AddRange(["Brush (recommended)", "FiveM lower right", "FPS upper left", "FPS upper right", "Custom rectangle"]);
        _mode.SelectedIndex = 0;
        controls.Controls.Add(_mode);
        controls.Controls.Add(new Label { Text = "Brush px", ForeColor = Color.Gainsboro, AutoSize = true,
            Margin = new Padding(8, 8, 2, 0) });
        controls.Controls.Add(_brushSize);
        controls.Controls.Add(_previewButton);
        controls.Controls.Add(_originalButton);
        controls.Controls.Add(_saveButton);
        controls.Controls.Add(_resetButton);
        controls.Controls.Add(_status);
        layout.Controls.Add(controls, 0, 2);
        layout.SetColumnSpan(controls, 2);
        layout.Controls.Add(_changes, 0, 3);
        layout.SetColumnSpan(_changes, 2);
        Controls.Add(layout);

        _saveButton.Enabled = false;
        _originalButton.Enabled = false;
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _painted.Clear();
            _rectangle = _mode.SelectedIndex switch
            {
                1 => new SKRect(0.86f, 0.93f, 0.99f, 0.99f),
                2 => new SKRect(0.01f, 0.01f, 0.14f, 0.07f),
                3 => new SKRect(0.86f, 0.01f, 0.99f, 0.07f),
                _ => null
            };
            InvalidatePending();
        };
        _images.SelectedIndexChanged += (_, _) => LoadPreview();
        _preview.Paint += PreviewPaint;
        _preview.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || _preview.Image is null) return;
            _dragStart = e.Location;
            if (_mode.SelectedIndex == 0) { _painting = true; PaintAt(e.Location); }
        };
        _preview.MouseMove += (_, e) =>
        {
            if (_painting) PaintAt(e.Location);
            else if (_dragStart.HasValue && _mode.SelectedIndex == 4) SetRectangle(_dragStart.Value, e.Location);
        };
        _preview.MouseUp += (_, e) =>
        {
            if (_dragStart.HasValue && _mode.SelectedIndex == 4) SetRectangle(_dragStart.Value, e.Location);
            _dragStart = null;
            _painting = false;
        };
        _previewButton.Click += async (_, _) => await PreviewAsync();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _resetButton.Click += (_, _) => { _painted.Clear(); _rectangle = null; InvalidatePending(); };
        _originalButton.Click += (_, _) =>
        {
            _showOriginal = !_showOriginal;
            _originalButton.Text = _showOriginal ? "SHOW EDIT" : "SHOW ORIGINAL";
            LoadPreview();
        };
    }

    internal void RefreshImages()
    {
        _pending.Clear();
        _painted.Clear();
        _rectangle = null;
        _saveButton.Enabled = _originalButton.Enabled = false;
        _images.Items.Clear();
        _preview.Image?.Dispose();
        _preview.Image = null;
        try
        {
            foreach (string path in LoadingScreenWorkflow.ProjectImages(_projectFolder())) _images.Items.Add(Path.GetFileName(path));
            _status.Text = _images.Items.Count + " numbered WebPs. Select one or more.";
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void LoadPreview()
    {
        _preview.Image?.Dispose();
        _preview.Image = null;
        if (_images.SelectedItem is not string name) return;
        try
        {
            byte[] png;
            if (!_showOriginal && _pending.TryGetValue(name, out var edit)) png = edit.Png;
            else
            {
                using SKBitmap bitmap = SKBitmap.Decode(Path.Combine(_projectFolder(), name)) ??
                    throw new InvalidDataException("Could not read " + name);
                using SKImage image = SKImage.FromBitmap(bitmap);
                using SKData data = image.Encode(SKEncodedImageFormat.Png, 100) ??
                    throw new InvalidDataException("Could not display " + name);
                png = data.ToArray();
            }
            using var stream = new MemoryStream(png);
            using var decoded = Image.FromStream(stream);
            _preview.Image = new Bitmap(decoded);
            _preview.Invalidate();
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private RectangleF ImageBounds()
    {
        if (_preview.Image is null) return RectangleF.Empty;
        float scale = Math.Min((float)_preview.Width / _preview.Image.Width, (float)_preview.Height / _preview.Image.Height);
        float width = _preview.Image.Width * scale, height = _preview.Image.Height * scale;
        return new RectangleF((_preview.Width - width) / 2f, (_preview.Height - height) / 2f, width, height);
    }

    private void PaintAt(Point point)
    {
        RectangleF bounds = ImageBounds();
        if (!bounds.Contains(point)) return;
        float x = (point.X - bounds.Left) / bounds.Width;
        float y = (point.Y - bounds.Top) / bounds.Height;
        float radius = (float)_brushSize.Value / bounds.Width;
        if (_painted.Count > 0)
        {
            MaskCircle prior = _painted[^1];
            if (Math.Abs((prior.X - x) * bounds.Width) < 2 && Math.Abs((prior.Y - y) * bounds.Height) < 2) return;
        }
        _painted.Add(new MaskCircle(x, y, radius));
        InvalidatePending();
    }

    private void SetRectangle(Point start, Point end)
    {
        RectangleF bounds = ImageBounds();
        if (bounds.IsEmpty) return;
        float x1 = Math.Clamp((start.X - bounds.Left) / bounds.Width, 0, 1);
        float y1 = Math.Clamp((start.Y - bounds.Top) / bounds.Height, 0, 1);
        float x2 = Math.Clamp((end.X - bounds.Left) / bounds.Width, 0, 1);
        float y2 = Math.Clamp((end.Y - bounds.Top) / bounds.Height, 0, 1);
        _rectangle = new SKRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
        InvalidatePending();
    }

    private void PreviewPaint(object? sender, PaintEventArgs e)
    {
        RectangleF bounds = ImageBounds();
        if (bounds.IsEmpty || _showOriginal || (_images.SelectedItem is string name && _pending.ContainsKey(name))) return;
        using var fill = new SolidBrush(Color.FromArgb(90, Color.OrangeRed));
        using var outline = new Pen(Color.OrangeRed, 2);
        if (_rectangle is SKRect rect)
        {
            var area = new RectangleF(bounds.Left + rect.Left * bounds.Width, bounds.Top + rect.Top * bounds.Height,
                rect.Width * bounds.Width, rect.Height * bounds.Height);
            e.Graphics.FillRectangle(fill, area);
            e.Graphics.DrawRectangle(outline, area.X, area.Y, area.Width, area.Height);
        }
        foreach (MaskCircle circle in _painted)
        {
            float radius = circle.Radius * bounds.Width;
            e.Graphics.FillEllipse(fill, bounds.Left + circle.X * bounds.Width - radius,
                bounds.Top + circle.Y * bounds.Height - radius, radius * 2, radius * 2);
        }
    }

    private void InvalidatePending()
    {
        if (_pending.Count > 0)
        {
            _pending.Clear();
            _saveButton.Enabled = _originalButton.Enabled = false;
            _showOriginal = false;
            _originalButton.Text = "SHOW ORIGINAL";
            _changes.Clear();
            LoadPreview();
        }
        _status.Text = "Mask ready. Click Preview Removal; no files have changed.";
        _preview.Invalidate();
    }

    private async Task PreviewAsync()
    {
        string[] names = _images.SelectedItems.Cast<string>().ToArray();
        if (names.Length == 0 || (_rectangle is null && _painted.Count == 0))
        {
            _status.Text = "Select images and paint a small mask over the overlay.";
            return;
        }
        var mask = new OverlayMask(_rectangle, _painted.ToArray());
        string folder = _projectFolder();
        _previewButton.Enabled = _saveButton.Enabled = false;
        _status.Text = "Generating previews...";
        try
        {
            var edits = await Task.Run(() => LoadingScreenWorkflow.PreviewRemoval(folder, names, mask));
            _pending.Clear();
            foreach (var edit in edits) _pending.Add(edit.Name, edit);
            _showOriginal = false;
            _originalButton.Enabled = _saveButton.Enabled = true;
            _changes.Text = string.Join(Environment.NewLine, edits.Select(edit => "Preview: " + edit.Name));
            _status.Text = $"Previewed {edits.Count} images. Compare, then Save Changes.";
            LoadPreview();
        }
        catch (Exception error) { _status.Text = "Preview failed: " + error.Message; }
        finally { _previewButton.Enabled = true; }
    }

    private async Task SaveAsync()
    {
        var edits = _pending.Values.ToArray();
        if (edits.Length == 0) return;
        _saveButton.Enabled = _previewButton.Enabled = false;
        _status.Text = "Saving reviewed images...";
        try
        {
            var saved = await Task.Run(() => LoadingScreenWorkflow.SaveRemoval(_projectFolder(), edits));
            _pending.Clear();
            _painted.Clear();
            _rectangle = null;
            _showOriginal = false;
            _originalButton.Text = "SHOW ORIGINAL";
            _originalButton.Enabled = false;
            _changes.Text = string.Join(Environment.NewLine, saved.Select(name => "Saved: " + name));
            _status.Text = $"Saved {saved.Count} images. Originals are in .blrp-image-backups.";
            LoadPreview();
        }
        catch (Exception error) { _status.Text = "Save failed: " + error.Message; _saveButton.Enabled = true; }
        finally { _previewButton.Enabled = true; }
    }

    private static Button ActionButton(string text, int width) => new()
    {
        Text = text, Width = width, Height = 36, BackColor = Color.FromArgb(100, 149, 237),
        ForeColor = Color.White, FlatStyle = FlatStyle.Flat
    };
}
