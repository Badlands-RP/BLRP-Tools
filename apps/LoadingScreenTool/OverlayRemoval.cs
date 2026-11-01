using System.Runtime.InteropServices;
using OpenCvSharp;
using SkiaSharp;

namespace BLRP.LoadingScreenTool;

internal sealed record MaskCircle(float X, float Y, float Radius);
internal sealed record OverlayMask(SKRect? Rectangle, IReadOnlyList<MaskCircle> Circles);

internal static class OverlayRemoval
{
    internal static SKBitmap Inpaint(SKBitmap source, OverlayMask selection)
    {
        using SKImage image = SKImage.FromBitmap(source);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100) ??
            throw new InvalidDataException("Could not prepare image for cleanup.");
        using Mat original = Cv2.ImDecode(data.ToArray(), ImreadModes.Color);
        using var mask = new Mat(source.Height, source.Width, MatType.CV_8UC1, Scalar.Black);
        if (selection.Rectangle is SKRect rectangle)
        {
            int left = Math.Clamp((int)Math.Floor(rectangle.Left * source.Width), 0, source.Width - 1);
            int top = Math.Clamp((int)Math.Floor(rectangle.Top * source.Height), 0, source.Height - 1);
            int right = Math.Clamp((int)Math.Ceiling(rectangle.Right * source.Width), left + 1, source.Width);
            int bottom = Math.Clamp((int)Math.Ceiling(rectangle.Bottom * source.Height), top + 1, source.Height);
            Cv2.Rectangle(mask, new OpenCvSharp.Rect(left, top, right - left, bottom - top), Scalar.White, -1);
        }
        foreach (MaskCircle circle in selection.Circles)
        {
            int x = Math.Clamp((int)Math.Round(circle.X * source.Width), 0, source.Width - 1);
            int y = Math.Clamp((int)Math.Round(circle.Y * source.Height), 0, source.Height - 1);
            int radius = Math.Max(1, (int)Math.Round(circle.Radius * source.Width));
            Cv2.Circle(mask, new OpenCvSharp.Point(x, y), radius, Scalar.White, -1);
        }
        int count = Cv2.CountNonZero(mask);
        if (count == 0) throw new InvalidOperationException("Paint a small area over the overlay first.");
        if (count > source.Width * source.Height * 0.025)
            throw new InvalidOperationException("The marked area covers more than 2.5% of the image. Use a smaller brush or selection.");

        using var inpainted = new Mat();
        Cv2.Inpaint(original, mask, inpainted, 3, InpaintTypes.Telea);
        if (!Cv2.ImEncode(".png", inpainted, out byte[] png))
            throw new InvalidDataException("Could not prepare the cleanup preview.");
        using SKBitmap restored = SKBitmap.Decode(png) ?? throw new InvalidDataException("Could not read cleanup preview.");

        var output = new SKBitmap(source.Width, source.Height);
        using (var canvas = new SKCanvas(output)) canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        byte[] maskBytes = new byte[source.Width * source.Height];
        Marshal.Copy(mask.Data, maskBytes, 0, maskBytes.Length);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            if (maskBytes[y * source.Width + x] == 0) continue;
            SKColor old = source.GetPixel(x, y);
            SKColor fill = restored.GetPixel(x, y);
            output.SetPixel(x, y, new SKColor(fill.Red, fill.Green, fill.Blue, old.Alpha));
        }
        return output;
    }
}
