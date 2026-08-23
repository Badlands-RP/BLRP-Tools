using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;

namespace BLRP.ClothingLocator;

internal enum ClothingPreviewLod { High, Medium, Low }

internal sealed class ClothingPreviewDialog : Form
{
    private readonly LodMeshPreview _preview = new() { Dock = DockStyle.Fill };

    public ClothingPreviewDialog(string modelPath, string? texturePath)
    {
        Text = "BLRP Clothing Preview";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 600);
        ClientSize = new Size(900, 760);
        BackColor = Color.FromArgb(12, 12, 28);
        ForeColor = Color.White;
        Font = new Font("Consolas", 9F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = $"{Path.GetFileName(modelPath)}  /  {(texturePath == null ? "NO MATCHING YTD" : Path.GetFileName(texturePath))}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(135, 206, 235),
            Font = new Font(Font, FontStyle.Bold)
        });
        layout.Controls.Add(_preview, 0, 1);
        Controls.Add(layout);

        Shown += async (_, _) =>
        {
            try { await _preview.LoadAsync(modelPath, ClothingPreviewLod.High, texturePath); }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }
}

internal sealed class LodReviewDialog : Form
{
    private readonly string _sourcePath;
    private readonly string _rootPath;
    private readonly string? _texturePath;
    private readonly string? _gitHistory;
    private readonly LodMeshPreview _before = new() { Dock = DockStyle.Fill };
    private readonly LodMeshPreview _after = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _medium = new() { Minimum = 10, Maximum = 90, Value = 50, Width = 70 };
    private readonly NumericUpDown _low = new() { Minimum = 5, Maximum = 80, Value = 20, Width = 70 };
    private readonly CheckBox _aggressiveLow = new() { Text = "AGGRESSIVE LOW", Checked = false, AutoSize = true, ForeColor = Color.White, Margin = new Padding(18, 9, 0, 0) };
    private readonly CheckBox _optimizeHigh = new() { Text = "OPTIMISE HIGH", Checked = false, AutoSize = true, ForeColor = Color.White, Margin = new Padding(0, 9, 8, 0) };
    private readonly ComboBox _highTargetMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly NumericUpDown _highTarget = new() { Minimum = 100, Maximum = 1_000_000, Value = 20_000, ThousandsSeparator = true, Width = 92 };
    private readonly ComboBox _highMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ComboBox _afterLod = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _distance = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Label _stats = new() { AutoSize = true, ForeColor = Color.White };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.FromArgb(135, 206, 235) };
    private readonly Button _generate = Button("GENERATE CANDIDATE");
    private readonly Button _apply = Button("APPLY REVIEWED CHANGES");
    private readonly ClothingLodStats _sourceStats;
    private bool _generationAllowed = true;
    private bool _candidateOptimizesHigh;
    private string? _candidatePath;

    public string? BackupPath { get; private set; }

    public LodReviewDialog(string sourcePath, string rootPath, bool ownsCloth, string? texturePath)
    {
        _sourcePath = sourcePath;
        _rootPath = rootPath;
        _texturePath = texturePath;
        _gitHistory = GitFileHistory.Describe(rootPath, sourcePath);
        _sourceStats = ClothingLodGenerator.Analyze(sourcePath);
        Text = "BLRP Clothing LOD Review";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 680);
        ClientSize = new Size(1200, 760);
        BackColor = Color.FromArgb(12, 12, 28);
        ForeColor = Color.White;
        Font = new Font("Consolas", 9F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _afterLod.Items.AddRange([ClothingPreviewLod.High, ClothingPreviewLod.Medium, ClothingPreviewLod.Low]);
        _afterLod.SelectedIndex = 1;
        _afterLod.SelectedIndexChanged += (_, _) => LoadAfterPreview();
        _distance.Items.AddRange(["CLOSE", "MEDIUM", "FAR"]);
        _distance.SelectedIndex = 0;
        _distance.SelectedIndexChanged += (_, _) => SetPreviewDistance();
        _highTargetMode.Items.AddRange(["POLYGONS", "PERCENT"]);
        _highTargetMode.SelectedIndex = 0;
        _highTargetMode.SelectedIndexChanged += (_, _) => UpdateHighTargetMode();
        _highMode.Items.AddRange(["CONSERVATIVE", "AGGRESSIVE"]);
        _highMode.SelectedIndex = 0;
        _optimizeHigh.CheckedChanged += (_, _) => UpdateHighControls();
        _generate.Click += async (_, _) => await GenerateAsync();
        _apply.Click += (_, _) => Apply();
        _apply.Enabled = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        Control header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);
        root.Controls.Add(PreviewCard("BEFORE / ORIGINAL HIGH", _before), 0, 1);
        root.Controls.Add(PreviewCard("AFTER / CANDIDATE", _after), 1, 1);
        Control footer = BuildFooter();
        root.Controls.Add(footer, 0, 2);
        root.SetColumnSpan(footer, 2);
        Controls.Add(root);

        _stats.Text = FormatStats("CURRENT", _sourceStats);
        UpdateHighControls();
        if (ownsCloth)
        {
            _generationAllowed = false;
            _generate.Enabled = false;
            _optimizeHigh.Enabled = false;
            _aggressiveLow.Enabled = false;
            UpdateHighControls();
            _status.Text = "CLOTH-SIMULATED MODEL / AUTOMATIC LODS DISABLED";
            _status.ForeColor = Color.FromArgb(255, 180, 50);
        }
        else if (_sourceStats.HasMedium && _sourceStats.HasLow)
        {
            _status.Text = "MEDIUM AND LOW ALREADY EXIST / HIGH OPTIMISATION IS OPTIONAL";
        }
        Shown += async (_, _) =>
        {
            try { await _before.LoadAsync(_sourcePath, ClothingPreviewLod.High, _texturePath); }
            catch (Exception exception) { ShowError(exception.Message); }
        };
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        string info = $"{Path.GetFileName(_sourcePath)}  /  {(_texturePath == null ? "NO MATCHING YTD" : Path.GetFileName(_texturePath))}";
        if (_gitHistory != null) info += "  /  " + _gitHistory;
        panel.Controls.Add(new Label
        {
            Text = info,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(135, 206, 235)
        });
        var lodControls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        lodControls.Controls.Add(new Label { Text = "MEDIUM %", AutoSize = true, Margin = new Padding(0, 11, 6, 0) });
        lodControls.Controls.Add(_medium);
        lodControls.Controls.Add(new Label { Text = "LOW %", AutoSize = true, Margin = new Padding(18, 11, 6, 0) });
        lodControls.Controls.Add(_low);
        lodControls.Controls.Add(_aggressiveLow);
        lodControls.Controls.Add(_generate);
        lodControls.Controls.Add(new Label { Text = "AFTER VIEW", AutoSize = true, Margin = new Padding(18, 11, 6, 0) });
        lodControls.Controls.Add(_afterLod);
        lodControls.Controls.Add(new Label { Text = "DISTANCE", AutoSize = true, Margin = new Padding(18, 11, 6, 0) });
        lodControls.Controls.Add(_distance);
        panel.Controls.Add(lodControls, 0, 1);

        var highControls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        highControls.Controls.Add(_optimizeHigh);
        highControls.Controls.Add(new Label { Text = "TARGET", AutoSize = true, Margin = new Padding(8, 11, 6, 0) });
        highControls.Controls.Add(_highTargetMode);
        highControls.Controls.Add(_highTarget);
        highControls.Controls.Add(new Label { Text = "MODE", AutoSize = true, Margin = new Padding(18, 11, 6, 0) });
        highControls.Controls.Add(_highMode);
        panel.Controls.Add(highControls, 0, 2);
        return panel;
    }

    private static Control PreviewCard(string title, Control preview)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), ColumnCount = 1, RowCount = 2, BackColor = Color.FromArgb(20, 20, 40) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(135, 206, 235), Font = new Font("Consolas", 10F, FontStyle.Bold) });
        panel.Controls.Add(preview, 0, 1);
        return panel;
    }

    private Control BuildFooter()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        panel.Controls.Add(_stats, 0, 0);
        panel.Controls.Add(_status, 0, 1);
        panel.Controls.Add(_apply, 1, 0);
        panel.SetRowSpan(_apply, 2);
        Button cancel = Button("CANCEL");
        cancel.Click += (_, _) => Close();
        panel.Controls.Add(cancel, 2, 0);
        panel.SetRowSpan(cancel, 2);
        return panel;
    }

    private async Task GenerateAsync()
    {
        if (_low.Value >= _medium.Value) { ShowError("Low must be smaller than Medium."); return; }
        if (_sourceStats.HasMedium && _sourceStats.HasLow && !_optimizeHigh.Checked)
        {
            ShowError("This model already has Medium and Low LODs. Enable High optimisation to create a new candidate.");
            return;
        }
        float? highRatio = HighRatio();
        if (_optimizeHigh.Checked && highRatio is null) return;
        SetBusy(true, "GENERATING REVIEW CANDIDATE...");
        try
        {
            if (_candidatePath != null && File.Exists(_candidatePath)) File.Delete(_candidatePath);
            ClothingLodResult result = await Task.Run(() => ClothingLodGenerator.Generate(
                _sourcePath,
                (float)_medium.Value / 100,
                (float)_low.Value / 100,
                _aggressiveLow.Checked,
                highRatio,
                _highMode.SelectedIndex == 1));
            _afterLod.SelectedItem = result.HighOptimized ? ClothingPreviewLod.High : ClothingPreviewLod.Medium;
            _candidatePath = result.CandidatePath;
            _candidateOptimizesHigh = result.HighOptimized;
            _stats.Text = FormatStats("BEFORE", result.Before) + "    " + FormatStats("AFTER", result.After);
            await _after.LoadAsync(_candidatePath, (ClothingPreviewLod)_afterLod.SelectedItem!, _texturePath);
            _apply.Enabled = true;
            _status.Text = "CANDIDATE READY / ROTATE AND REVIEW BOTH VIEWS BEFORE APPLYING";
        }
        catch (Exception exception) { ShowError(exception.Message); }
        finally { SetBusy(false, string.Empty); }
    }

    private async void LoadAfterPreview()
    {
        if (_candidatePath == null) return;
        try { await _after.LoadAsync(_candidatePath, (ClothingPreviewLod)_afterLod.SelectedItem!, _texturePath); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void Apply()
    {
        if (_candidatePath == null) return;
        try
        {
            BackupPath = ClothingLodGenerator.Apply(_sourcePath, _candidatePath, _rootPath, _candidateOptimizesHigh);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void SetBusy(bool busy, string status)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _generate.Enabled = !busy && _generationAllowed;
        _apply.Enabled = !busy && _candidatePath != null;
        if (busy) _status.Text = status;
    }

    private float? HighRatio()
    {
        if (!_optimizeHigh.Checked) return null;
        float ratio = _highTargetMode.SelectedIndex == 0
            ? (float)_highTarget.Value / _sourceStats.High
            : (float)_highTarget.Value / 100;
        if (ratio is > 0 and < 1) return ratio;
        ShowError("The High target must be below the current High polygon count.");
        return null;
    }

    private void UpdateHighControls()
    {
        bool enabled = _generationAllowed && _optimizeHigh.Checked;
        _highTargetMode.Enabled = enabled;
        _highTarget.Enabled = enabled;
        _highMode.Enabled = enabled;
    }

    private void UpdateHighTargetMode()
    {
        if (_highTargetMode.SelectedIndex == 1)
        {
            _highTarget.Minimum = 10;
            _highTarget.Maximum = 95;
            _highTarget.Value = 50;
            _highTarget.ThousandsSeparator = false;
        }
        else
        {
            _highTarget.Minimum = 1;
            _highTarget.Maximum = Math.Max(1, _sourceStats.High - 1);
            _highTarget.Minimum = Math.Min(100, _highTarget.Maximum);
            _highTarget.Value = Math.Min(20_000, _highTarget.Maximum);
            _highTarget.ThousandsSeparator = true;
        }
    }

    private void SetPreviewDistance()
    {
        float zoom = _distance.SelectedIndex switch { 1 => 0.65f, 2 => 0.4f, _ => 1f };
        _before.SetZoom(zoom);
        _after.SetZoom(zoom);
    }

    private void ShowError(string message)
    {
        _status.Text = message.ToUpperInvariant();
        _status.ForeColor = Color.FromArgb(255, 180, 50);
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static string FormatStats(string label, ClothingLodStats stats) =>
        $"{label}: HIGH {stats.High:N0} / MED {stats.Medium:N0} / LOW {stats.Low:N0}";

    private static Button Button(string text) => new()
    {
        Text = text,
        Width = 170,
        Height = 34,
        Margin = new Padding(12, 2, 0, 0),
        BackColor = Color.FromArgb(100, 149, 237),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing && _candidatePath != null && File.Exists(_candidatePath)) File.Delete(_candidatePath);
        base.Dispose(disposing);
    }
}

internal sealed class LodMeshPreview : Control
{
    private LodPreviewScene? _scene;
    private Bitmap? _frame;
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 140 };
    private Point _lastMouse;
    private float _yaw = -0.65f;
    private float _pitch = 0.35f;
    private float _zoom = 1f;

    public LodMeshPreview()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14, 14, 30);
        _settle.Tick += (_, _) => { _settle.Stop(); Render(); };
        Resize += (_, _) => RenderInteractive();
        MouseDown += (_, e) => _lastMouse = e.Location;
        MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _yaw += (e.X - _lastMouse.X) * 0.012f;
            _pitch = Math.Clamp(_pitch + (e.Y - _lastMouse.Y) * 0.012f, -1.5f, 1.5f);
            _lastMouse = e.Location;
            RenderInteractive();
        };
        MouseWheel += (_, e) => { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.12f : 0.89f), 0.25f, 4f); RenderInteractive(); };
    }

    public async Task LoadAsync(string path, ClothingPreviewLod lod, string? texturePath)
    {
        _scene = await Task.Run(() => LodPreviewScene.Load(path, lod, texturePath));
        _yaw = -0.65f; _pitch = 0.35f; _zoom = 1f;
        Render();
    }

    public void SetZoom(float zoom)
    {
        _zoom = Math.Clamp(zoom, 0.25f, 4f);
        RenderInteractive();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_frame != null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            e.Graphics.DrawImage(_frame, ClientRectangle);
        }
        else TextRenderer.DrawText(e.Graphics, "GENERATE TO PREVIEW", Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void RenderInteractive()
    {
        _settle.Stop();
        Render(0.5f);
        _settle.Start();
    }

    private void Render(float resolutionScale = 1f)
    {
        if (_scene == null || Width < 8 || Height < 8) return;
        int width = Math.Max(8, (int)(Width * resolutionScale));
        int height = Math.Max(8, (int)(Height * resolutionScale));
        Bitmap next = _scene.Render(width, height, _yaw, _pitch, _zoom);
        Bitmap? old = _frame; _frame = next; old?.Dispose(); Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _settle.Dispose(); _frame?.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed record LodVertex(Vector3 Position, Vector2 UV);
internal sealed record LodTriangle(LodVertex A, LodVertex B, LodVertex C, LodPreviewTexture Texture);
internal sealed record LodScreenVertex(float X, float Y, float Depth, Vector2 UV);

internal sealed class LodPreviewTexture
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int[] Pixels { get; init; }

    public int Sample(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);
        int x = Math.Clamp((int)(u * Width), 0, Width - 1);
        int y = Math.Clamp((int)(v * Height), 0, Height - 1);
        return Pixels[y * Width + x];
    }
}

