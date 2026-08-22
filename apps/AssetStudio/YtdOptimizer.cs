using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;

namespace BLRP.WeaponSkinTool;

internal static class YtdOptimizer
{
    public static void Optimize(string inputPath, string outputPath)
    {
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(inputPath));
        Texture[] textures = ytd.TextureDict?.Textures?.data_items ?? [];

        foreach (Texture texture in textures)
        {
            if (Math.Max(texture.Width, texture.Height) < 512) continue;
            int width = Math.Max(1, texture.Width / 2);
            int height = Math.Max(1, texture.Height / 2);

            if (texture.Levels > 1)
            {
                int topLevelBytes = LevelSize(texture.Format, texture.Width, texture.Height);
                byte[] source = texture.Data?.FullData ?? throw new InvalidDataException($"Texture {texture.Name} has no data.");
                texture.Data = new TextureData { FullData = source[topLevelBytes..] };
                texture.Levels--;
            }
            else
            {
                PreviewTexture decoded = PreviewScene.DecodeTexture(texture);
                using Bitmap source = ToBitmap(decoded);
                using Bitmap resized = Resize(source, width, height);
                texture.Data = new TextureData { FullData = WeaponTextureBuilder.EncodeMipChain(resized, texture.Format, 1) };
            }

            texture.Width = checked((ushort)width);
            texture.Height = checked((ushort)height);
            texture.Stride = checked((ushort)Stride(texture.Format, width));
        }

        ytd.TextureDict!.BuildFromTextureList(textures.ToList());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, ytd.Save());
    }

    private static int LevelSize(TextureFormat format, int width, int height) => format switch
    {
        TextureFormat.D3DFMT_DXT1 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8,
        TextureFormat.D3DFMT_DXT5 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
        TextureFormat.D3DFMT_A8R8G8B8 => width * height * 4,
        _ => throw new NotSupportedException($"YTD optimisation does not support {format}.")
    };

    private static int Stride(TextureFormat format, int width) => format switch
    {
        TextureFormat.D3DFMT_DXT1 => Math.Max(1, width / 2),
        TextureFormat.D3DFMT_DXT5 => width,
        TextureFormat.D3DFMT_A8R8G8B8 => width * 4,
        _ => throw new NotSupportedException($"YTD optimisation does not support {format}.")
    };

    private static Bitmap ToBitmap(PreviewTexture texture)
    {
        var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(texture.Pixels, y * bitmap.Width, data.Scan0 + y * data.Stride, bitmap.Width);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return result;
    }
}
