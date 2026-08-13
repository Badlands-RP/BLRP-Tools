using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace Badlands.LiveryTool;

internal sealed record VehicleMetadataSearchResult(
    string ModelName,
    IReadOnlyList<VehicleMetadataBlock> Vehicles,
    IReadOnlyList<VehicleMetadataBlock> Variations,
    IReadOnlyList<VehicleMetadataBlock> Kits,
    IReadOnlyList<VehicleMetadataSourceResult> Sources)
{
    public bool HasMatches => Vehicles.Count > 0 || Variations.Count > 0 || Kits.Count > 0;
}

internal sealed record VehicleMetadataSourceResult(
    string SourceKey,
    string DisplayName,
    IReadOnlyList<VehicleMetadataBlock> Vehicles,
    IReadOnlyList<VehicleMetadataBlock> Variations,
    IReadOnlyList<VehicleMetadataBlock> Kits)
{
    public bool HasMatches => Vehicles.Count > 0 || Variations.Count > 0 || Kits.Count > 0;

    public override string ToString()
    {
        return $"{DisplayName} - vehicles {Vehicles.Count}, carvar {Variations.Count}, carcols {Kits.Count}";
    }
}

internal sealed record VehicleMetadataBlock(string Kind, string SourceKey, string SourceDisplayName, string SourcePath, string Name, string Xml);

internal sealed class VehicleMetadataFinder
{
    public static bool InsertVehiclesBlock(string targetPath, string xml, bool createBackup)
    {
        var modelName = GetChildValue(xml, "modelName");
        return InsertBlock(targetPath, "InitDatas", xml, modelName, "modelName", createBackup);
    }

    public static bool InsertCarVariationsBlock(string targetPath, string xml, bool createBackup)
    {
        var modelName = GetChildValue(xml, "modelName");
        return InsertBlock(targetPath, "variationData", xml, modelName, "modelName", createBackup);
    }

    public static bool InsertCarColsKitBlock(string targetPath, string xml, bool createBackup)
    {
        var kitName = GetChildValue(xml, "kitName");
        return InsertBlock(targetPath, "Kits", xml, kitName, "kitName", createBackup);
    }

    public VehicleMetadataSearchResult Find(string gtaFolder, string modelName, Action<string>? updateStatus = null)
    {
        if (string.IsNullOrWhiteSpace(gtaFolder))
        {
            throw new InvalidOperationException("GTA V folder is required.");
        }

        if (!Directory.Exists(gtaFolder))
        {
            throw new DirectoryNotFoundException($"GTA V folder was not found: {gtaFolder}");
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new InvalidOperationException("Vehicle model name is required.");
        }

        modelName = modelName.Trim();
        updateStatus?.Invoke("Loading GTA archive keys...");
        GTA5Keys.LoadFromPath(gtaFolder, gen9: false, key: null);

        var errors = new List<string>();
        var manager = new RpfManager();
        updateStatus?.Invoke("Indexing GTA RPF archives...");
        manager.Init(
            gtaFolder,
            gen9: false,
            updateStatus: _ => { },
            errorLog: error => errors.Add(error),
            rootOnly: false,
            buildIndex: false);

        if (manager.AllRpfs.Count == 0)
        {
            var suffix = errors.Count == 0 ? string.Empty : $" First error: {errors[0]}";
            throw new InvalidOperationException($"No readable GTA RPF archives were found.{suffix}");
        }

        updateStatus?.Invoke($"Searching {manager.AllRpfs.Count:N0} RPF archives...");

        var vehicles = new List<VehicleMetadataBlock>();
        var variations = new List<VehicleMetadataBlock>();
        var kitHashes = new HashSet<uint>();

        foreach (var entry in EnumerateEntries(manager, "vehicles.meta"))
        {
            var xml = TryGetXmlText(entry);
            if (xml is null)
            {
                continue;
            }

            foreach (var item in FindItemsByChildText(xml, "modelName", modelName))
            {
                vehicles.Add(CreateBlock("vehicles.meta", entry.Path, modelName, item));
            }
        }

        foreach (var entry in EnumerateEntries(manager, "carvariations.meta", "carvariations.ymt"))
        {
            var xml = TryLoadCarVariationsXml(entry);
            if (xml is null)
            {
                continue;
            }

            foreach (var item in FindItemsByChildText(xml, "modelName", modelName))
            {
                variations.Add(CreateBlock("carvariations", entry.Path, modelName, item.ToString()));
                foreach (var kitHash in ExtractKitHashes(item))
                {
                    kitHashes.Add(kitHash);
                }
            }
        }

        var kits = new List<VehicleMetadataBlock>();
        if (kitHashes.Count > 0)
        {
            foreach (var entry in EnumerateEntries(manager, "carcols.meta", "carcols.ymt"))
            {
                var xml = TryLoadCarColsXml(entry);
                if (xml is null)
                {
                    continue;
                }

                foreach (var kit in FindKits(xml, kitHashes))
                {
                    var kitName = kit.Element("kitName")?.Value.Trim() ?? "(unknown kit)";
                    kits.Add(CreateBlock("carcols kit", entry.Path, kitName, kit.ToString()));
                }
            }
        }

        return new VehicleMetadataSearchResult(modelName, vehicles, variations, kits, BuildSources(vehicles, variations, kits));
    }

