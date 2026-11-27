using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using BLRP.LiveryPreview;

internal static class Program
{
    private static int result = 1;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--paint-check") return PaintChecks.Run(args[1], args[2]);
        if (args.Length is < 2 or > 3) return 2;
        bool compatibility = args.Length == 3;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory(args[1]);
        string plain = Path.Combine(args[1], "plain.png"), image = Path.Combine(args[1], "background.png"), transparent = Path.Combine(args[1], "transparent.png");
        string source = Path.Combine(args[1], "background-source.png");
        using (var bitmap = new Bitmap(64, 64))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(52, 84, 130));
            bitmap.Save(source, ImageFormat.Png);
        }
        using var surface = new RenderSurface { Size = new Size(1024, 640), Opacity = 0, ShowInTaskbar = false };
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var clock = Stopwatch.StartNew();
        int phase = 0; long firstFrame = 0;
        surface.Failed += exception => { Console.Error.WriteLine(exception); surface.Close(); };
        surface.Shown += (_, _) =>
        {
            surface.LoadVehicle(args[0], true);
            clock.Restart(); timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("Preview capture timed out.");
                if (phase == 0 && clock.Elapsed.TotalSeconds > 3)
                {
                    surface.Screenshot(plain, false); phase = 1;
                }
                else if (phase == 1 && File.Exists(plain))
                {
                    surface.SetBackgroundImage(source); firstFrame = surface.FrameCount; clock.Restart(); phase = 2;
                }
                else if (phase == 2 && clock.Elapsed.TotalSeconds > 2)
                {
                    double fps = (surface.FrameCount - firstFrame) / clock.Elapsed.TotalSeconds;
                    Console.WriteLine($"Offscreen frame processing: {fps:F1} frames/sec (not display refresh rate)");
                    if (fps < 20) throw new Exception("GPU background rendering fell below 20 frames/sec.");
                    surface.Screenshot(image, false); phase = 3;
                }
                else if (phase == 3 && File.Exists(image)) { surface.Screenshot(transparent, true); phase = 4; }
                else if (phase == 4 && File.Exists(transparent))
                {
                    if (compatibility)
                    {
                        using var rendered = new Bitmap(plain);
                        using var alpha = new Bitmap(transparent);
                        Color background = rendered.GetPixel(2, 2);
                        int visible = 0;
                        for (int y = 0; y < rendered.Height; y += 2)
                        for (int x = 0; x < rendered.Width; x += 2)
                        {
                            Color pixel = rendered.GetPixel(x, y);
                            if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B) > 60) visible++;
                        }
                        if (visible < 1000) throw new Exception("Vehicle did not produce enough visible rendered geometry: " + visible);
                        Console.WriteLine($"PASS actual vehicle rendered on the GPU ({visible} foreground samples).");
                        if (args[2] == "--emissive" && surface.EmissivePaintGeometryCount == 0) throw new Exception("Emissive paint adapter was not applied.");
                        if (surface.EmissivePaintGeometryCount > 0) Console.WriteLine($"PASS emissive vehicle paint configured on {surface.EmissivePaintGeometryCount} geometries.");
                        if (alpha.GetPixel(2, 2).A != 0) throw new Exception("Transparent screenshot background is not transparent.");
                    }
                    else Verify(plain, image, transparent);
                    result = 0; surface.Close();
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); surface.Close(); }
        };
        Application.Run(surface);
        return result;
    }

    private static void Verify(string plain, string image, string transparent)
    {
        using var original = new Bitmap(plain);
        using var backdrop = new Bitmap(image);
        using var alpha = new Bitmap(transparent);
        Color corner = backdrop.GetPixel(2, 2);
        if (Math.Abs(corner.R - 52) > 2 || Math.Abs(corner.G - 84) > 2 || Math.Abs(corner.B - 130) > 2)
            throw new Exception("The image background did not reach the GPU framebuffer.");
        int body = 0, preserved = 0;
        for (int y = 0; y < original.Height; y += 2)
        for (int x = 0; x < original.Width; x += 2)
        {
            Color first = original.GetPixel(x, y), second = backdrop.GetPixel(x, y);
            if (first.R < 180 || first.G < 180 || first.B < 180) continue;
            body++;
            if (Math.Abs(first.R - second.R) < 4 && Math.Abs(first.G - second.G) < 4 && Math.Abs(first.B - second.B) < 4) preserved++;
        }
        if (body < 1000 || preserved < body * 0.9) throw new Exception($"Background covered the car: {preserved}/{body} body samples preserved.");
        if (alpha.GetPixel(2, 2).A != 0) throw new Exception("Transparent screenshots included the image background.");
        Console.WriteLine($"PASS GPU background is behind the car ({preserved}/{body} opaque body samples preserved).");
        Console.WriteLine("PASS transparent screenshots omit the background image.");
    }
}
