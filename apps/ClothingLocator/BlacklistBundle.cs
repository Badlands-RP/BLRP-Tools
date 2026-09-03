using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BLRP.ClothingLocator;

internal sealed record BlacklistAssetItem(
    ClothingEntry Entry,
    int GlobalIndex,
    string Scope,
    IReadOnlyList<string> Files,
    IReadOnlyList<int> MissingTextureIndexes)
{
    public int TextureCount => Files.Count - 1;
}

internal sealed record BlacklistAssetSearchResult(
    IReadOnlyList<BlacklistAssetItem> Items,
    int UnresolvedDrawables,
    int MissingTextures);

internal sealed record BlacklistImportResult(int FileCount, string BackupDirectory);

internal sealed record BlacklistBundleManifest(int Version, string Group, DateTime CreatedUtc, string[] Files);

internal static class BlacklistBundle
{
    public const string ManifestFileName = "blrp-clothing-export.json";

    private static readonly Regex CategoryPattern = new(
        @"^\s{2}\['(?<key>\d+|p\d+)'\]\s*=\s*\{\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DrawablePattern = new(
        @"^\s{4}\[(?<index>\d+)\]\s*=\s*'(?<value>(?:\\.|[^'])*)'\s*,?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DrawableTablePattern = new(
        @"^\s{4}\[(?<index>\d+)\]\s*=\s*\{\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TexturePattern = new(
        @"^\s{6}\[(?<index>\d+)\]\s*=\s*'(?<value>(?:\\.|[^'])*)'\s*,?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static BlacklistAssetSearchResult Find(
        string rootPath,
        ClothingCatalog catalog,
        string group)
    {
        group = group.Trim();
        if (group.Length == 0) throw new ArgumentException("Choose a blacklist group.", nameof(group));

        var items = new List<BlacklistAssetItem>();
        int unresolved = 0;
        int missingTextures = 0;

        foreach (Gender gender in Enum.GetValues<Gender>())
        {
            string path = ClothingBlacklist.GetPath(rootPath, gender);
            string[] lines = File.ReadAllText(path)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');
            int tableStart = ClothingBlacklist.FindTableStart(lines);
            int tableEnd = ClothingBlacklist.FindBlockEnd(lines, tableStart);

            for (int lineIndex = tableStart + 1; lineIndex < tableEnd; lineIndex++)
            {
                Match categoryMatch = CategoryPattern.Match(lines[lineIndex]);
                if (!categoryMatch.Success || !TryGetComponent(categoryMatch.Groups["key"].Value, out ComponentDefinition? component))
                {
                    continue;
                }

                int categoryEnd = ClothingBlacklist.FindBlockEnd(lines, lineIndex);
                for (int entryIndex = lineIndex + 1; entryIndex < categoryEnd; entryIndex++)
                {
                    Match drawableMatch = DrawablePattern.Match(lines[entryIndex]);
                    if (drawableMatch.Success)
                    {
                        string restriction = ClothingBlacklist.UnescapeLua(drawableMatch.Groups["value"].Value);
                        if (Matches(group, restriction))
                        {
                            AddItem(
                                catalog,
                                gender,
                                component!,
                                int.Parse(drawableMatch.Groups["index"].Value),
                                restriction,
                                null,
                                items,
                                ref unresolved,
                                ref missingTextures);
                        }
                        continue;
                    }

                    Match tableMatch = DrawableTablePattern.Match(lines[entryIndex]);
                    if (!tableMatch.Success) continue;

                    int entryEnd = ClothingBlacklist.FindBlockEnd(lines, entryIndex);
                    var textureRestrictions = new Dictionary<int, string>();
                    for (int textureLine = entryIndex + 1; textureLine < entryEnd; textureLine++)
                    {
                        Match textureMatch = TexturePattern.Match(lines[textureLine]);
                        if (!textureMatch.Success) continue;
                        string restriction = ClothingBlacklist.UnescapeLua(textureMatch.Groups["value"].Value);
                        if (Matches(group, restriction))
                        {
                            textureRestrictions[int.Parse(textureMatch.Groups["index"].Value)] = restriction;
                        }
                    }

                    if (textureRestrictions.Count > 0)
                    {
                        AddItem(
                            catalog,
                            gender,
                            component!,
                            int.Parse(tableMatch.Groups["index"].Value),
                            string.Empty,
                            textureRestrictions,
                            items,
                            ref unresolved,
                            ref missingTextures);
                    }
                    entryIndex = entryEnd;
                }
                lineIndex = categoryEnd;
            }
        }

        return new BlacklistAssetSearchResult(
            items.OrderBy(item => item.Entry.Gender)
                .ThenBy(item => item.Entry.Component.IsProp)
                .ThenBy(item => item.Entry.Component.Slot)
                .ThenBy(item => item.GlobalIndex)
                .ToArray(),
            unresolved,
            missingTextures);
    }

