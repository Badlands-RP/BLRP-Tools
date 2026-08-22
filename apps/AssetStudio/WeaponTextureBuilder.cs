using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace BLRP.WeaponSkinTool;

internal static class WeaponTextureBuilder
{
    private const uint DiffuseSampler = 4059966321;

    public static byte[] Build(string modelPath, string templateYtdPath, string imagePath)
    {
        var ydr = new YdrFile();
        ydr.Load(File.ReadAllBytes(modelPath));
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(templateYtdPath));
        Texture[] textures = ytd.TextureDict?.Textures?.data_items ?? [];
        if (textures.Length == 0) throw new InvalidDataException("The template YTD contains no textures.");
        Texture diffuse = FindDiffuse(ydr.Drawable, textures) ??
            textures.FirstOrDefault(texture => texture.Name.EndsWith("_d", StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException("Could not identify the diffuse texture used by the model.");
        ReplaceDiffuse(diffuse, imagePath);
        ytd.TextureDict!.BuildFromTextureList(textures.ToList());
        return ytd.Save();
    }

    public static byte[] BuildEmbedded(string modelPath, string? imagePath, string? topPath = null, string? lodPath = null)
    {
        var ydr = new YdrFile();
        ydr.Load(File.ReadAllBytes(modelPath));
        TextureDictionary dictionary = ydr.Drawable?.ShaderGroup?.TextureDictionary ??
            throw new InvalidDataException("The YDR has no embedded texture dictionary.");
        Texture[] textures = dictionary.Textures?.data_items ?? [];
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            Texture diffuse = textures.FirstOrDefault(texture => texture.Name.Equals("coffee_main", StringComparison.OrdinalIgnoreCase)) ??
                FindDiffuse(ydr.Drawable, textures) ??
                textures.FirstOrDefault(texture => texture.Name.EndsWith("_d", StringComparison.OrdinalIgnoreCase)) ??
                textures.FirstOrDefault() ??
                throw new InvalidDataException("The YDR contains no embedded diffuse texture.");
            ReplaceDiffuse(diffuse, imagePath);
        }
        ReplaceNamed(textures, "coffee_top", topPath);
        ReplaceNamed(textures, "coffee_lod", lodPath);
        dictionary.BuildFromTextureList(textures.ToList());
        return ydr.Save();
    }

    private static void ReplaceNamed(Texture[] textures, string name, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return;
        Texture texture = textures.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException($"The YDR has no embedded {name} texture.");
        ReplaceDiffuse(texture, imagePath);
    }

