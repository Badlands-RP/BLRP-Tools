using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using StbImageSharp;
using System.Text.RegularExpressions;

namespace Badlands.LiveryTool;

internal static class Paths
{
    public const string DefaultRepoRoot = @"D:\BadlandsRP";

    public static readonly string LiveryStreamRelativePath = Path.Combine(
        "resources",
        "addons",
        "stream",
        "custom_vehicle_liveries");

    public static string GetDefaultVehicleDataFolder(string repoRoot)
    {
        return Path.Combine(repoRoot, "resources", "addons", "data", "custom_vehicle_liverys");
    }

    public static string GetDefaultModkitMasterListPath(string repoRoot)
    {
        return Path.Combine(repoRoot, "resources", "addons", "! modkit master list.txt");
    }

    public static string GetDefaultLiveryStreamFolder(string repoRoot)
    {
        return Path.Combine(repoRoot, LiveryStreamRelativePath);
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
    public ImageConversionResult ConvertToDxt5Dds(string inputPath, string outputPath)
    {
        inputPath = Path.GetFullPath(inputPath);
        outputPath = Path.GetFullPath(outputPath);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input image was not found.", inputPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        using var inputStream = File.OpenRead(inputPath);
        var image = ImageResult.FromStream(inputStream, ColorComponents.RedGreenBlueAlpha);

        var encoder = new BcEncoder(CompressionFormat.Bc3)
        {
            OutputOptions =
            {
                FileFormat = OutputFileFormat.Dds,
                Format = CompressionFormat.Bc3,
                GenerateMipMaps = true,
                Quality = CompressionQuality.Balanced,
                DdsPreferDxt10Header = false,
            },
        };

        var dds = encoder.EncodeToDds(
            image.Data,
            image.Width,
            image.Height,
            PixelFormat.Rgba32);

        using (var outputStream = File.Create(outputPath))
        {
            dds.Write(outputStream);
        }

        return new ImageConversionResult(
            outputPath,
            image.Width,
            image.Height,
            new FileInfo(outputPath).Length,
            ReadDdsFourCc(outputPath));
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

internal sealed record LiverySlotGroup(
    string Prefix,
    string ExistingFileNumbers,
    int NextFileNumber,
    int NextLuaSlot,
    int Count);

internal sealed partial class LiveryScanner
{
    public IReadOnlyList<LiverySlotGroup> Scan(string repoRoot)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        var liveryDir = Path.Combine(repoRoot, Paths.LiveryStreamRelativePath);

        if (!Directory.Exists(liveryDir))
        {
            throw new DirectoryNotFoundException($"Livery stream folder was not found: {liveryDir}");
        }

        return Directory
            .EnumerateFiles(liveryDir, "*.yft", SearchOption.TopDirectoryOnly)
            .Select(path => new LiveryAsset(path))
            .Where(asset => asset.Number is not null)
            .GroupBy(asset => asset.Prefix, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var numbers = group
                    .Select(asset => asset.Number!.Value)
                    .Order()
                    .ToArray();

                return new LiverySlotGroup(
                    group.Key,
                    string.Join(", ", numbers),
                    numbers.Last() + 1,
                    numbers.Last(),
                    numbers.Length);
            })
            .ToArray();
    }

    private sealed partial class LiveryAsset
    {
        private static readonly Regex FilePattern = LiveryFileRegex();

        public LiveryAsset(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var match = FilePattern.Match(name);

            Prefix = match.Success ? match.Groups["prefix"].Value : name;
            Number = match.Success ? int.Parse(match.Groups["number"].Value) : null;
        }

        public string Prefix { get; }
        public int? Number { get; }

        [GeneratedRegex("^(?<prefix>.+?_(?:liv|livery)_?)(?<number>\\d+)$", RegexOptions.IgnoreCase)]
        private static partial Regex LiveryFileRegex();
    }
}
