using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;

namespace BLRP.WeaponSkinTool;

internal sealed class ModelPreview : Control
{
    private PreviewScene? _scene;
    private Bitmap? _frame;
    private Point _lastMouse;
    private float _yaw = -0.65f;
    private float _pitch = 0.35f;
    private float _tilt;
    private float _defaultTilt;
    private float _zoom = 1f;
    public string EmptyMessage { get; set; } = "SELECT A YDR + YTD, THEN LOAD PREVIEW";

    public ModelPreview()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14, 14, 30);
        ForeColor = Color.FromArgb(180, 200, 215, 240);
        Resize += (_, _) => Render();
        MouseDown += (_, e) => _lastMouse = e.Location;
        MouseMove += (_, e) =>
        {
            if (e.Button == MouseButtons.None) return;
            if (e.Button == MouseButtons.Right)
                _tilt += (e.X - _lastMouse.X) * 0.012f;
            else if (e.Button == MouseButtons.Left)
            {
                _yaw += (e.X - _lastMouse.X) * 0.012f;
                _pitch = Math.Clamp(_pitch + (e.Y - _lastMouse.Y) * 0.012f, -1.5f, 1.5f);
            }
            _lastMouse = e.Location;
            Render();
        };
        MouseWheel += (_, e) =>
        {
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.12f : 0.89f), 0.35f, 4f);
            Render();
        };
    }

    public async Task LoadAsync(string modelPath, string? texturePath, string? replacementImage = null, string? replacementTop = null, string? replacementLod = null, float initialTilt = 0f)
    {
        _scene = null;
        _frame?.Dispose();
        _frame = null;
        Invalidate();
        _scene = await Task.Run(() => PreviewScene.Load(modelPath, texturePath, replacementImage, replacementTop, replacementLod));
        _yaw = -0.65f;
        _pitch = 0.35f;
        _defaultTilt = _tilt = initialTilt;
        _zoom = 1f;
        Render();
    }

    public void SaveInventoryImage(string outputPath)
    {
        if (_scene is null) throw new InvalidOperationException("Load and pose a model before saving its inventory image.");
        using Bitmap model = _scene.Render(256, 256, _yaw, _pitch, _zoom, true, _tilt);
        InventoryImageExporter.SaveWebp(model, outputPath);
    }

    public void ResetView()
    {
        _yaw = -0.65f;
        _pitch = 0.35f;
        _tilt = _defaultTilt;
        _zoom = 1f;
        Render();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_frame is not null)
        {
            e.Graphics.DrawImageUnscaled(_frame, 0, 0);
            return;
        }
        string text = _scene is null ? EmptyMessage : "RENDERING...";
        TextRenderer.DrawText(e.Graphics, text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void Render()
    {
        if (_scene is null || ClientSize.Width < 8 || ClientSize.Height < 8) return;
        Bitmap next = _scene.Render(ClientSize.Width, ClientSize.Height, _yaw, _pitch, _zoom, false, _tilt);
        Bitmap? old = _frame;
        _frame = next;
        old?.Dispose();
        Invalidate();
    }
}

internal sealed record PreviewVertex(Vector3 Position, Vector2 UV);
internal sealed record PreviewTriangle(PreviewVertex A, PreviewVertex B, PreviewVertex C, PreviewTexture Texture);

internal sealed class PreviewTexture
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

internal sealed class PreviewScene
{
    private const uint DiffuseSampler = 4059966321;
    private readonly PreviewTriangle[] _triangles;
    private readonly Vector3 _center;
    private readonly float _radius;

    private PreviewScene(PreviewTriangle[] triangles, Vector3 center, float radius)
    {
        _triangles = triangles;
        _center = center;
        _radius = radius;
    }

