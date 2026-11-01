using SkiaSharp;

namespace BLRP.LoadingScreenTool;

internal sealed class CleanupTab : UserControl
{
    private readonly Func<string> _projectFolder;
    private readonly ListBox _images = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 215 };
    private readonly Button _apply = new() { Text = "REMOVE SELECTED AREA", Width = 190 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.LightSkyBlue };
    private readonly TextBox _changes = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(25, 25, 52), ForeColor = Color.White };
    private SKRect _selection = new(0.84f, 0.91f, 0.99f, 0.99f);
    private Point? _dragStart;

    internal CleanupTab(Func<string> projectFolder)
    {
        _projectFolder = projectFolder;
        BackColor = Color.FromArgb(12, 12, 28);
        ForeColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        var refresh = new Button { Text = "REFRESH IMAGES", Dock = DockStyle.Fill };
        refresh.Click += (_, _) => RefreshImages();
        layout.Controls.Add(refresh, 0, 0);
        layout.Controls.Add(new Label { Text = "Choose numbered WebPs, then select an overlay area. Drag on the preview for a custom area.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        layout.Controls.Add(_images, 0, 1);
        layout.Controls.Add(_preview, 1, 1);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        _preset.Items.AddRange(["FiveM watermark (bottom right)", "FPS counter (top left)", "FPS counter (top right)", "Custom area (drag on image)"]);
        _preset.SelectedIndex = 0;
        controls.Controls.Add(_preset);
        controls.Controls.Add(_apply);
        controls.Controls.Add(_status);
        layout.Controls.Add(controls, 0, 2);
        layout.SetColumnSpan(controls, 2);
        layout.Controls.Add(_changes, 0, 3);
        layout.SetColumnSpan(_changes, 2);
        Controls.Add(layout);
        _preset.SelectedIndexChanged += (_, _) =>
        {
            if (_preset.SelectedIndex == 3) return;
            _selection = _preset.SelectedIndex switch
            {
                1 => new SKRect(0.0f, 0.0f, 0.16f, 0.08f),
                2 => new SKRect(0.84f, 0.0f, 1.0f, 0.08f),
                _ => new SKRect(0.84f, 0.91f, 0.99f, 0.99f)
            };
            _preview.Invalidate();
        };
        _images.SelectedIndexChanged += (_, _) => LoadPreview();
        _preview.Paint += PreviewPaint;
        _preview.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
        _preview.MouseMove += (_, e) => { if (_dragStart.HasValue) SetDraggedSelection(_dragStart.Value, e.Location); };
        _preview.MouseUp += (_, e) =>
        {
            if (!_dragStart.HasValue) return;
            SetDraggedSelection(_dragStart.Value, e.Location);
            _dragStart = null;
            _preset.SelectedIndex = 3;
        };
        _apply.Click += async (_, _) => await ApplyAsync();
    }

    internal void RefreshImages()
    {
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
            using SKBitmap bitmap = SKBitmap.Decode(Path.Combine(_projectFolder(), name)) ??
                throw new InvalidDataException("Could not read " + name);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            using var decoded = Image.FromStream(stream);
            _preview.Image = new Bitmap(decoded);
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

    private void SetDraggedSelection(Point start, Point end)
    {
        RectangleF bounds = ImageBounds();
        if (bounds.IsEmpty) return;
        float x1 = Math.Clamp((start.X - bounds.Left) / bounds.Width, 0, 1);
        float y1 = Math.Clamp((start.Y - bounds.Top) / bounds.Height, 0, 1);
        float x2 = Math.Clamp((end.X - bounds.Left) / bounds.Width, 0, 1);
        float y2 = Math.Clamp((end.Y - bounds.Top) / bounds.Height, 0, 1);
        _selection = new SKRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
        _preview.Invalidate();
    }

    private void PreviewPaint(object? sender, PaintEventArgs e)
    {
        RectangleF bounds = ImageBounds();
        if (bounds.IsEmpty) return;
        using var pen = new Pen(Color.Yellow, 2);
        e.Graphics.DrawRectangle(pen, bounds.Left + _selection.Left * bounds.Width,
            bounds.Top + _selection.Top * bounds.Height, _selection.Width * bounds.Width, _selection.Height * bounds.Height);
    }

    private async Task ApplyAsync()
    {
        string[] names = _images.SelectedItems.Cast<string>().ToArray();
        if (names.Length == 0 || _selection.Width <= 0 || _selection.Height <= 0)
        {
            _status.Text = "Select images and a nonempty overlay area.";
            return;
        }
        _apply.Enabled = false;
        _status.Text = "Removing overlay area...";
        try
        {
            var changed = await Task.Run(() => LoadingScreenWorkflow.RemoveOverlay(_projectFolder(), names, _selection));
            _changes.Text = string.Join(Environment.NewLine, changed);
            _status.Text = $"Changed {changed.Count} images. Review each before export.";
            LoadPreview();
        }
        catch (Exception error) { _status.Text = "Failed: " + error.Message; }
        finally { _apply.Enabled = true; }
    }
}
