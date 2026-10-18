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
}