    public static string Format(VehicleMetadataSearchResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Vehicle metadata search: {result.ModelName}");
        sb.AppendLine($"vehicles.meta blocks: {result.Vehicles.Count}");
        sb.AppendLine($"carvariations blocks: {result.Variations.Count}");
        sb.AppendLine($"carcols kit blocks: {result.Kits.Count}");
        sb.AppendLine();

        AppendBlocks(sb, result.Vehicles);
        AppendBlocks(sb, result.Variations);
        AppendBlocks(sb, result.Kits);

        if (!result.HasMatches)
        {
            sb.AppendLine("No matching base-game metadata was found for this model.");
        }

        return sb.ToString();
    }

    public static string Format(VehicleMetadataSourceResult source)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Source: {source.DisplayName}");
        sb.AppendLine($"vehicles.meta blocks: {source.Vehicles.Count}");
        sb.AppendLine($"carvariations blocks: {source.Variations.Count}");
        sb.AppendLine($"carcols kit blocks: {source.Kits.Count}");
        sb.AppendLine();

        AppendBlocks(sb, source.Vehicles);
        AppendBlocks(sb, source.Variations);
        AppendBlocks(sb, source.Kits);

        if (!source.HasMatches)
        {
            sb.AppendLine("No blocks were found for this source.");
        }