    private static void ReplaceDiffuse(Texture diffuse, string imagePath)
    {
        if (Path.GetExtension(imagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            Texture dds = DDSIO.GetTexture(File.ReadAllBytes(imagePath));
            ValidateDimensions(dds.Width, dds.Height, diffuse.Width, diffuse.Height, "DDS");
            if (dds.Data?.FullData is not { Length: > 0 })
                throw new InvalidDataException("The DDS contains no texture data.");
            diffuse.Width = dds.Width;
            diffuse.Height = dds.Height;
            diffuse.Depth = dds.Depth;
            diffuse.Levels = dds.Levels;
            diffuse.Format = dds.Format;
            diffuse.Stride = dds.Stride;
            diffuse.Data = new TextureData { FullData = dds.Data.FullData.ToArray() };
            return;
        }

        if (!Path.GetExtension(imagePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The replacement texture must be a PNG or DDS file.");
        using var source = new Bitmap(imagePath);
        ValidateDimensions(source.Width, source.Height, diffuse.Width, diffuse.Height, "PNG");
        using Bitmap image = ToArgb(source, diffuse.Width, diffuse.Height);
        const TextureFormat format = TextureFormat.D3DFMT_DXT5;
        byte levels = (byte)Math.Clamp(diffuse.Levels, 1, FullMipCount(image.Width, image.Height));
        diffuse.Width = checked((ushort)image.Width);
        diffuse.Height = checked((ushort)image.Height);
        diffuse.Depth = 1;
        diffuse.Levels = levels;
        diffuse.Format = format;
        diffuse.Stride = checked((ushort)(format switch
        {
            TextureFormat.D3DFMT_DXT1 => Math.Max(1, image.Width / 2),
            TextureFormat.D3DFMT_DXT5 => image.Width,
            _ => image.Width * 4
        }));
        diffuse.Data = new TextureData { FullData = EncodeMipChain(image, format, levels) };
    }

    internal static void ApplyDiffuse(DrawableBase drawable, Texture[] textures, string imagePath)
    {
        Texture diffuse = FindDiffuse(drawable, textures) ??
            textures.FirstOrDefault(texture => texture.Name.EndsWith("_d", StringComparison.OrdinalIgnoreCase)) ??
            textures.FirstOrDefault() ?? throw new InvalidDataException("The model has no diffuse texture.");
        ReplaceDiffuse(diffuse, imagePath);
    }

    private static Texture? FindDiffuse(DrawableBase? drawable, Texture[] textures)
    {
        if (drawable is null) return null;
        var byName = textures.ToDictionary(texture => texture.Name, StringComparer.OrdinalIgnoreCase);
        foreach (DrawableModel model in drawable.DrawableModels?.High ?? [])
        foreach (DrawableGeometry geometry in model.Geometries ?? [])
        {
            ShaderParametersBlock? parameters = geometry.Shader?.ParametersList;
            if (parameters is null) continue;
            for (int index = 0; index < parameters.Hashes.Length; index++)
            {
                if ((uint)parameters.Hashes[index] == DiffuseSampler &&
                    parameters.Parameters[index].Data is TextureBase reference &&
                    byName.TryGetValue(reference.Name, out Texture? texture)) return texture;
            }
            for (int index = 0; index < parameters.Hashes.Length; index++)
            {
                if (parameters.Parameters[index].Data is TextureBase reference &&
                    byName.TryGetValue(reference.Name, out Texture? texture)) return texture;
            }
        }
        return null;
    }

    private static void ValidateDimensions(int width, int height, int templateWidth, int templateHeight, string kind)
    {
        if (width < 4 || height < 4 || width > 8192 || height > 8192 || !IsPowerOfTwo(width) || !IsPowerOfTwo(height))
            throw new InvalidDataException($"The {kind} must use power-of-two dimensions between 4 and 8192 pixels; received {width}x{height}.");
        if ((long)width * templateHeight != (long)height * templateWidth)
            throw new InvalidDataException($"The {kind} aspect ratio must match the template diffuse texture ({templateWidth}x{templateHeight}); received {width}x{height}.");
    }

    private static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0;
    private static int FullMipCount(int width, int height) => 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));

    private static Bitmap ToArgb(Image source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return result;
    }

    internal static byte[] EncodeMipChain(Bitmap source, TextureFormat format, int levels)
    {
        using var output = new MemoryStream();
        Bitmap current = (Bitmap)source.Clone();
        try
        {
            for (int level = 0; level < levels; level++)
            {
                int[] pixels = ReadPixels(current);
                byte[] encoded = format switch
                {
                    TextureFormat.D3DFMT_DXT1 => EncodeDxt(pixels, current.Width, current.Height, false),
                    TextureFormat.D3DFMT_DXT5 => EncodeDxt(pixels, current.Width, current.Height, true),
                    _ => pixels.SelectMany(BitConverter.GetBytes).ToArray()
                };
                output.Write(encoded);
                if (level + 1 >= levels) break;
                var next = new Bitmap(Math.Max(1, current.Width / 2), Math.Max(1, current.Height / 2), PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(next))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(current, new Rectangle(0, 0, next.Width, next.Height));
                }
                current.Dispose();
                current = next;
            }
        }
        finally { current.Dispose(); }
        return output.ToArray();
    }

