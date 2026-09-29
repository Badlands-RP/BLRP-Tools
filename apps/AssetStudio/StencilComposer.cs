using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BLRP.WeaponSkinTool;

internal static class StencilComposer
{
    public static Bitmap Preview(string backgroundPath, string sprayPath, int scalePercent, int offsetX, int offsetY)
    {
        using SixLabors.ImageSharp.Image<Rgba32> image = Compose(backgroundPath, sprayPath, scalePercent, offsetX, offsetY);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    }

    public static void SaveWebp(string backgroundPath, string sprayPath, int scalePercent, int offsetX, int offsetY, string outputPath)
    {
        using SixLabors.ImageSharp.Image<Rgba32> image = Compose(backgroundPath, sprayPath, scalePercent, offsetX, offsetY);
        image.Save(outputPath, new WebpEncoder { Quality = 90 });
    }

    private static SixLabors.ImageSharp.Image<Rgba32> Compose(
        string backgroundPath, string sprayPath, int scalePercent, int offsetX, int offsetY)
    {
        using SixLabors.ImageSharp.Image<Rgba32> background = SixLabors.ImageSharp.Image.Load<Rgba32>(backgroundPath);
        using SixLabors.ImageSharp.Image<Rgba32> spray = SixLabors.ImageSharp.Image.Load<Rgba32>(sprayPath);
        int targetSize = Math.Max(1, Math.Min(background.Width, background.Height) * scalePercent / 100);
        float resize = Math.Min((float)targetSize / spray.Width, (float)targetSize / spray.Height);
        int width = Math.Max(1, (int)MathF.Round(spray.Width * resize));
        int height = Math.Max(1, (int)MathF.Round(spray.Height * resize));
        spray.Mutate(context => context.Resize(width, height));

        int left = (background.Width - width) / 2 + offsetX;
        int top = (background.Height - height) / 2 + offsetY;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int destinationX = left + x, destinationY = top + y;
            if ((uint)destinationX >= background.Width || (uint)destinationY >= background.Height) continue;
            Rgba32 source = spray[x, y];
            if (source.A == 0) continue;
            Rgba32 destination = background[destinationX, destinationY];
            float sourceAlpha = source.A / 255f;
            float destinationAlpha = destination.A / 255f;
            float alpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
            background[destinationX, destinationY] = new Rgba32(
                Blend(source.R, destination.R, sourceAlpha, destinationAlpha, alpha),
                Blend(source.G, destination.G, sourceAlpha, destinationAlpha, alpha),
                Blend(source.B, destination.B, sourceAlpha, destinationAlpha, alpha),
                (byte)MathF.Round(alpha * 255f));
        }
        return background.Clone();
    }

    private static byte Blend(byte source, byte destination, float sourceAlpha, float destinationAlpha, float alpha) =>
        alpha == 0 ? (byte)0 : (byte)MathF.Round((source * sourceAlpha + destination * destinationAlpha * (1f - sourceAlpha)) / alpha);

    public static bool SelfTest()
    {
        string id = Guid.NewGuid().ToString("N");
        string backgroundPath = Path.Combine(Path.GetTempPath(), $"BLRP-stencil-background-{id}.png");
        string sprayPath = Path.Combine(Path.GetTempPath(), $"BLRP-stencil-spray-{id}.png");
        string outputPath = Path.Combine(Path.GetTempPath(), $"BLRP-stencil-output-{id}.webp");
        try
        {
            using (var background = new SixLabors.ImageSharp.Image<Rgba32>(8, 8, new Rgba32(255, 255, 255, 255))) background.SaveAsPng(backgroundPath);
            using (var spray = new SixLabors.ImageSharp.Image<Rgba32>(2, 1, new Rgba32(100, 20, 200, 255))) spray.SaveAsPng(sprayPath);
            SaveWebp(backgroundPath, sprayPath, 50, 0, 0, outputPath);
            using SixLabors.ImageSharp.Image<Rgba32> result = SixLabors.ImageSharp.Image.Load<Rgba32>(outputPath);
            return result.Width == 8 && result.Height == 8 && result[4, 4].B > result[4, 4].R && result[0, 0].R > 200;
        }
        finally
        {
            File.Delete(backgroundPath);
            File.Delete(sprayPath);
            File.Delete(outputPath);
        }
    }
}
