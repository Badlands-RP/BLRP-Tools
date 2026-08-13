using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace BLRP.WeaponSkinTool;

internal static class InventoryImageExporter
{
    public static void SaveWebp(Bitmap model, string outputPath)
    {
        using Bitmap composed = AddShadow(model);
        BitmapData data = composed.LockBits(
            new Rectangle(0, 0, composed.Width, composed.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[composed.Width * composed.Height * 4];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            using SixLabors.ImageSharp.Image<Bgra32> image =
                SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(pixels, composed.Width, composed.Height);
            using FileStream output = File.Create(outputPath);
            image.Save(output, new WebpEncoder { Quality = 90 });
        }
        finally { composed.UnlockBits(data); }
    }

    private static Bitmap AddShadow(Bitmap model)
    {
        int width = model.Width, height = model.Height;
        int[] source = ReadPixels(model);
        var output = new int[source.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int alpha = (source[y * width + x] >>> 24) & 255;
            if (alpha < 12) continue;
            for (int oy = -2; oy <= 8; oy++)
            for (int ox = -3; ox <= 7; ox++)
            {
                int dx = ox - 2, dy = oy - 3;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > 5.5f) continue;
                int px = x + ox, py = y + oy;
                if ((uint)px >= width || (uint)py >= height) continue;
                int shadowAlpha = (int)(alpha * 0.32f * (1f - distance / 5.5f));
                int index = py * width + px;
                if (((output[index] >>> 24) & 255) < shadowAlpha) output[index] = shadowAlpha << 24;
            }
        }
        for (int index = 0; index < source.Length; index++)
            if ((source[index] & unchecked((int)0xFF000000)) != 0) output[index] = source[index];
        return WritePixels(output, width, height);
    }

    private static int[] ReadPixels(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static Bitmap WritePixels(int[] pixels, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
        return bitmap;
    }

    public static bool SelfTest()
    {
        string path = Path.Combine(Path.GetTempPath(), "BLRP-inventory-" + Guid.NewGuid().ToString("N") + ".webp");
        try
        {
            using var model = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(model))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(Brushes.CornflowerBlue, 80, 50, 90, 150);
            }
            SaveWebp(model, path);
            using SixLabors.ImageSharp.Image<Bgra32> image = SixLabors.ImageSharp.Image.Load<Bgra32>(path);
            bool hasShadow = false;
            image.ProcessPixelRows(rows =>
            {
                for (int y = 0; y < rows.Height && !hasShadow; y++)
                {
                    Span<Bgra32> row = rows.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                        if (row[x].A is > 0 and < 100) { hasShadow = true; break; }
                }
            });
            return image.Width == 256 && image.Height == 256 && image[0, 0].A == 0 && hasShadow;
        }
        finally { File.Delete(path); }
    }
}
