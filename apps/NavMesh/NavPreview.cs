using System.Drawing.Drawing2D;
using System.Globalization;
using Vector3 = SharpDX.Vector3;

namespace BLRP.NavMesh;

internal sealed class NavPreview : Control
{
    private Vector3[][] polygons = [];
    private PointF center;
    private float extent = 1;
    private float zoom = 1;
    private Point? drag;
    public float? FloorHeight { get; set; }
    public int PolygonCount => polygons.Length;

    public NavPreview()
    {
        DoubleBuffered = true;
        BackColor = BlrpTheme.Background;
        ForeColor = BlrpTheme.AccentLight;
        ResizeRedraw = true;
        Cursor = Cursors.Cross;
        AccessibleName = "Navigation preview, top view";
        MouseWheel += (_, e) => { zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f), 0.2f, 100); Invalidate(); };
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { drag = e.Location; Capture = true; Focus(); } };
        MouseMove += (_, e) =>
        {
            if (drag is not { } start) return;
            float scale = ScaleFactor;
            center.X -= (e.X - start.X) / scale; center.Y += (e.Y - start.Y) / scale;
            drag = e.Location; Invalidate();
        };
        MouseUp += (_, _) => { drag = null; Capture = false; };
        DoubleClick += (_, _) => Fit();
    }

    public void LoadObj(string path)
    {
        var vertices = new List<Vector3>(); var faces = new List<Vector3[]>();
        foreach (string line in File.ReadLines(path))
        {
            string[] values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 4 && values[0] == "v")
                vertices.Add(new Vector3(float.Parse(values[1], CultureInfo.InvariantCulture), float.Parse(values[2], CultureInfo.InvariantCulture), float.Parse(values[3], CultureInfo.InvariantCulture)));
            else if (values.Length >= 4 && values[0] == "f")
                faces.Add(values.Skip(1).Select(v => vertices[int.Parse(v, CultureInfo.InvariantCulture) - 1]).ToArray());
        }
        polygons = faces.ToArray(); Fit();
    }

    public void Clear() { polygons = []; Invalidate(); }

    public void Fit()
    {
        var vertices = polygons.SelectMany(p => p).ToArray();
        if (vertices.Length == 0) return;
        float minX = vertices.Min(p => p.X), maxX = vertices.Max(p => p.X);
        float minY = vertices.Min(p => p.Y), maxY = vertices.Max(p => p.Y);
        center = new PointF((minX + maxX) / 2, (minY + maxY) / 2);
        extent = Math.Max(1, Math.Max(maxX - minX, maxY - minY)); zoom = 1;
        Invalidate();
    }

    private float ScaleFactor => Math.Max(1, Math.Min(Width, Height) - 56) / extent * zoom;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (polygons.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, "Generate a preview to inspect walkable surfaces.", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = ScaleFactor;
        using var fill = new SolidBrush(Color.FromArgb(60, BlrpTheme.Accent));
        using var line = new Pen(Color.FromArgb(170, BlrpTheme.AccentLight), 0.8f);
        int shown = 0;
        foreach (var polygon in polygons)
        {
            if (FloorHeight is { } z && (polygon.Min(v => v.Z) > z + 0.5f || polygon.Max(v => v.Z) < z - 0.5f)) continue;
            var points = polygon.Select(v => new PointF(Width / 2f + (v.X - center.X) * scale, Height / 2f - (v.Y - center.Y) * scale)).ToArray();
            e.Graphics.FillPolygon(fill, points); e.Graphics.DrawPolygon(line, points); shown++;
        }
        TextRenderer.DrawText(e.Graphics, $"TOP VIEW  ·  {shown:N0} faces  ·  north ↑\nScroll to zoom · drag to pan · double-click to fit", Font,
            new Rectangle(10, 10, Width - 20, 48), ForeColor, BackColor, TextFormatFlags.WordBreak);
    }
}