    private static int[] ReadPixels(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            if (data.Stride == bitmap.Width * 4) Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            else
            {
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width);
            }
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static byte[] EncodeDxt(int[] pixels, int width, int height, bool dxt5)
    {
        using var output = new MemoryStream(((width + 3) / 4) * ((height + 3) / 4) * (dxt5 ? 16 : 8));
        var block = new int[16];
        for (int by = 0; by < height; by += 4)
        for (int bx = 0; bx < width; bx += 4)
        {
            for (int py = 0; py < 4; py++)
            for (int px = 0; px < 4; px++)
                block[py * 4 + px] = pixels[Math.Min(height - 1, by + py) * width + Math.Min(width - 1, bx + px)];
            if (dxt5) WriteAlphaBlock(output, block);
            WriteColorBlock(output, block);
        }
        return output.ToArray();
    }

    private static void WriteAlphaBlock(Stream output, int[] block)
    {
        byte min = 255, max = 0;
        foreach (int color in block) { byte alpha = (byte)(color >> 24); min = Math.Min(min, alpha); max = Math.Max(max, alpha); }
        if (max == min) min = max == 0 ? (byte)1 : (byte)(max - 1);
        byte[] palette = new byte[8]; palette[0] = max; palette[1] = min;
        for (int i = 1; i <= 6; i++) palette[i + 1] = (byte)(((7 - i) * max + i * min) / 7);
        ulong bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int alpha = (block[i] >> 24) & 255;
            int best = 0, distance = int.MaxValue;
            for (int p = 0; p < 8; p++) { int d = Math.Abs(alpha - palette[p]); if (d < distance) { distance = d; best = p; } }
            bits |= (ulong)best << (i * 3);
        }
        output.WriteByte(max); output.WriteByte(min);
        for (int i = 0; i < 6; i++) output.WriteByte((byte)(bits >> (i * 8)));
    }

    private static void WriteColorBlock(Stream output, int[] block)
    {
        int first = block[0], second = block[0], farthest = -1;
        for (int a = 0; a < 16; a++)
        for (int b = a + 1; b < 16; b++)
        {
            int distance = ColorDistance(block[a], block[b]);
            if (distance > farthest) { farthest = distance; first = block[a]; second = block[b]; }
        }
        ushort c0 = To565(first), c1 = To565(second);
        if (c0 < c1) (c0, c1) = (c1, c0);
        if (c0 == c1) { if (c0 < ushort.MaxValue) c0++; else c1--; }
        int[] palette = BuildPalette(c0, c1);
        uint bits = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, distance = int.MaxValue;
            for (int p = 0; p < 4; p++) { int d = ColorDistance(block[i], palette[p]); if (d < distance) { distance = d; best = p; } }
            bits |= (uint)best << (i * 2);
        }
        output.Write(BitConverter.GetBytes(c0));
        output.Write(BitConverter.GetBytes(c1));
        output.Write(BitConverter.GetBytes(bits));
    }

    private static ushort To565(int color) => (ushort)((((color >> 19) & 31) << 11) | (((color >> 10) & 63) << 5) | ((color >> 3) & 31));
    private static int ColorDistance(int a, int b)
    {
        int r = ((a >> 16) & 255) - ((b >> 16) & 255), g = ((a >> 8) & 255) - ((b >> 8) & 255), blue = (a & 255) - (b & 255);
        return r * r + g * g + blue * blue;
    }

    private static int[] BuildPalette(ushort c0, ushort c1)
    {
        int C(ushort c) => (255 << 24) | (((c >> 11) * 255 / 31) << 16) | ((((c >> 5) & 63) * 255 / 63) << 8) | ((c & 31) * 255 / 31);
        int a = C(c0), b = C(c1);
        int Blend(int x, int y, int wx, int wy) => (255 << 24) |
            (((((x >> 16) & 255) * wx + ((y >> 16) & 255) * wy) / 3) << 16) |
            (((((x >> 8) & 255) * wx + ((y >> 8) & 255) * wy) / 3) << 8) |
            (((x & 255) * wx + (y & 255) * wy) / 3);
        return [a, b, Blend(a, b, 2, 1), Blend(a, b, 1, 2)];
    }

    public static bool SelfTest()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BLRP-Weapon-Texture-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string png = Path.Combine(directory, "submission.png");
            using (var image = new Bitmap(256, 512, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Teal);
                using var brush = new SolidBrush(Color.FromArgb(180, 255, 80, 160));
                graphics.FillEllipse(brush, 32, 64, 192, 192);
                image.Save(png, ImageFormat.Png);
            }
            string template = BundledAssets.BatTemplate();
            byte[] rebuilt = Build(template + ".ydr", template + ".ytd", png);
            var ytd = new YtdFile();
            ytd.Load(rebuilt);
            Texture? diffuse = ytd.TextureDict?.Textures?.data_items?
                .FirstOrDefault(texture => texture.Name.EndsWith("_d", StringComparison.OrdinalIgnoreCase));
            bool weaponPassed = diffuse is { Width: 512, Height: 1024, Format: TextureFormat.D3DFMT_DXT5 } &&
                diffuse.Data?.FullData.Length == 512 * 1024;

            string cupPng = Path.Combine(directory, "cup-wrap.png");
            using (var image = new Bitmap(512, 256, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.CornflowerBlue);
                graphics.FillRectangle(Brushes.White, 128, 32, 256, 192);
                image.Save(cupPng, ImageFormat.Png);
            }
            string cupTemplate = BundledAssets.CupTemplate();
            string cupOutput = Path.Combine(directory, "created-cup.ydr");
            File.WriteAllBytes(cupOutput, BuildEmbedded(cupTemplate, cupPng));
            var cup = new YdrFile();
            cup.Load(File.ReadAllBytes(cupOutput));
            Texture[] cupTextures = cup.Drawable?.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? [];
            Texture? cupMain = cupTextures.FirstOrDefault(texture => texture.Name == "coffee_main");
            Texture? cupTop = cupTextures.FirstOrDefault(texture => texture.Name == "coffee_top");
            using Bitmap cupPreview = PreviewScene.Load(cupOutput, null).Render(128, 128, -0.65f, 0.35f, 1f);
            if (!weaponPassed || cupMain is not { Width: 512, Height: 256, Format: TextureFormat.D3DFMT_DXT5 } ||
                cupTop is not { Format: TextureFormat.D3DFMT_A8R8G8B8 }) return false;

            string topPng = Path.Combine(directory, "coffee_top.png");
            string lodPng = Path.Combine(directory, "coffee_lod.png");
            using (var image = new Bitmap(256, 128)) { using Graphics graphics = Graphics.FromImage(image); graphics.Clear(Color.DarkSlateBlue); image.Save(topPng, ImageFormat.Png); }
            using (var image = new Bitmap(256, 256)) { using Graphics graphics = Graphics.FromImage(image); graphics.Clear(Color.DarkOrange); image.Save(lodPng, ImageFormat.Png); }
            var fullCup = new YdrFile();
            fullCup.Load(BuildEmbedded(cupTemplate, cupPng, topPng, lodPng));
            Texture[] fullTextures = fullCup.Drawable?.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? [];
            if (fullTextures.FirstOrDefault(texture => texture.Name == "coffee_top")?.Format != TextureFormat.D3DFMT_DXT5 ||
                fullTextures.FirstOrDefault(texture => texture.Name == "coffee_lod")?.Format != TextureFormat.D3DFMT_DXT5) return false;

            string cupDds = Path.Combine(directory, "cup-wrap.dds");
            File.WriteAllBytes(cupDds, DDSIO.GetDDSFile(cupMain));
            var ddsCup = new YdrFile();
            ddsCup.Load(BuildEmbedded(cupTemplate, cupDds));
            Texture? ddsMain = ddsCup.Drawable?.ShaderGroup?.TextureDictionary?.Textures?.data_items?
                .FirstOrDefault(texture => texture.Name == "coffee_main");
            return ddsMain is { Width: 512, Height: 256, Format: TextureFormat.D3DFMT_DXT5 } &&
                ddsMain.Data?.FullData.SequenceEqual(cupMain.Data!.FullData) == true;
        }
        finally { Directory.Delete(directory, true); }
    }
}
