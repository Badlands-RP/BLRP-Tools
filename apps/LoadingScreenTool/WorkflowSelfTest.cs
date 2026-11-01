using SkiaSharp;

namespace BLRP.LoadingScreenTool;

internal static class WorkflowSelfTest
{
    internal static bool Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "BLRP-loading-test-" + Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "project");
        string active = Path.Combine(root, "active");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(active);
        try
        {
            MakeImage(Path.Combine(project, "1.webp"), SKColors.Red);
            MakeImage(Path.Combine(project, "3.webp"), SKColors.Green);
            MakeImage(Path.Combine(project, "new.png"), SKColors.Blue);
            var first = LoadingScreenWorkflow.Process(project);
            Check(first.Existing == 2 && first.Added == 1 && first.Total == 3);
            Check(IsColor(Path.Combine(project, "1.webp"), SKColors.Red));
            Check(IsColor(Path.Combine(project, "2.webp"), SKColors.Green));
            Check(IsColor(Path.Combine(project, "3.webp"), SKColors.Blue));
            Check(LoadingScreenWorkflow.Process(project).Added == 0);
            File.Delete(Path.Combine(project, "2.webp"));
            var compacted = LoadingScreenWorkflow.Process(project);
            Check(compacted.Total == 2 && IsColor(Path.Combine(project, "2.webp"), SKColors.Blue));
            MakeImage(Path.Combine(active, "4.webp"), SKColors.White);
            File.WriteAllText(Path.Combine(active, "keep.txt"), "keep");
            var exported = LoadingScreenWorkflow.Export(project, active);
            Check(exported.Copied == 2 && exported.Removed == 1);
            Check(!File.Exists(Path.Combine(active, "4.webp")) && File.Exists(Path.Combine(active, "keep.txt")));
            Check(IsColor(Path.Combine(active, "2.webp"), SKColors.Blue));
            var resized = LoadingScreenWorkflow.ResizeAll(project);
            Check(resized.Changed.Count == 3 && resized.AlreadyCorrect == 0);
            using (SKBitmap sizeCheck = SKBitmap.Decode(Path.Combine(project, "1.webp"))!)
                Check(sizeCheck.Width == 1920 && sizeCheck.Height == 1080);
            Check(LoadingScreenWorkflow.ResizeAll(project).Changed.Count == 0);
            Check(LoadingScreenWorkflow.Process(project).Added == 0);
            string editPath = Path.Combine(project, "1.webp");
            byte[] beforePreview = File.ReadAllBytes(editPath);
            var preview = LoadingScreenWorkflow.PreviewRemoval(project, ["1.webp"],
                new OverlayMask(new SKRect(0.01f, 0.01f, 0.05f, 0.05f), []));
            Check(preview.Count == 1 && beforePreview.SequenceEqual(File.ReadAllBytes(editPath)));
            Check(LoadingScreenWorkflow.SaveRemoval(project, preview).Count == 1);
            var backups = Directory.EnumerateFiles(Path.Combine(project, ".blrp-image-backups")).ToArray();
            Check(backups.Length >= 4 && backups.All(path =>
                Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)));
            using (var fixture = new SKBitmap(200, 100))
            {
                fixture.Erase(SKColors.Blue);
                using (var canvas = new SKCanvas(fixture))
                using (var paint = new SKPaint { Color = SKColors.White })
                    canvas.DrawRect(new SKRect(86, 26, 94, 34), paint);
                using SKBitmap restored = OverlayRemoval.Inpaint(fixture,
                    new OverlayMask(null, [new MaskCircle(0.45f, 0.30f, 0.05f)]));
                Check(IsNear(restored.GetPixel(90, 30), SKColors.Blue));
                Check(restored.GetPixel(10, 10) == fixture.GetPixel(10, 10));
            }
            return true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return false;
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void MakeImage(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(color);
        using SKImage image = SKImage.FromBitmap(bitmap);
        SKEncodedImageFormat format = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Webp;
        using SKData data = image.Encode(format, 100);
        using FileStream output = File.Create(path);
        data.SaveTo(output);
    }

    private static bool IsColor(string path, SKColor expected)
    {
        using SKBitmap bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException("Unreadable test image: " + path);
        SKColor actual = bitmap.GetPixel(0, 0);
        return Math.Abs(actual.Red - expected.Red) <= 6 &&
            Math.Abs(actual.Green - expected.Green) <= 6 &&
            Math.Abs(actual.Blue - expected.Blue) <= 6;
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Loading Screen Tool self-test failed.");
    }

    private static bool IsNear(SKColor actual, SKColor expected) =>
        Math.Abs(actual.Red - expected.Red) <= 6 &&
        Math.Abs(actual.Green - expected.Green) <= 6 &&
        Math.Abs(actual.Blue - expected.Blue) <= 6;
}
