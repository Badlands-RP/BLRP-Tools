using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using CodeWalker.GameFiles;
using StbImageSharp;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Badlands.LiveryTool;

internal sealed record SignBatchItem(
    string SourcePath,
    string SourceName,
    string TargetName,
    string Input,
    string Status,
    bool CanBuild);

internal sealed record SignBatchResult(IReadOnlyList<string> Files, IReadOnlyList<string> Messages);

internal sealed class SignBatchWorkflow
{
    private const string BuiltInResourceSuffix = ".Assets.SignTemplate.yft.xml";
    private readonly LiveryImageConverter imageConverter = new();

    public IReadOnlyList<SignBatchItem> CreatePlan(
        string sourceFolder,
        string outputFolder,
        string sourcePrefix,
        string outputPrefix,
        int startNumber)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"Source folder was not found: {sourceFolder}");
        }

        ValidateOutputName(outputPrefix, "Output prefix");
        if (startNumber < 0)
        {
            throw new InvalidOperationException("Starting number must be zero or greater.");
        }

        string[] sources = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(sourcePrefix) ||
                           Path.GetFileNameWithoutExtension(path).StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => SortNumber(path, sourcePrefix))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sources.Length == 0)
        {
            throw new InvalidOperationException("No matching PNG or DDS files were found in the source folder.");
        }

        outputFolder = Path.GetFullPath(outputFolder);
        return sources.Select((path, index) =>
        {
            string targetName = $"{outputPrefix}{startNumber + index}";
            string? problem = InspectSource(path);
            bool exists = File.Exists(Path.Combine(outputFolder, targetName + ".yft")) ||
                          File.Exists(Path.Combine(outputFolder, targetName + ".dds"));
            string status = problem ?? (exists ? "OUTPUT EXISTS" : "READY");
            return new SignBatchItem(
                path,
                Path.GetFileName(path),
                targetName,
                Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
                status,
                problem is null && !exists);
        }).ToArray();
    }

    public SignBatchResult Build(
        IReadOnlyList<SignBatchItem> plan,
        string outputFolder,
        string? customTemplatePath,
        string templateToken)
    {
        if (plan.Count == 0 || plan.Any(item => !item.CanBuild))
        {
            throw new InvalidOperationException("Preview a valid batch with no errors or existing outputs before building.");
        }

        if (string.IsNullOrWhiteSpace(templateToken))
        {
            throw new InvalidOperationException("Template token is required.");
        }

        string template = LoadTemplate(customTemplatePath);
        if (!template.Contains(templateToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The template does not contain the token '{templateToken}'.");
        }

        outputFolder = Path.GetFullPath(outputFolder);
        string staging = Path.Combine(Path.GetTempPath(), "BLRP-SignBatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            foreach (SignBatchItem item in plan)
            {
                string ddsPath = Path.Combine(staging, item.TargetName + ".dds");
                PrepareDds(item.SourcePath, ddsPath);
                DdsInfo info = ReadDdsInfo(ddsPath);

                string xml = template.Replace(templateToken, item.TargetName, StringComparison.Ordinal);
                xml = UpdateTextureMetadata(xml, info);
                YftFile yft = XmlYft.GetYft(xml, staging);
                File.WriteAllBytes(Path.Combine(staging, item.TargetName + ".yft"), yft.Save());
            }

            Directory.CreateDirectory(outputFolder);
            var files = new List<string>();
            foreach (string stagedFile in Directory.EnumerateFiles(staging).Order(StringComparer.OrdinalIgnoreCase))
            {
                string target = Path.Combine(outputFolder, Path.GetFileName(stagedFile));
                File.Copy(stagedFile, target, overwrite: false);
                files.Add(target);
            }

            return new SignBatchResult(
                files,
                [$"Built {plan.Count} sign liveries.", $"Output: {outputFolder}"]);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private void PrepareDds(string sourcePath, string outputPath)
    {
        if (sourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            imageConverter.ConvertToDxt5Dds(sourcePath, outputPath);
            return;
        }

        DdsInfo info = ReadDdsInfo(sourcePath);
        if (string.Equals(info.FourCc, "DXT5", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, outputPath, overwrite: true);
            return;
        }

        using var input = File.OpenRead(sourcePath);
        DdsFile source = DdsFile.Load(input);
        DdsFace face = source.Faces.FirstOrDefault() ?? throw new InvalidDataException("DDS has no image face.");
        ColorRgba32[] pixels = new BcDecoder().Decode(source);
        var rgba = new byte[pixels.Length * 4];
        for (int index = 0; index < pixels.Length; index++)
        {
            rgba[index * 4] = pixels[index].r;
            rgba[index * 4 + 1] = pixels[index].g;
            rgba[index * 4 + 2] = pixels[index].b;
            rgba[index * 4 + 3] = pixels[index].a;
        }

        var encoder = CreateDxt5Encoder();
        DdsFile converted = encoder.EncodeToDds(rgba, checked((int)face.Width), checked((int)face.Height), PixelFormat.Rgba32);
        using var output = File.Create(outputPath);
        converted.Write(output);
    }

    private static BcEncoder CreateDxt5Encoder() => new(CompressionFormat.Bc3)
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

    private static string? InspectSource(string path)
    {
        try
        {
            int width;
            int height;
            if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                DdsInfo info = ReadDdsInfo(path);
                width = info.Width;
                height = info.Height;
            }
            else
            {
                using var stream = File.OpenRead(path);
                ImageInfo info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("PNG header could not be read.");
                width = info.Width;
                height = info.Height;
            }

            return IsPowerOfTwo(width) && IsPowerOfTwo(height)
                ? null
                : $"INVALID SIZE {width}x{height} (POWER OF TWO REQUIRED)";
        }
        catch (Exception ex)
        {
            return "INVALID: " + ex.Message;
        }
    }

    private static DdsInfo ReadDdsInfo(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        if (stream.Length < 128 || reader.ReadUInt32() != 0x20534444)
        {
            throw new InvalidDataException($"Not a valid DDS file: {Path.GetFileName(path)}");
        }

        stream.Position = 12;
        int height = reader.ReadInt32();
        int width = reader.ReadInt32();
        stream.Position = 28;
        int mipLevels = Math.Max(1, reader.ReadInt32());
        stream.Position = 84;
        string fourCc = Encoding.ASCII.GetString(reader.ReadBytes(4));
        return new DdsInfo(width, height, mipLevels, fourCc);
    }

    private static string UpdateTextureMetadata(string xml, DdsInfo info)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement texture = document.Descendants("TextureDictionary").Elements("Item").FirstOrDefault() ??
            throw new InvalidDataException("The sign template has no embedded texture entry.");
        SetValue(texture, "Width", info.Width);
        SetValue(texture, "Height", info.Height);
        SetValue(texture, "MipLevels", info.MipLevels);
        texture.Element("Format")!.Value = "D3DFMT_DXT5";
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static void SetValue(XElement parent, string name, int value)
    {
        XElement element = parent.Element(name) ?? throw new InvalidDataException($"The sign template is missing {name}.");
        element.SetAttributeValue("value", value);
    }

    private static string LoadTemplate(string? customTemplatePath)
    {
        if (!string.IsNullOrWhiteSpace(customTemplatePath))
        {
            return File.ReadAllText(customTemplatePath);
        }

        Assembly assembly = typeof(SignBatchWorkflow).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(BuiltInResourceSuffix, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException("The built-in sign template is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int SortNumber(string path, string sourcePrefix)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string suffix = !string.IsNullOrWhiteSpace(sourcePrefix) && name.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
            ? name[sourcePrefix.Length..]
            : name;
        return int.TryParse(suffix.TrimStart('_', '-', ' '), out int number) ? number : int.MaxValue;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static void ValidateOutputName(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new InvalidOperationException($"{label} may contain only letters, numbers, underscores, and hyphens.");
        }
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed record DdsInfo(int Width, int Height, int MipLevels, string FourCc);
}