internal sealed class LodPreviewScene
{
    private const uint DiffuseSampler = 4059966321;
    private readonly LodTriangle[] _triangles;
    private readonly Vector3 _center;
    private readonly float _radius;

    private LodPreviewScene(LodTriangle[] triangles, Vector3 center, float radius)
    { _triangles = triangles; _center = center; _radius = radius; }

    internal static bool SelfTest(string modelPath, string texturePath)
    {
        using Bitmap image = Load(modelPath, ClothingPreviewLod.High, texturePath).Render(96, 96, -0.65f, 0.35f, 1f);
        int background = Color.FromArgb(14, 14, 30).ToArgb();
        return Enumerable.Range(0, image.Width).Any(x =>
            Enumerable.Range(0, image.Height).Any(y => image.GetPixel(x, y).ToArgb() != background));
    }

    public static LodPreviewScene Load(string path, ClothingPreviewLod lod, string? texturePath)
    {
        var file = new YddFile(); file.Load(File.ReadAllBytes(path));
        var textures = new Dictionary<string, LodPreviewTexture>(StringComparer.OrdinalIgnoreCase);
        if (texturePath != null && File.Exists(texturePath))
        {
            var ytd = new YtdFile();
            ytd.Load(File.ReadAllBytes(texturePath));
            foreach (Texture texture in ytd.TextureDict?.Textures?.data_items ?? [])
            {
                try { textures[texture.Name] = DecodeTexture(texture); }
                catch (NotSupportedException) { }
            }
        }
        LodPreviewTexture fallback = textures.Values.FirstOrDefault() ?? SolidTexture(Color.FromArgb(90, 205, 230));
        var triangles = new List<LodTriangle>();
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Drawable drawable in file.DrawableDict?.Drawables?.data_items ?? [])
        {
            DrawableModel[] models = lod switch
            {
                ClothingPreviewLod.High => drawable.DrawableModels?.High ?? [],
                ClothingPreviewLod.Medium => drawable.DrawableModels?.Med ?? [],
                _ => drawable.DrawableModels?.Low ?? []
            };
            foreach (DrawableGeometry geometry in models.SelectMany(model => model.Geometries ?? []))
            {
                VertexData vertices = geometry.VertexData;
                ushort[] indices = geometry.IndexBuffer?.Indices ?? [];
                LodPreviewTexture texture = FindTexture(geometry.Shader, textures) ?? fallback;
                for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                {
                    LodVertex a = ReadVertex(vertices, indices[offset]);
                    LodVertex b = ReadVertex(vertices, indices[offset + 1]);
                    LodVertex c = ReadVertex(vertices, indices[offset + 2]);
                    min = Vector3.Min(min, Vector3.Min(a.Position, Vector3.Min(b.Position, c.Position)));
                    max = Vector3.Max(max, Vector3.Max(a.Position, Vector3.Max(b.Position, c.Position)));
                    triangles.Add(new LodTriangle(a, b, c, texture));
                }
            }
        }
        if (triangles.Count == 0) throw new InvalidDataException($"The {lod} LOD has no triangles.");
        Vector3 center = (min + max) * 0.5f;
        float radius = triangles.SelectMany(item => new[] { item.A, item.B, item.C }).Max(point => Vector3.Distance(center, point.Position));
        return new LodPreviewScene(triangles.ToArray(), center, Math.Max(radius, 0.001f));
    }

    public Bitmap Render(int width, int height, float yaw, float pitch, float zoom)
    {
        int background = Color.FromArgb(14, 14, 30).ToArgb();
        int[] pixels = Enumerable.Repeat(background, width * height).ToArray();
        float[] depth = Enumerable.Repeat(float.PositiveInfinity, width * height).ToArray();
        Matrix4x4 rotation = Matrix4x4.CreateRotationZ(yaw) * Matrix4x4.CreateRotationX(pitch);
        float scale = Math.Min(width, height) * 0.44f / _radius * zoom;
        foreach (LodTriangle triangle in _triangles)
        {
            Vector3 a = Vector3.Transform(triangle.A.Position - _center, rotation);
            Vector3 b = Vector3.Transform(triangle.B.Position - _center, rotation);
            Vector3 c = Vector3.Transform(triangle.C.Position - _center, rotation);
            Rasterize(pixels, depth, width, height,
                Screen(a, triangle.A.UV, width, height, scale),
                Screen(b, triangle.B.UV, width, height, scale),
                Screen(c, triangle.C.UV, width, height, scale),
                triangle.Texture);
        }
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); bitmap.UnlockBits(data); return bitmap;
    }

    private static LodScreenVertex Screen(Vector3 point, Vector2 uv, int width, int height, float scale) =>
        new(width * 0.5f + point.X * scale, height * 0.5f - point.Z * scale, point.Y, uv);

    private static void Rasterize(int[] pixels, float[] depth, int width, int height,
        LodScreenVertex a, LodScreenVertex b, LodScreenVertex c, LodPreviewTexture texture)
    {
        float area = Edge(a, b, c.X, c.Y); if (Math.Abs(area) < 0.001f) return;
        int minX = Math.Clamp((int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1);
        int minY = Math.Clamp((int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
        Vector3 normal = Vector3.Normalize(Vector3.Cross(new(b.X - a.X, b.Y - a.Y, b.Depth - a.Depth), new(c.X - a.X, c.Y - a.Y, c.Depth - a.Depth)));
        float light = Math.Clamp(0.45f + 0.55f * Math.Abs(normal.Z), 0.35f, 1f);
        for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
        {
            float w0 = Edge(b, c, x + 0.5f, y + 0.5f) / area;
            float w1 = Edge(c, a, x + 0.5f, y + 0.5f) / area;
            float w2 = 1 - w0 - w1;
            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
            float z = w0 * a.Depth + w1 * b.Depth + w2 * c.Depth;
            int index = y * width + x;
            if (z >= depth[index]) continue;
            int color = texture.Sample(
                w0 * a.UV.X + w1 * b.UV.X + w2 * c.UV.X,
                w0 * a.UV.Y + w1 * b.UV.Y + w2 * c.UV.Y);
            int alpha = (color >> 24) & 255;
            if (alpha < 16) continue;
            int red = (int)(((color >> 16) & 255) * light);
            int green = (int)(((color >> 8) & 255) * light);
            int blue = (int)((color & 255) * light);
            depth[index] = z;
            pixels[index] = (alpha << 24) | (red << 16) | (green << 8) | blue;
        }
    }

    private static float Edge(LodScreenVertex a, LodScreenVertex b, float x, float y) =>
        (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

    private static LodVertex ReadVertex(VertexData data, int index)
    {
        SharpDX.Vector3 value = data.GetVector3(index, 0);
        Vector2 uv = Vector2.Zero;
        if ((data.Info.Flags & (1 << 6)) != 0)
        {
            if (data.Info.GetComponentType(6) == VertexComponentType.Half2)
            {
                SharpDX.Half2 half = data.GetHalf2(index, 6);
                uv = new Vector2((float)half.X, (float)half.Y);
            }
            else
            {
                SharpDX.Vector2 full = data.GetVector2(index, 6);
                uv = new Vector2(full.X, full.Y);
            }
        }
        return new LodVertex(new Vector3(value.X, value.Y, value.Z), uv);
    }

    private static LodPreviewTexture? FindTexture(ShaderFX? shader, Dictionary<string, LodPreviewTexture> textures)
    {
        ShaderParametersBlock? parameters = shader?.ParametersList;
        if (parameters is null) return null;
        for (int index = 0; index < parameters.Hashes.Length; index++)
        {
            if ((uint)parameters.Hashes[index] == DiffuseSampler &&
                parameters.Parameters[index].Data is TextureBase reference &&
                textures.TryGetValue(reference.Name, out LodPreviewTexture? texture)) return texture;
        }
        for (int index = 0; index < parameters.Hashes.Length; index++)
        {
            if (parameters.Parameters[index].Data is TextureBase reference &&
                textures.TryGetValue(reference.Name, out LodPreviewTexture? texture)) return texture;
        }
        return null;
    }

    private static LodPreviewTexture SolidTexture(Color color) => new()
    {
        Width = 2,
        Height = 2,
        Pixels = Enumerable.Repeat(color.ToArgb(), 4).ToArray()
    };

    private static LodPreviewTexture DecodeTexture(Texture texture)
    {
        int width = texture.Width, height = texture.Height;
        byte[] data = texture.Data?.FullData ?? throw new InvalidDataException($"Texture {texture.Name} has no pixel data.");
        int[] pixels = new int[width * height];
        switch (texture.Format)
        {
            case TextureFormat.D3DFMT_DXT1: DecodeBlocks(data, width, height, pixels, false); break;
            case TextureFormat.D3DFMT_DXT5: DecodeBlocks(data, width, height, pixels, true); break;
            case TextureFormat.D3DFMT_A8R8G8B8:
                for (int index = 0; index < pixels.Length; index++) pixels[index] = BitConverter.ToInt32(data, index * 4);
                break;
            default: throw new NotSupportedException();
        }
        return new LodPreviewTexture { Width = width, Height = height, Pixels = pixels };
    }

    private static void DecodeBlocks(byte[] data, int width, int height, int[] pixels, bool dxt5)
    {
        int offset = 0;
        for (int blockY = 0; blockY < height; blockY += 4)
        for (int blockX = 0; blockX < width; blockX += 4)
        {
            byte[] alpha = Enumerable.Repeat((byte)255, 16).ToArray();
            if (dxt5)
            {
                byte a0 = data[offset], a1 = data[offset + 1];
                byte[] palette = new byte[8]; palette[0] = a0; palette[1] = a1;
                if (a0 > a1) for (int index = 1; index <= 6; index++) palette[index + 1] = (byte)(((7 - index) * a0 + index * a1) / 7);
                else { for (int index = 1; index <= 4; index++) palette[index + 1] = (byte)(((5 - index) * a0 + index * a1) / 5); palette[6] = 0; palette[7] = 255; }
                ulong bits = 0; for (int index = 0; index < 6; index++) bits |= (ulong)data[offset + 2 + index] << (8 * index);
                for (int index = 0; index < 16; index++) alpha[index] = palette[(bits >> (3 * index)) & 7];
                offset += 8;
            }
            ushort c0 = BitConverter.ToUInt16(data, offset), c1 = BitConverter.ToUInt16(data, offset + 2);
            int[] colors = BuildPalette(c0, c1, dxt5);
            uint colorBits = BitConverter.ToUInt32(data, offset + 4);
            for (int pixelY = 0; pixelY < 4; pixelY++)
            for (int pixelX = 0; pixelX < 4; pixelX++)
            {
                int x = blockX + pixelX, y = blockY + pixelY, index = pixelY * 4 + pixelX;
                if (x >= width || y >= height) continue;
                pixels[y * width + x] = (alpha[index] << 24) | (colors[(colorBits >> (2 * index)) & 3] & 0xFFFFFF);
            }
            offset += 8;
        }
    }

    private static int[] BuildPalette(ushort c0, ushort c1, bool opaque)
    {
        int Color565(ushort color) => (((color >> 11) * 255 / 31) << 16) |
            ((((color >> 5) & 63) * 255 / 63) << 8) | ((color & 31) * 255 / 31);
        int a = Color565(c0), b = Color565(c1);
        int Blend(int x, int y, int wx, int wy, int divisor) =>
            (((((x >> 16) & 255) * wx + ((y >> 16) & 255) * wy) / divisor) << 16) |
            (((((x >> 8) & 255) * wx + ((y >> 8) & 255) * wy) / divisor) << 8) |
            (((x & 255) * wx + (y & 255) * wy) / divisor);
        return c0 > c1 || opaque
            ? [a, b, Blend(a, b, 2, 1, 3), Blend(a, b, 1, 2, 3)]
            : [a, b, Blend(a, b, 1, 1, 2), 0];
    }
}
