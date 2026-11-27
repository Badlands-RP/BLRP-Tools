using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using StbImageSharp;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Badlands.LiveryTool;

internal static class Paths
{
    public static string DefaultRepoRoot { get; } = DiscoverRepository();

    private static string DiscoverRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "BadlandsRP");
            if (Directory.Exists(Path.Combine(candidate, "resources"))) return candidate;
        }
        string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitHub", "BadlandsRP");
        return Directory.Exists(Path.Combine(documents, "resources")) ? documents : "";
    }

}

internal sealed record ImageConversionResult(
    string OutputPath,
    int Width,
    int Height,
    long OutputBytes,
    string FourCc);

internal sealed class LiveryImageConverter
{
    public ImageConversionResult ConvertForTemplate(string inputPath, string outputPath, int width, int height, bool fitImage, CancellationToken cancellation)
    {
        if (width < 4 || height < 4 || width > 8192 || height > 8192 || (width & (width - 1)) != 0 || (height & (height - 1)) != 0)
            throw new InvalidDataException("Template needs power-of-two dimensions between 4 and 8192 pixels.");
        using var input = File.OpenRead(inputPath);
        ImageInfo info = ImageInfo.FromStream(input) ?? throw new InvalidDataException("Image header could not be read (use PNG, JPG, TGA or BMP).");
        if (info.Width > 16384 || info.Height > 16384 || (long)info.Width * info.Height > 67_108_864)
            throw new InvalidDataException("Source image exceeds the 64-megapixel limit. Resize it before importing.");
        input.Position = 0;
        var image = ImageResult.FromStream(input, ColorComponents.RedGreenBlueAlpha);
        cancellation.ThrowIfCancellationRequested();
        byte[] pixels = image.Data;
        if (image.Width != width || image.Height != height)
        {
            if (!fitImage) throw new InvalidDataException($"Image is {image.Width}×{image.Height}; template needs {width}×{height}. Enable Fit image or resize the artwork.");
            // Letterbox into a transparent canvas; never stretch artwork or discard alpha.
            using var source = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            BitmapData sourceBits = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[source.Width * 4];
                for (int y = 0; y < source.Height; y++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Buffer.BlockCopy(pixels, y * row.Length, row, 0, row.Length);
                    for (int i = 0; i < row.Length; i += 4) (row[i], row[i + 2]) = (row[i + 2], row[i]);
                    Marshal.Copy(row, 0, IntPtr.Add(sourceBits.Scan0, y * sourceBits.Stride), row.Length);
                }
            }
            finally { source.UnlockBits(sourceBits); }
            using var resized = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float scale = Math.Min((float)width / image.Width, (float)height / image.Height);
                float w = image.Width * scale, h = image.Height * scale;
                graphics.DrawImage(source, (width - w) / 2, (height - h) / 2, w, h);
            }
            pixels = new byte[checked(width * height * 4)];
            BitmapData resultBits = resized.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Marshal.Copy(IntPtr.Add(resultBits.Scan0, y * resultBits.Stride), row, 0, row.Length);
                    for (int i = 0; i < row.Length; i += 4) (row[i], row[i + 2]) = (row[i + 2], row[i]);
                    Buffer.BlockCopy(row, 0, pixels, y * row.Length, row.Length);
                }
            }
            finally { resized.UnlockBits(resultBits); }
        }
        var encoder = new BcEncoder(CompressionFormat.Bc3)
        {
            OutputOptions = { FileFormat = OutputFileFormat.Dds, Format = CompressionFormat.Bc3, GenerateMipMaps = true,
                Quality = CompressionQuality.Balanced, DdsPreferDxt10Header = false }
        };
        var dds = encoder.EncodeToDdsAsync(pixels, width, height, BCnEncoder.Encoder.PixelFormat.Rgba32, cancellation).GetAwaiter().GetResult();
        cancellation.ThrowIfCancellationRequested();
        using (var output = File.Create(outputPath)) dds.Write(output);
        byte[] bytes = File.ReadAllBytes(outputPath);
        int expectedMips = 1 + (int)Math.Log2(Math.Max(width, height));
        if (ReadDdsFourCc(outputPath) != "DXT5" || BitConverter.ToInt32(bytes, 28) != expectedMips)
            throw new InvalidDataException("DDS compression/mipmap verification failed.");
        return new ImageConversionResult(outputPath, width, height, bytes.Length, "DXT5");
    }

    public ImageConversionResult ConvertToDxt5Dds(string inputPath, string outputPath)
    {
        using var inputStream = File.OpenRead(inputPath);
        ImageInfo info = ImageInfo.FromStream(inputStream) ?? throw new InvalidDataException("Image header could not be read.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        return ConvertForTemplate(inputPath, outputPath, info.Width, info.Height, false, default);
    }

    private static string ReadDdsFourCc(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < 88)
        {
            return "unknown";
        }

        return System.Text.Encoding.ASCII.GetString(bytes, 84, 4);
    }
}
