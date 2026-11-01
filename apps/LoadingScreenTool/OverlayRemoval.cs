using SkiaSharp;

namespace BLRP.LoadingScreenTool;

internal static class OverlayRemoval
{
    // Copy a nearby clean patch whose edge best matches the target's surroundings.
    // This works well for small HUD overlays against scenery; the UI asks for visual review.
    internal static SKBitmap FillFromNearby(SKBitmap source, SKRect normalized)
    {
        int x = Math.Clamp((int)Math.Floor(normalized.Left * source.Width), 0, source.Width - 1);
        int y = Math.Clamp((int)Math.Floor(normalized.Top * source.Height), 0, source.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(normalized.Right * source.Width), x + 1, source.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(normalized.Bottom * source.Height), y + 1, source.Height);
        int width = right - x, height = bottom - y;
        int bestX = -1, bestY = -1;
        double bestScore = double.PositiveInfinity;
        foreach (int distance in new[] { 1, 2, 3 })
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1),
                     (1, -1), (-1, 1), (1, 1) })
        {
            int sx = x + dx * width * distance;
            int sy = y + dy * height * distance;
            if (sx < 0 || sy < 0 || sx + width > source.Width || sy + height > source.Height) continue;
            double score = BorderScore(source, x, y, sx, sy, width, height);
            if (score >= bestScore) continue;
            bestScore = score;
            bestX = sx;
            bestY = sy;
        }
        if (bestX < 0) throw new InvalidOperationException("The selected area is too large to fill from nearby pixels.");

        var output = new SKBitmap(source.Width, source.Height);
        using (var canvas = new SKCanvas(output))
        {
            canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
            canvas.DrawBitmap(source, new SKRect(bestX, bestY, bestX + width, bestY + height),
                new SKRect(x, y, right, bottom), SKSamplingOptions.Default);
            canvas.Flush();
        }
        return output;
    }

    private static double BorderScore(SKBitmap image, int x, int y, int sx, int sy, int width, int height)
    {
        double total = 0;
        int count = 0;
        for (int i = 0; i < 16; i++)
        {
            int px = x + i * (width - 1) / 15;
            int qx = sx + i * (width - 1) / 15;
            if (y > 0) { total += Difference(image.GetPixel(px, y - 1), image.GetPixel(qx, sy)); count++; }
            if (y + height < image.Height) { total += Difference(image.GetPixel(px, y + height), image.GetPixel(qx, sy + height - 1)); count++; }
            int py = y + i * (height - 1) / 15;
            int qy = sy + i * (height - 1) / 15;
            if (x > 0) { total += Difference(image.GetPixel(x - 1, py), image.GetPixel(sx, qy)); count++; }
            if (x + width < image.Width) { total += Difference(image.GetPixel(x + width, py), image.GetPixel(sx + width - 1, qy)); count++; }
        }
        return total / Math.Max(1, count);
    }

    private static int Difference(SKColor a, SKColor b) =>
        Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
}