    public static string ExportDirectory(
        string rootPath,
        string group,
        IEnumerable<BlacklistAssetItem> items,
        string outputDirectory)
    {
        string fullOutput = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(fullOutput) && Directory.EnumerateFileSystemEntries(fullOutput).Any())
        {
            throw new IOException("Choose an empty export directory.");
        }
        Directory.CreateDirectory(fullOutput);
        WriteBundle(rootPath, group, items, fullOutput);
        return fullOutput;
    }

    public static void ExportZip(
        string rootPath,
        string group,
        IEnumerable<BlacklistAssetItem> items,
        string zipPath)
    {
        string staging = Path.Combine(Path.GetTempPath(), "BLRP-Clothing-Export-" + Guid.NewGuid().ToString("N"));
        string fullZip = Path.GetFullPath(zipPath);
        string temporaryZip = fullZip + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(staging);
            WriteBundle(rootPath, group, items, staging);
            Directory.CreateDirectory(Path.GetDirectoryName(fullZip)!);
            ZipFile.CreateFromDirectory(staging, temporaryZip, CompressionLevel.Optimal, false);
            File.Move(temporaryZip, fullZip, true);
        }
        finally
        {
            if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public static BlacklistImportResult Reimport(string rootPath, string bundlePath)
    {
        string fullBundle = Path.GetFullPath(bundlePath);
        string? temporaryDirectory = null;
        string sourceRoot;
        string manifestPath;

        if (Path.GetExtension(fullBundle).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "BLRP-Clothing-Reimport-" + Guid.NewGuid().ToString("N"));
            sourceRoot = temporaryDirectory;
            manifestPath = Path.Combine(sourceRoot, ManifestFileName);
        }
        else
        {
            manifestPath = fullBundle;
            sourceRoot = Path.GetDirectoryName(manifestPath)!;
        }

        try
        {
            if (temporaryDirectory != null)
            {
                Directory.CreateDirectory(temporaryDirectory);
                ZipFile.ExtractToDirectory(fullBundle, temporaryDirectory);
            }
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException($"{ManifestFileName} was not found in the export.");
            }

            BlacklistBundleManifest manifest = JsonSerializer.Deserialize<BlacklistBundleManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("The clothing export manifest is invalid.");
            if (manifest.Version != 1 || manifest.Files.Length == 0)
            {
                throw new InvalidDataException("The clothing export manifest is empty or unsupported.");
            }

            string fullRoot = Path.GetFullPath(rootPath);
            var replacements = manifest.Files
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(relative =>
                {
                    string normalized = ValidateRelativeAssetPath(relative);
                    string source = GetContainedPath(sourceRoot, normalized);
                    string target = GetContainedPath(fullRoot, normalized);
                    if (!File.Exists(source)) throw new FileNotFoundException("An exported asset is missing.", source);
                    if (!File.Exists(target)) throw new FileNotFoundException("The matching repository asset was not found.", target);
                    return (Source: source, Target: target, Relative: normalized);
                })
                .ToArray();

            string backupRoot = Path.Combine(
                fullRoot,
                ".clothing-locator-backups",
                DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-blacklist-reimport-" + Guid.NewGuid().ToString("N")[..6]);
            string[] temporaryFiles = replacements.Select(item => item.Target + "." + Guid.NewGuid().ToString("N") + ".blrp-importing").ToArray();
            try
            {
                for (int index = 0; index < replacements.Length; index++)
                {
                    File.Copy(replacements[index].Source, temporaryFiles[index], false);
                }

                foreach (var replacement in replacements)
                {
                    string backup = Path.Combine(backupRoot, replacement.Relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(replacement.Target, backup, false);
                }

                for (int index = 0; index < replacements.Length; index++)
                {
                    File.Move(temporaryFiles[index], replacements[index].Target, true);
                }
            }
            finally
            {
                foreach (string temporaryFile in temporaryFiles)
                {
                    if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
                }
            }

            return new BlacklistImportResult(replacements.Length, backupRoot);
        }
        finally
        {
            if (temporaryDirectory != null && Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }
    }

    private static void AddItem(
        ClothingCatalog catalog,
        Gender gender,
        ComponentDefinition component,
        int globalIndex,
        string drawableRestriction,
        IReadOnlyDictionary<int, string>? textureRestrictions,
        List<BlacklistAssetItem> items,
        ref int unresolved,
        ref int missingTextures)
    {
        ClothingEntry? entry = catalog.FindByGlobalIndex(
            gender,
            component,
            globalIndex,
            component.DefaultOffset(gender));
        if (entry == null)
        {
            unresolved++;
            return;
        }

        IReadOnlyList<string> allTextures = catalog.FindTextures(entry);
        var files = new List<string> { entry.FilePath };
        var missing = new List<int>();
        string scope;
        if (textureRestrictions == null)
        {
            files.AddRange(allTextures);
            scope = $"DRAWABLE / {drawableRestriction}";
        }
        else
        {
            foreach (int textureIndex in textureRestrictions.Keys.Order())
            {
                if (textureIndex < allTextures.Count) files.Add(allTextures[textureIndex]);
                else missing.Add(textureIndex);
            }
            scope = "TEXTURE " + string.Join(", ", textureRestrictions.OrderBy(pair => pair.Key)
                .Select(pair => $"#{pair.Key} / {pair.Value}"));
        }

        missingTextures += missing.Count;
        items.Add(new BlacklistAssetItem(entry, globalIndex, scope, files, missing));
    }

    private static void WriteBundle(
        string rootPath,
        string group,
        IEnumerable<BlacklistAssetItem> items,
        string outputRoot)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        string[] files = items.SelectMany(item => item.Files)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(fullRoot, Path.GetFullPath(path)))
            .Select(ValidateRelativeAssetPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) throw new InvalidOperationException("No clothing assets were selected for export.");

        foreach (string relative in files)
        {
            string source = GetContainedPath(fullRoot, relative);
            string target = GetContainedPath(outputRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }

        var manifest = new BlacklistBundleManifest(1, group, DateTime.UtcNow, files.Select(path => path.Replace('\\', '/')).ToArray());
        File.WriteAllText(
            Path.Combine(outputRoot, ManifestFileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool Matches(string requested, string restriction)
    {
        string[] requestedGroups = requested.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] allowedGroups = restriction.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return requestedGroups.Any(group => allowedGroups.Contains(group, StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryGetComponent(string categoryKey, out ComponentDefinition? component)
    {
        component = ClothingComponents.All.FirstOrDefault(item =>
            (item.IsProp ? $"p{item.Slot}" : item.Slot.ToString()) == categoryKey);
        return component != null;
    }

    private static string ValidateRelativeAssetPath(string relative)
    {
        string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        string extension = Path.GetExtension(normalized);
        if (Path.IsPathRooted(normalized) ||
            normalized == ".." ||
            normalized.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !new[] { ".ydd", ".ytd" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid clothing asset path in export: {relative}");
        }
        return normalized;
    }

    private static string GetContainedPath(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        string check = Path.GetRelativePath(fullRoot, path);
        if (Path.IsPathRooted(check) || check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Asset path escapes its clothing export root: {relative}");
        }
        return path;
    }

    internal static bool SelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "BLRP-Blacklist-Bundle-Test-" + Guid.NewGuid().ToString("N"));
        string blacklistDirectory = Path.Combine(root, "blrp_clothingstore", "blacklists");
        string assetDirectory = Path.Combine(root, "clothing_addon_1", "stream", "mp_m_freemode_01_mp_m_c_addons_i", "berd");
        Directory.CreateDirectory(blacklistDirectory);
        Directory.CreateDirectory(assetDirectory);
        string firstModel = Path.Combine(assetDirectory, "mp_m_freemode_01_mp_m_c_addons_i^berd_000_u.ydd");
        string secondModel = Path.Combine(assetDirectory, "mp_m_freemode_01_mp_m_c_addons_i^berd_001_u.ydd");
        string firstTextureA = Path.Combine(assetDirectory, "mp_m_freemode_01_mp_m_c_addons_i^berd_diff_000_a_uni.ytd");
        string firstTextureB = Path.Combine(assetDirectory, "mp_m_freemode_01_mp_m_c_addons_i^berd_diff_000_b_uni.ytd");
        string secondTexture = Path.Combine(assetDirectory, "mp_m_freemode_01_mp_m_c_addons_i^berd_diff_001_a_uni.ytd");
        File.WriteAllText(firstModel, "first-original");
        File.WriteAllText(secondModel, "second-original");
        File.WriteAllText(firstTextureA, "a-original");
        File.WriteAllText(firstTextureB, "b-original");
        File.WriteAllText(secondTexture, "second-texture");
        const string fixture = "--[[\nblacklists[`mp_m_freemode_01`] = {\n  ['1'] = {\n    [237] = 'WRONG EXAMPLE',\n  },\n}\n]]\nblacklists[`mp_m_freemode_01`] = {\n  sex = 'male',\n  ['1'] = {\n    [237] = {\n      [0] = 'Angels of Death',\n      [1] = 'LEO|LSFD',\n    },\n    [238] = 'Angels of Death',\n  },\n}\n";
        File.WriteAllText(Path.Combine(blacklistDirectory, "mp_m_freemode_01.lua"), fixture);
        File.WriteAllText(Path.Combine(blacklistDirectory, "mp_f_freemode_01.lua"), "blacklists[`mp_f_freemode_01`] = {\n  sex = 'female',\n}\n");

        try
        {
            ClothingCatalog catalog = ClothingCatalog.LoadAsync(root).GetAwaiter().GetResult();
            BlacklistAssetSearchResult angels = Find(root, catalog, "Angels of Death");
            BlacklistAssetSearchResult leo = Find(root, catalog, "LEO");
            if (angels.Items.Count != 2 || angels.Items[0].Files.Count != 2 || angels.Items[1].Files.Count != 2 ||
                leo.Items.Count != 1 || leo.Items[0].Files.Count != 2 || leo.Items[0].Files[1] != firstTextureB)
            {
                return false;
            }

            string exported = Path.Combine(root, "exported");
            ExportDirectory(root, "Angels of Death", angels.Items, exported);
            string zipPath = Path.Combine(root, "angels.zip");
            ExportZip(root, "Angels of Death", angels.Items, zipPath);
            BlacklistImportResult zipImport = Reimport(root, zipPath);
            string exportedModel = Path.Combine(exported, Path.GetRelativePath(root, firstModel));
            File.WriteAllText(exportedModel, "first-edited");
            BlacklistImportResult imported = Reimport(root, Path.Combine(exported, ManifestFileName));
            return zipImport.FileCount == 4 &&
                imported.FileCount == 4 &&
                File.ReadAllText(firstModel) == "first-edited" &&
                File.ReadAllText(Path.Combine(imported.BackupDirectory, Path.GetRelativePath(root, firstModel))) == "first-original";
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