        return sb.ToString();
    }

    private static void AppendBlocks(StringBuilder sb, IReadOnlyList<VehicleMetadataBlock> blocks)
    {
        foreach (var block in blocks)
        {
            sb.AppendLine($"===== {block.Kind}: {block.Name}");
            sb.AppendLine($"Source: {block.SourcePath}");
            sb.AppendLine(block.Xml);
            sb.AppendLine();
        }
    }

    private static VehicleMetadataBlock CreateBlock(string kind, string sourcePath, string name, string xml)
    {
        var sourceKey = GetSourceKey(sourcePath);
        return new VehicleMetadataBlock(kind, sourceKey, GetSourceDisplayName(sourceKey), sourcePath, name, xml);
    }

    private static IReadOnlyList<VehicleMetadataSourceResult> BuildSources(
        IReadOnlyList<VehicleMetadataBlock> vehicles,
        IReadOnlyList<VehicleMetadataBlock> variations,
        IReadOnlyList<VehicleMetadataBlock> kits)
    {
        return vehicles
            .Concat(variations)
            .Concat(kits)
            .GroupBy(block => block.SourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new VehicleMetadataSourceResult(
                group.Key,
                group.First().SourceDisplayName,
                vehicles.Where(block => string.Equals(block.SourceKey, group.Key, StringComparison.OrdinalIgnoreCase)).ToArray(),
                variations.Where(block => string.Equals(block.SourceKey, group.Key, StringComparison.OrdinalIgnoreCase)).ToArray(),
                kits.Where(block => string.Equals(block.SourceKey, group.Key, StringComparison.OrdinalIgnoreCase)).ToArray()))
            .OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetSourceKey(string path)
    {
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "dlcpacks", StringComparison.OrdinalIgnoreCase))
            {
                return $"dlc:{parts[i + 1].ToLowerInvariant()}";
            }
        }

        return "base";
    }

    private static string GetSourceDisplayName(string sourceKey)
    {
        return sourceKey.StartsWith("dlc:", StringComparison.OrdinalIgnoreCase)
            ? sourceKey[4..]
            : "Base game";
    }

    private static IEnumerable<RpfFileEntry> EnumerateEntries(RpfManager manager, params string[] names)
    {
        var nameSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rpf in manager.AllRpfs)
        {
            if (rpf.AllEntries is null)
            {
                continue;
            }

            foreach (var entry in rpf.AllEntries)
            {
                if (entry is RpfFileEntry fileEntry && nameSet.Contains(fileEntry.NameLower))
                {
                    yield return fileEntry;
                }
            }
        }
    }

    private static string? TryGetXmlText(RpfFileEntry entry)
    {
        try
        {
            var bytes = entry.File.ExtractFile(entry);
            return bytes is null ? null : DecodeUtf8(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? TryLoadCarVariationsXml(RpfFileEntry entry)
    {
        try
        {
            var bytes = entry.File.ExtractFile(entry);
            if (bytes is null)
            {
                return null;
            }

            var file = new CarVariationsFile();
            file.Load(bytes, entry);
            return file.Xml;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryLoadCarColsXml(RpfFileEntry entry)
    {
        try
        {
            var bytes = entry.File.ExtractFile(entry);
            if (bytes is null)
            {
                return null;
            }

            var file = new CarColsFile();
            file.Load(bytes, entry);
            return file.Xml;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> FindItemsByChildText(string xml, string childName, string expectedText)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        foreach (var item in document.Descendants("Item"))
        {
            var value = item.Element(childName)?.Value.Trim();
            if (string.Equals(value, expectedText, StringComparison.OrdinalIgnoreCase))
            {
                yield return item.ToString();
            }
        }
    }

    private static IEnumerable<uint> ExtractKitHashes(string itemXml)
    {
        XElement item;
        try
        {
            item = XElement.Parse(itemXml, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        foreach (var kit in item.Element("kits")?.Elements("Item") ?? Enumerable.Empty<XElement>())
        {
            if (TryGetHash(kit.Value, out var hash))
            {
                yield return hash;
            }
        }
    }

    private static IEnumerable<XElement> FindKits(string xml, HashSet<uint> kitHashes)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        foreach (var item in document.Descendants("Kits").Elements("Item"))
        {
            var kitName = item.Element("kitName")?.Value;
            if (kitName is not null && TryGetHash(kitName, out var hash) && kitHashes.Contains(hash))
            {
                yield return item;
            }
        }
    }

    private static bool TryGetHash(string value, out uint hash)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            hash = 0;
            return false;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out hash))
        {
            return true;
        }

        if (uint.TryParse(value, out hash))
        {
            return true;
        }

        hash = JenkHash.GenHash(value.ToLowerInvariant());
        return true;
    }

    private static bool InsertBlock(
        string targetPath,
        string sectionName,
        string blockXml,
        string? identityValue,
        string identityElement,
        bool createBackup)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("Target meta file is required.");
        }

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("Target meta file was not found.", targetPath);
        }

        if (string.IsNullOrWhiteSpace(blockXml))
        {
            throw new InvalidOperationException("No source XML block is selected.");
        }

        var text = File.ReadAllText(targetPath);
        if (!string.IsNullOrWhiteSpace(identityValue) &&
            Regex.IsMatch(
                text,
                $@"<{Regex.Escape(identityElement)}>\s*{Regex.Escape(identityValue)}\s*</{Regex.Escape(identityElement)}>",
                RegexOptions.IgnoreCase))
        {
            return false;
        }

        var closeMatches = Regex.Matches(text, $@"</{Regex.Escape(sectionName)}>", RegexOptions.IgnoreCase);
        if (closeMatches.Count == 0)
        {
            throw new InvalidOperationException($"Could not find <{sectionName}> in {Path.GetFileName(targetPath)}.");
        }

        var close = closeMatches[^1];
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var closeIndent = GetLineIndent(text, close.Index);
        var itemIndent = closeIndent + "  ";
        var insertXml = IndentBlock(blockXml, itemIndent, newline) + newline;

        if (createBackup)
        {
            File.Copy(targetPath, $"{targetPath}.{DateTime.Now:yyyyMMddHHmmss}.bak", overwrite: false);
        }

        File.WriteAllText(targetPath, text.Insert(close.Index, insertXml));
        return true;
    }

    private static string? GetChildValue(string xml, string childName)
    {
        try
        {
            return XElement.Parse(xml).Element(childName)?.Value.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string GetLineIndent(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var end = lineStart;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text[lineStart..end];
    }

    private static string IndentBlock(string xml, string indent, string newline)
    {
        var normalized = xml.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return string.Join(newline, normalized.Split('\n').Select(line => indent + line.TrimEnd()));
    }
}
