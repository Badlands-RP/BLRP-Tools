using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using BCnEncoder.Decoder;
using BCnEncoder.Shared.ImageFiles;

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
    private readonly bool _exportMode;
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

    public LodReviewDialog(string sourcePath, string rootPath, bool ownsCloth, string? texturePath, bool exportMode = false)
    {
        _sourcePath = sourcePath;
        _rootPath = rootPath;
        _texturePath = texturePath;
        _exportMode = exportMode;
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
        if (_exportMode) _apply.Text = "EXPORT REVIEWED FILES";

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
            if (_exportMode)
            {
                using var save = new SaveFileDialog
                {
                    Title = "Export optimised YDD and matching YTDs",
                    Filter = "Drawable dictionary (*.ydd)|*.ydd",
                    FileName = Path.GetFileName(_sourcePath),
                    InitialDirectory = Path.GetDirectoryName(_sourcePath),
                    AddExtension = true,
                    DefaultExt = "ydd"
                };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                IReadOnlyList<string> files = ClothingLodGenerator.Export(
                    _sourcePath, _candidatePath, save.FileName, _candidateOptimizesHigh);
                BackupPath = $"EXPORTED {files.Count} FILE{(files.Count == 1 ? string.Empty : "S")} TO {Path.GetDirectoryName(save.FileName)}";
            }
            else BackupPath = ClothingLodGenerator.Apply(_sourcePath, _candidatePath, _rootPath, _candidateOptimizesHigh);
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
internal enum LodRasterPass { Legacy, Opaque, Transparent }
internal sealed record LodPreviewModelInfo(bool Placeholder, bool Skinned, bool Emissive, bool FrontMounted);

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
        bool visible = Enumerable.Range(0, image.Width).Any(x =>
            Enumerable.Range(0, image.Height).Any(y => image.GetPixel(x, y).ToArgb() != background));
        LodVertex Vertex(float x, float y, float z) => new(new Vector3(x, y, z), Vector2.Zero);
        // A two-sided accessory must show its red front for a badge and blue
        // back for a backpack, without relying on the empty-view fallback.
        var accessory = new LodPreviewScene([
            new(Vertex(-0.5f, -0.01f, 0), Vertex(0.5f, -0.01f, 0), Vertex(0, -0.01f, 1), SolidTexture(Color.Red)),
            new(Vertex(0, 0.01f, 1), Vertex(0.5f, 0.01f, 0), Vertex(-0.5f, 0.01f, 0), SolidTexture(Color.Blue)),
        ], new Vector3(0, 0, 0.5f), 1);
        using Bitmap front = accessory.RenderThumbnail(64, 64, 5, frontMounted: true);
        using Bitmap back = accessory.RenderThumbnail(64, 64, 5);
        Color frontColor = front.GetPixel(32, 32), backColor = back.GetPixel(32, 32);
        return visible && frontColor.R > frontColor.B && backColor.B > backColor.R
            && front.GetPixel(0, 0).A == 0 && back.GetPixel(0, 0).A == 0;
    }

    public static LodPreviewScene Load(string path, ClothingPreviewLod lod, string? texturePath)
    {
        return Load(File.ReadAllBytes(path), lod,
            texturePath != null ? File.ReadAllBytes(texturePath) : null);
    }

    internal static LodPreviewModelInfo InspectModel(byte[] modelBytes, bool checkAccessoryPosition = false)
    {
        var file = new YddFile();
        file.Load(modelBytes);
        DrawableModel[] models = (file.DrawableDict?.Drawables?.data_items ?? [])
            .SelectMany(drawable => drawable.DrawableModels?.High ?? []).ToArray();
        DrawableGeometry[] geometries = models.SelectMany(model => model.Geometries ?? []).ToArray();
        bool skinned = models.Any(model => model.HasSkin != 0);
        bool emissive = geometries.Any(geometry => geometry.Shader?.Name.ToString() == "ped_emissive");
        SharpDX.Vector3[] positions = checkAccessoryPosition
            ? geometries.SelectMany(geometry => Enumerable.Range(0, geometry.VertexData.VertexCount)
                .Select(index => geometry.VertexData.GetVector3(index, 0))).ToArray() : [];
        bool frontMounted = positions.Length > 0 && (positions.Min(point => point.Y) + positions.Max(point => point.Y)) / 2 < -0.01f;
        if (geometries.Length != 1 || geometries[0].VertexData.VertexCount > 8
            || geometries[0].VertexData.VertexCount < 4
            || geometries[0].IndexBuffer?.Indices?.Length > 12) return new(false, skinned, emissive, frontMounted);
        // Empty GTA slots use a tiny quad. Framing it as a garment produces
        // an enlarged checkerboard or texture fragment.
        SharpDX.Vector3[] points = Enumerable.Range(0, geometries[0].VertexData.VertexCount)
            .Select(index => geometries[0].VertexData.GetVector3(index, 0)).Distinct().ToArray();
        bool placeholder = points.Length <= 4
            && points.Max(point => point.X) - points.Min(point => point.X) < 0.1f
            && points.Max(point => point.Y) - points.Min(point => point.Y) < 0.1f
            && points.Max(point => point.Z) - points.Min(point => point.Z) < 0.1f;
        return new(placeholder, skinned, emissive, frontMounted);
    }

    internal LodPreviewScene WithReferenceHead(LodPreviewScene head)
    {
        Vector3 light = Vector3.Normalize(new Vector3(-0.4f, -0.8f, 0.6f));
        LodTriangle ShadeHead(LodTriangle triangle)
        {
            Vector3 normal = Vector3.Cross(triangle.B.Position - triangle.A.Position, triangle.C.Position - triangle.A.Position);
            float intensity = normal.LengthSquared() > 0.000000000001f ? MathF.Max(0, Vector3.Dot(Vector3.Normalize(normal), light)) : 0;
            int shade = (int)(140 + 65 * intensity);
            return triangle with { Texture = SolidTexture(Color.FromArgb(shade, shade - 4, shade - 8)) };
        }
        LodTriangle[] triangles = head._triangles.Select(ShadeHead)
            .Concat(_triangles).ToArray();
        Vector3[] points = triangles.SelectMany(triangle => new[] { triangle.A.Position, triangle.B.Position, triangle.C.Position }).ToArray();
        Vector3 min = points.Aggregate(Vector3.Min), max = points.Aggregate(Vector3.Max);
        Vector3 center = (min + max) / 2;
        return new LodPreviewScene(triangles, center, points.Max(point => Vector3.Distance(center, point)));
    }

    public static LodPreviewScene Load(byte[] modelBytes, ClothingPreviewLod lod, byte[]? textureBytes, bool strict = false, Matrix4x4? attachment = null)
    {
        var file = new YddFile(); file.Load(modelBytes);
        var textures = new Dictionary<string, LodPreviewTexture>(StringComparer.OrdinalIgnoreCase);
        if (textureBytes != null)
        {
            var ytd = new YtdFile();
            ytd.Load(textureBytes);
            foreach (Texture texture in ytd.TextureDict?.Textures?.data_items ?? [])
            {
                try { textures[texture.Name] = DecodeTexture(texture); }
                catch (NotSupportedException) when (!strict) { }
            }
        }
        LodPreviewTexture fallback = textures.Values.FirstOrDefault() ?? SolidTexture(Color.FromArgb(90, 205, 230));
        LodPreviewTexture? selectedDiffuse = textures.Count == 1 ? textures.Values.Single() : null;
        var triangles = new List<LodTriangle>();
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Drawable drawable in file.DrawableDict?.Drawables?.data_items ?? [])
        {
            foreach (Texture embedded in drawable.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? [])
            {
                if (!textures.ContainsKey(embedded.Name)) textures[embedded.Name] = DecodeTexture(embedded);
            }
            DrawableModel[] models = lod switch
            {
                ClothingPreviewLod.High => drawable.DrawableModels?.High ?? [],
                ClothingPreviewLod.Medium => drawable.DrawableModels?.Med ?? [],
                _ => drawable.DrawableModels?.Low ?? []
            };
            foreach (DrawableModel model in models)
            {
                Matrix4x4 transform = (model.HasSkin == 0 ? ModelTransform(drawable.Skeleton, model.BoneIndex) : Matrix4x4.Identity)
                    * (attachment ?? Matrix4x4.Identity);
                foreach (DrawableGeometry geometry in model.Geometries ?? [])
                {
                    VertexData vertices = geometry.VertexData;
                    ushort[] indices = geometry.IndexBuffer?.Indices ?? [];
                    LodPreviewTexture? resolved = selectedDiffuse ?? FindTexture(geometry.Shader, textures);
                    // GTA applies the selected component YTD as the diffuse override.
                    // Some custom models omit the sampler reference entirely.
                    if (resolved == null && textures.Count == 1) resolved = textures.Values.Single();
                    if (strict && resolved == null)
                        throw new InvalidDataException("A diffuse material could not be resolved. No placeholder was exported.");
                    LodPreviewTexture texture = resolved ?? fallback;
                    string shaderName = geometry.Shader?.Name.ToString() ?? string.Empty;
                    if (shaderName == "ped_emissive")
                    {
                        // This shader's diffuse alpha controls the glow, not
                        // whether the sweater's fabric exists.
                        texture = new LodPreviewTexture
                        {
                            Width = texture.Width, Height = texture.Height,
                            Pixels = texture.Pixels.Select(color => color | unchecked((int)0xFF000000)).ToArray(),
                        };
                    }
                    if (shaderName.Contains("hair", StringComparison.OrdinalIgnoreCase))
                    {
                        var parameters = geometry.Shader?.ParametersList;
                        int order = Array.FindIndex(parameters?.Hashes ?? [], hash => (uint)hash == JenkHash.GenHash("ordernumber"));
                        if (order >= 0 && parameters!.Parameters[order].Data is SharpDX.Vector4 value && value.X > 0) continue;
                        texture = new LodPreviewTexture
                        {
                            Width = texture.Width, Height = texture.Height,
                            Pixels = texture.Pixels.Select(color =>
                            {
                                int shade = 16 + (((color >> 16) & 255) + 2 * ((color >> 8) & 255) + (color & 255)) / 16;
                                return (color & unchecked((int)0xFF000000)) | (shade << 16) | (shade << 8) | shade;
                            }).ToArray(),
                        };
                    }
                    for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                    {
                        LodVertex a = ReadVertex(vertices, indices[offset], transform);
                        LodVertex b = ReadVertex(vertices, indices[offset + 1], transform);
                        LodVertex c = ReadVertex(vertices, indices[offset + 2], transform);
                        min = Vector3.Min(min, Vector3.Min(a.Position, Vector3.Min(b.Position, c.Position)));
                        max = Vector3.Max(max, Vector3.Max(a.Position, Vector3.Max(b.Position, c.Position)));
                        triangles.Add(new LodTriangle(a, b, c, texture));
                    }
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

    public Bitmap RenderThumbnail(int width, int height, int component = -1, bool isProp = false,
        float yawOffset = 0, float pitchOffset = 0, bool frontMounted = false)
    {
        float yaw = -0.25f;
        float pitch = 0.12f;
        if (!isProp && component == 5 && !frontMounted) yaw += MathF.PI;
        if (isProp && component == 2) yaw += MathF.PI / 2;
        if (isProp && component is 6 or 7)
        {
            yaw -= MathF.PI / 2;
            pitch = 0.35f;
        }
        if (isProp && component == 0) pitch = 0.5f;
        yaw += yawOffset;
        pitch += pitchOffset;
        if (!isProp && component == 10)
        {
            Vector3 facing = _triangles.Aggregate(Vector3.Zero, (sum, triangle) =>
                sum + Vector3.Cross(triangle.B.Position - triangle.A.Position, triangle.C.Position - triangle.A.Position));
            if (facing.X * facing.X + facing.Y * facing.Y > 0.000001f)
                yaw = MathF.Atan2(-facing.X, -facing.Y) + yawOffset;
        }
        Matrix4x4 rotation = Matrix4x4.CreateRotationZ(yaw) * Matrix4x4.CreateRotationX(pitch);
        Vector3[] points = _triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Select(vertex => Vector3.Transform(vertex.Position - _center, rotation)).ToArray();
        float minX = points.Min(point => point.X), maxX = points.Max(point => point.X);
        float minZ = points.Min(point => point.Z), maxZ = points.Max(point => point.Z);
        float scale = Math.Min(width * 0.86f / Math.Max(maxX - minX, 0.001f),
            height * 0.86f / Math.Max(maxZ - minZ, 0.001f));
        Vector3 center = new((minX + maxX) / 2, 0, (minZ + maxZ) / 2);
        var pixels = new int[width * height];
        float[] depth = Enumerable.Repeat(float.PositiveInfinity, pixels.Length).ToArray();
        var projected = new List<(LodScreenVertex A, LodScreenVertex B, LodScreenVertex C, LodPreviewTexture Texture)>();
        foreach (LodTriangle triangle in _triangles)
        {
            Vector3 a = Vector3.Transform(triangle.A.Position - _center, rotation) - center;
            Vector3 b = Vector3.Transform(triangle.B.Position - _center, rotation) - center;
            Vector3 c = Vector3.Transform(triangle.C.Position - _center, rotation) - center;
            projected.Add((Screen(a, triangle.A.UV, width, height, scale), Screen(b, triangle.B.UV, width, height, scale),
                Screen(c, triangle.C.UV, width, height, scale), triangle.Texture));
        }
        foreach (var triangle in projected)
            Rasterize(pixels, depth, width, height, triangle.A, triangle.B, triangle.C, triangle.Texture, true, LodRasterPass.Opaque);
        // Glass and soft alpha must blend over the opaque surface behind them.
        // Writing a transparent lens into the depth buffer hides the watch dial.
        foreach (var triangle in projected.OrderByDescending(item => item.A.Depth + item.B.Depth + item.C.Depth))
            Rasterize(pixels, depth, width, height, triangle.A, triangle.B, triangle.C, triangle.Texture, true, LodRasterPass.Transparent);
        if (!pixels.Any(pixel => ((uint)pixel >> 24) > 0))
        {
            // Some bag components are one-sided panels. Prefer the outside of
            // backpacks, but use the other side when that view has no pixels.
            if (yawOffset == 0 && pitchOffset == 0)
                return RenderThumbnail(width, height, component, isProp, MathF.PI, 0, frontMounted);
            throw new InvalidDataException("The preview is fully transparent.");
        }
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
        return bitmap;
    }

    private static LodScreenVertex Screen(Vector3 point, Vector2 uv, int width, int height, float scale) =>
        new(width * 0.5f + point.X * scale, height * 0.5f - point.Z * scale, point.Y, uv);

    private static void Rasterize(int[] pixels, float[] depth, int width, int height,
        LodScreenVertex a, LodScreenVertex b, LodScreenVertex c, LodPreviewTexture texture, bool cullBackFaces = false,
        LodRasterPass pass = LodRasterPass.Legacy)
    {
        float area = Edge(a, b, c.X, c.Y); if (Math.Abs(area) < 0.001f) return;
        if (cullBackFaces && area < 0) return;
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
            if (pass == LodRasterPass.Opaque && alpha < 250) continue;
            if (pass == LodRasterPass.Transparent && alpha >= 250) continue;
            int red = (int)(((color >> 16) & 255) * light);
            int green = (int)(((color >> 8) & 255) * light);
            int blue = (int)((color & 255) * light);
            if (pass == LodRasterPass.Transparent)
            {
                int previous = pixels[index];
                float sourceAlpha = alpha / 255f, destinationAlpha = ((uint)previous >> 24) / 255f;
                float combined = sourceAlpha + destinationAlpha * (1 - sourceAlpha);
                int Blend(int source, int destination) => (int)((source * sourceAlpha + destination * destinationAlpha * (1 - sourceAlpha)) / combined);
                pixels[index] = ((int)(combined * 255) << 24) | (Blend(red, (previous >> 16) & 255) << 16)
                    | (Blend(green, (previous >> 8) & 255) << 8) | Blend(blue, previous & 255);
            }
            else
            {
                depth[index] = z;
                pixels[index] = (alpha << 24) | (red << 16) | (green << 8) | blue;
            }
        }
    }

    private static float Edge(LodScreenVertex a, LodScreenVertex b, float x, float y) =>
        (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

    private static Matrix4x4 ModelTransform(Skeleton? skeleton, int index)
    {
        if (skeleton?.Transformations == null || index >= skeleton.Transformations.Length) return Matrix4x4.Identity;
        SharpDX.Matrix matrix = skeleton.Transformations[index];
        matrix.Column4 = SharpDX.Vector4.UnitW;
        int parent = skeleton.ParentIndices[index];
        int remaining = skeleton.Transformations.Length;
        while (parent >= 0 && remaining-- > 0)
        {
            SharpDX.Matrix next = skeleton.Transformations[parent];
            next.Column4 = SharpDX.Vector4.UnitW;
            matrix *= next;
            parent = skeleton.ParentIndices[parent];
        }
        if (remaining < 0) throw new InvalidDataException("Cyclic skeleton hierarchy.");
        return new Matrix4x4(matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44);
    }

    private static LodVertex ReadVertex(VertexData data, int index, Matrix4x4 transform)
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
        return new LodVertex(Vector3.Transform(new Vector3(value.X, value.Y, value.Z), transform), uv);
    }

    private static LodPreviewTexture? FindTexture(ShaderFX? shader, Dictionary<string, LodPreviewTexture> textures)
    {
        ShaderParametersBlock? parameters = shader?.ParametersList;
        if (parameters is null) return null;
        for (int index = 0; index < parameters.Hashes.Length; index++)
        {
            if ((uint)parameters.Hashes[index] == DiffuseSampler &&
                parameters.Parameters[index].Data is TextureBase reference)
            {
                if (textures.TryGetValue(reference.Name, out LodPreviewTexture? texture)) return texture;
                // Clothing variants retain the model's original sampler name.
                // Resolve that diffuse family against the selected YTD, rather
                // than accidentally sampling a normal/specular map.
                int marker = reference.Name.IndexOf("_diff_", StringComparison.OrdinalIgnoreCase);
                if (marker >= 0)
                {
                    string family = reference.Name[..(marker + 5)];
                    var variants = textures.Where(pair => pair.Key.StartsWith(family, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (variants.Length == 1) return variants[0].Value;
                }
            }
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
            case TextureFormat.D3DFMT_X8R8G8B8:
                for (int index = 0; index < pixels.Length; index++) pixels[index] = BitConverter.ToInt32(data, index * 4) | unchecked((int)0xFF000000);
                break;
            case TextureFormat.D3DFMT_A8:
                for (int index = 0; index < pixels.Length; index++) pixels[index] = (data[index] << 24) | 0xFFFFFF;
                break;
            case TextureFormat.D3DFMT_L8:
                for (int index = 0; index < pixels.Length; index++) pixels[index] = unchecked((int)0xFF000000) | (data[index] << 16) | (data[index] << 8) | data[index];
                break;
            case TextureFormat.D3DFMT_A8B8G8R8:
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = (data[index * 4 + 3] << 24) | (data[index * 4] << 16) | (data[index * 4 + 1] << 8) | data[index * 4 + 2];
                break;
            default:
                using (var dds = new MemoryStream(DDSIO.GetDDSFile(texture)))
                {
                    var decoded = new BcDecoder().Decode(DdsFile.Load(dds));
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        var pixel = decoded[index];
                        pixels[index] = (pixel.a << 24) | (pixel.r << 16) | (pixel.g << 8) | pixel.b;
                    }
                }
                break;
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
                int colorIndex = (int)((colorBits >> (2 * index)) & 3);
                byte opacity = !dxt5 && c0 <= c1 && colorIndex == 3 ? (byte)0 : alpha[index];
                pixels[y * width + x] = (opacity << 24) | (colors[colorIndex] & 0xFFFFFF);
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