    public static PreviewScene Load(string modelPath, string? texturePath, string? replacementImage = null, string? replacementTop = null, string? replacementLod = null)
    {
        bool isYdr = Path.GetExtension(modelPath).Equals(".ydr", StringComparison.OrdinalIgnoreCase);
        byte[] modelData = isYdr && string.IsNullOrWhiteSpace(texturePath) &&
            (!string.IsNullOrWhiteSpace(replacementImage) || !string.IsNullOrWhiteSpace(replacementTop) || !string.IsNullOrWhiteSpace(replacementLod))
            ? WeaponTextureBuilder.BuildEmbedded(modelPath, replacementImage, replacementTop, replacementLod)
            : File.ReadAllBytes(modelPath);
        DrawableBase drawable = LoadDrawable(modelPath, modelData);
        Texture[] sourceTextures;
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            sourceTextures = drawable.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? [];
        }
        else
        {
            var ytd = new YtdFile();
            ytd.Load(!isYdr || string.IsNullOrWhiteSpace(replacementImage)
                ? File.ReadAllBytes(texturePath)
                : WeaponTextureBuilder.Build(modelPath, texturePath, replacementImage));
            sourceTextures = ytd.TextureDict?.Textures?.data_items ?? [];
        }
        if (sourceTextures.Length == 0) throw new InvalidDataException("The model has no available textures.");
        if (!isYdr && !string.IsNullOrWhiteSpace(replacementImage))
            WeaponTextureBuilder.ApplyDiffuse(drawable, sourceTextures, replacementImage);
        var textures = new Dictionary<string, PreviewTexture>(StringComparer.OrdinalIgnoreCase);
        foreach (Texture texture in sourceTextures)
        {
            try { textures[texture.Name] = DecodeTexture(texture); }
            catch (NotSupportedException) { textures[texture.Name] = SolidTexture(Color.DimGray); }
        }
        PreviewTexture fallback = textures.GetValueOrDefault("coffee_main")
            ?? textures.FirstOrDefault(pair => pair.Key.EndsWith("_d", StringComparison.OrdinalIgnoreCase)).Value
            ?? textures.FirstOrDefault(pair => !pair.Key.Contains("normal", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Contains("bump", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Contains("spec", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Equals("blank", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.EndsWith("_n", StringComparison.OrdinalIgnoreCase)).Value
            ?? textures.Values.First();
        var triangles = new List<PreviewTriangle>();
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);

        foreach (DrawableModel model in drawable.DrawableModels?.High ?? [])
        foreach (DrawableGeometry geometry in model.Geometries ?? [])
        {
            VertexData data = geometry.VertexData ?? throw new InvalidDataException("A geometry has no vertex data.");
            ushort[] indices = geometry.IndexBuffer?.Indices ?? [];
            PreviewTexture texture = FindTexture(geometry.Shader, textures) ?? fallback;
            for (int offset = 0; offset + 2 < indices.Length; offset += 3)
            {
                PreviewVertex a = ReadVertex(data, indices[offset]);
                PreviewVertex b = ReadVertex(data, indices[offset + 1]);
                PreviewVertex c = ReadVertex(data, indices[offset + 2]);
                min = Vector3.Min(min, Vector3.Min(a.Position, Vector3.Min(b.Position, c.Position)));
                max = Vector3.Max(max, Vector3.Max(a.Position, Vector3.Max(b.Position, c.Position)));
                triangles.Add(new PreviewTriangle(a, b, c, texture));
            }
        }
        if (triangles.Count == 0) throw new InvalidDataException("The model has no high-detail triangles to preview.");
        Vector3 center = (min + max) * 0.5f;
        float radius = triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Max(vertex => Vector3.Distance(center, vertex.Position));
        return new PreviewScene(triangles.ToArray(), center, Math.Max(0.001f, radius));
    }

    private static DrawableBase LoadDrawable(string path, byte[] data)
    {
        if (data.AsSpan().StartsWith("FXAP"u8))
            throw new InvalidDataException("Asset Escrow-protected models cannot be previewed.");
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ydr" => LoadYdr(data),
            ".ydd" => LoadYdd(data),
            ".yft" => LoadYft(data),
            _ => throw new InvalidDataException("Choose a YDR, YDD, or YFT model file.")
        };
    }

    private static DrawableBase LoadYdr(byte[] data)
    {
        var file = new YdrFile();
        file.Load(data);
        return file.Drawable ?? throw new InvalidDataException("The YDR has no drawable.");
    }

    private static DrawableBase LoadYdd(byte[] data)
    {
        var file = new YddFile();
        file.Load(data);
        return file.Drawables?.FirstOrDefault(drawable => drawable.DrawableModels?.High?.Length > 0)
            ?? file.Drawables?.FirstOrDefault() ?? throw new InvalidDataException("The YDD has no drawables.");
    }

    private static DrawableBase LoadYft(byte[] data)
    {
        var file = new YftFile();
        file.Load(data);
        DrawableBase? main = file.Fragment?.Drawable;
        return (main?.DrawableModels?.High?.Length > 0 ? main : null)
            ?? file.Fragment?.DrawableArray?.data_items?.FirstOrDefault(drawable => drawable.DrawableModels?.High?.Length > 0)
            ?? main ?? file.Fragment?.DrawableArray?.data_items?.FirstOrDefault()
            ?? throw new InvalidDataException("The YFT has no drawable.");
    }

    public Bitmap Render(int width, int height, float yaw, float pitch, float zoom, bool transparent = false, float tilt = 0f)
    {
        int[] pixels = Enumerable.Repeat(transparent ? 0 : Color.FromArgb(14, 14, 30).ToArgb(), width * height).ToArray();
        float[] depth = Enumerable.Repeat(float.PositiveInfinity, width * height).ToArray();
        Matrix4x4 rotation = Matrix4x4.CreateRotationZ(yaw) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(tilt);
        var projected = new List<(PreviewTriangle Triangle, ScreenVertex A, ScreenVertex B, ScreenVertex C)>(_triangles.Length);
        foreach (PreviewTriangle triangle in _triangles)
        {
            Vector3 a = Vector3.Transform(triangle.A.Position - _center, rotation);
            Vector3 b = Vector3.Transform(triangle.B.Position - _center, rotation);
            Vector3 c = Vector3.Transform(triangle.C.Position - _center, rotation);
            projected.Add((triangle, new(a.X, a.Z, a.Y, triangle.A.UV), new(b.X, b.Z, b.Y, triangle.B.UV), new(c.X, c.Z, c.Y, triangle.C.UV)));
        }
        float scale = Math.Min(width, height) * 0.44f / _radius * zoom;
        foreach (var item in projected)
        {
            ScreenVertex a = item.A.ToScreen(width, height, scale);
            ScreenVertex b = item.B.ToScreen(width, height, scale);
            ScreenVertex c = item.C.ToScreen(width, height, scale);
            Rasterize(pixels, depth, width, height, a, b, c, item.Triangle.Texture);
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, locked.Scan0, pixels.Length);
        bitmap.UnlockBits(locked);
        return bitmap;
    }

    private static void Rasterize(int[] pixels, float[] depth, int width, int height, ScreenVertex a, ScreenVertex b, ScreenVertex c, PreviewTexture texture)
    {
        float area = Edge(a, b, c.X, c.Y);
        if (Math.Abs(area) < 0.001f) return;
        int minX = Math.Clamp((int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1);
        int minY = Math.Clamp((int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
        float light = Math.Clamp(0.45f + 0.55f * Math.Abs(Vector3.Normalize(Vector3.Cross(
            new Vector3(b.X - a.X, b.Y - a.Y, b.Depth - a.Depth),
            new Vector3(c.X - a.X, c.Y - a.Y, c.Depth - a.Depth))).Z), 0.35f, 1f);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float wa = Edge(b, c, px, py) / area;
            float wb = Edge(c, a, px, py) / area;
            float wc = 1f - wa - wb;
            if (wa < 0 || wb < 0 || wc < 0) continue;
            float z = wa * a.Depth + wb * b.Depth + wc * c.Depth;
            int index = y * width + x;
            if (z >= depth[index]) continue;
            int color = texture.Sample(wa * a.UV.X + wb * b.UV.X + wc * c.UV.X, wa * a.UV.Y + wb * b.UV.Y + wc * c.UV.Y);
            int alpha = (color >> 24) & 255;
            if (alpha < 16) continue;
            int red = (int)(((color >> 16) & 255) * light);
            int green = (int)(((color >> 8) & 255) * light);
            int blue = (int)((color & 255) * light);
            pixels[index] = (alpha << 24) | (red << 16) | (green << 8) | blue;
            depth[index] = z;
        }
    }

    private static float Edge(ScreenVertex a, ScreenVertex b, float x, float y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

    private static PreviewVertex ReadVertex(VertexData data, int index)
    {
        SharpDX.Vector3 p = data.GetVector3(index, 0);
        Vector2 uv = Vector2.Zero;
        if ((data.Info.Flags & (1 << 6)) != 0)
        {
            if (data.Info.GetComponentType(6) == VertexComponentType.Half2)
            {
                SharpDX.Half2 h = data.GetHalf2(index, 6);
                uv = new Vector2((float)h.X, (float)h.Y);
            }
            else
            {
                SharpDX.Vector2 f = data.GetVector2(index, 6);
                uv = new Vector2(f.X, f.Y);
            }
        }
        return new PreviewVertex(new Vector3(p.X, p.Y, p.Z), uv);
    }

    private static PreviewTexture? FindTexture(ShaderFX? shader, Dictionary<string, PreviewTexture> textures)
    {
        ShaderParametersBlock? parameters = shader?.ParametersList;
        if (parameters is null) return null;
        for (int index = 0; index < parameters.Hashes.Length; index++)
        {
            if ((uint)parameters.Hashes[index] == DiffuseSampler && parameters.Parameters[index].Data is TextureBase reference &&
                textures.TryGetValue(reference.Name, out PreviewTexture? texture)) return texture;
        }
        for (int index = 0; index < parameters.Hashes.Length; index++)
        {
            if (parameters.Parameters[index].Data is TextureBase reference &&
                textures.TryGetValue(reference.Name, out PreviewTexture? texture)) return texture;
        }
        return null;
    }

    private static PreviewTexture SolidTexture(Color color) => new()
    {
        Width = 2,
        Height = 2,
        Pixels = Enumerable.Repeat(color.ToArgb(), 4).ToArray()
    };

    internal static PreviewTexture DecodeTexture(Texture texture)
    {
        int width = texture.Width, height = texture.Height;
        byte[] data = texture.Data?.FullData ?? throw new InvalidDataException($"Texture {texture.Name} has no pixel data.");
        int[] pixels = new int[width * height];
        switch (texture.Format)
        {
            case TextureFormat.D3DFMT_DXT1: DecodeBlocks(data, width, height, pixels, false); break;
            case TextureFormat.D3DFMT_DXT5: DecodeBlocks(data, width, height, pixels, true); break;
            case TextureFormat.D3DFMT_A8R8G8B8:
                for (int i = 0; i < pixels.Length; i++) pixels[i] = BitConverter.ToInt32(data, i * 4);
                break;
            default: throw new NotSupportedException($"Preview does not yet support texture format {texture.Format}.");
        }
        return new PreviewTexture { Width = width, Height = height, Pixels = pixels };
    }

    private static void DecodeBlocks(byte[] data, int width, int height, int[] pixels, bool dxt5)
    {
        int offset = 0;
        for (int by = 0; by < height; by += 4)
        for (int bx = 0; bx < width; bx += 4)
        {
            byte[] alpha = Enumerable.Repeat((byte)255, 16).ToArray();
            if (dxt5)
            {
                byte a0 = data[offset], a1 = data[offset + 1];
                byte[] palette = new byte[8]; palette[0] = a0; palette[1] = a1;
                if (a0 > a1) for (int i = 1; i <= 6; i++) palette[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                else { for (int i = 1; i <= 4; i++) palette[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5); palette[6] = 0; palette[7] = 255; }
                ulong bits = 0; for (int i = 0; i < 6; i++) bits |= (ulong)data[offset + 2 + i] << (8 * i);
                for (int i = 0; i < 16; i++) alpha[i] = palette[(bits >> (3 * i)) & 7];
                offset += 8;
            }
            ushort c0 = BitConverter.ToUInt16(data, offset), c1 = BitConverter.ToUInt16(data, offset + 2);
            int[] colors = BuildPalette(c0, c1, dxt5);
            uint colorBits = BitConverter.ToUInt32(data, offset + 4);
            for (int py = 0; py < 4; py++)
            for (int px = 0; px < 4; px++)
            {
                int x = bx + px, y = by + py, i = py * 4 + px;
                if (x >= width || y >= height) continue;
                pixels[y * width + x] = (alpha[i] << 24) | (colors[(colorBits >> (2 * i)) & 3] & 0xFFFFFF);
            }
            offset += 8;
        }
    }

    private static int[] BuildPalette(ushort c0, ushort c1, bool opaque)
    {
        int C(ushort c) => (((c >> 11) * 255 / 31) << 16) | ((((c >> 5) & 63) * 255 / 63) << 8) | ((c & 31) * 255 / 31);
        int a = C(c0), b = C(c1);
        int Blend(int x, int y, int wx, int wy, int divisor) =>
            (((((x >> 16) & 255) * wx + ((y >> 16) & 255) * wy) / divisor) << 16) |
            (((((x >> 8) & 255) * wx + ((y >> 8) & 255) * wy) / divisor) << 8) |
            (((x & 255) * wx + (y & 255) * wy) / divisor);
        return c0 > c1 || opaque ? [a, b, Blend(a, b, 2, 1, 3), Blend(a, b, 1, 2, 3)] : [a, b, Blend(a, b, 1, 1, 2), 0];
    }

    private readonly record struct ScreenVertex(float X, float Y, float Depth, Vector2 UV)
    {
        public ScreenVertex ToScreen(int width, int height, float scale) => new(width * 0.5f + X * scale, height * 0.5f - Y * scale, Depth, UV);
    }
}
