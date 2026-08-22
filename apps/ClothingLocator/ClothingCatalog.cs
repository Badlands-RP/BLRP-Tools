using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BLRP.ClothingLocator;

internal enum Gender
{
    Male,
    Female
}

internal sealed record ComponentDefinition(
    int Slot,
    bool IsProp,
    string Code,
    string Label,
    int MaleBaseOffset,
    int FemaleBaseOffset)
{
    public string Display => $"{(IsProp ? "PROP" : "COMP")} {Slot}  •  {Label}";
    public int DefaultOffset(Gender gender) => gender == Gender.Male ? MaleBaseOffset : FemaleBaseOffset;
}

internal static class ClothingComponents
{
    public static readonly IReadOnlyList<ComponentDefinition> All = new[]
    {
        new ComponentDefinition(1, false, "berd", "MASKS (BERD)", 237, 238),
        new ComponentDefinition(2, false, "hair", "HAIR", 81, 85),
        new ComponentDefinition(3, false, "uppr", "TORSO (UPPR)", 214, 248),
        new ComponentDefinition(4, false, "lowr", "LEGS (LOWR)", 193, 207),
        new ComponentDefinition(5, false, "hand", "BAGS (HAND)", 111, 111),
        new ComponentDefinition(6, false, "feet", "SHOES (FEET)", 145, 154),
        new ComponentDefinition(7, false, "teef", "ACCESSORIES (TEEF)", 178, 148),
        new ComponentDefinition(8, false, "accs", "UNDERSHIRTS (ACCS)", 207, 253),
        new ComponentDefinition(9, false, "task", "ARMOUR (TASK)", 62, 62),
        new ComponentDefinition(10, false, "decl", "DECALS (DECL)", 193, 209),
        new ComponentDefinition(11, false, "jbib", "TOPS (JBIB)", 524, 565),
        new ComponentDefinition(0, true, "p_head", "HATS", 214, 213),
        new ComponentDefinition(1, true, "p_eyes", "GLASSES", 56, 58),
        new ComponentDefinition(2, true, "p_ears", "EARS", 42, 23),
        new ComponentDefinition(6, true, "p_lwrist", "LEFT WRIST", 47, 36),
        new ComponentDefinition(7, true, "p_rwrist", "RIGHT WRIST", 14, 21)
    };

    public static readonly IReadOnlyDictionary<string, ComponentDefinition> ByCode =
        All.ToDictionary(component => component.Code, StringComparer.OrdinalIgnoreCase);
}

internal sealed record ClothingEntry(
    string FilePath,
    long Length,
    Gender Gender,
    ComponentDefinition Component,
    int Pack,
    int RelativeIndex,
    int TextureCount = 0);

internal sealed class ClothingCatalog
{
    private static readonly Regex FilePattern = new(
        @"^mp_(?<gender>[mf])_freemode_01_(?:p_)?mp_[mf]_c_addons_(?<collection>iv|iii|ii|i|v)\^(?<component>p_(?:head|eyes|ears|lwrist|rwrist)|berd|hair|uppr|lowr|hand|feet|teef|accs|task|decl|jbib)_(?<index>\d+)(?:_[ru])?\.ydd$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TextureFilePattern = new(
        @"^(?<model>.+)_diff_(?<index>\d+)_[^.]+\.ytd$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly List<ClothingEntry> _entries;
    private readonly Dictionary<(Gender Gender, string Component, int Pack), int> _packSizes;
    private readonly ConcurrentDictionary<string, string> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    private ClothingCatalog(string rootPath, List<ClothingEntry> entries)
    {
        RootPath = rootPath;
        _entries = entries;
        _packSizes = entries
            .GroupBy(entry => (entry.Gender, entry.Component.Code, entry.Pack))
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.RelativeIndex) + 1);
    }

    public string RootPath { get; }
    public int FileCount => _entries.Count;
    public IReadOnlyList<ClothingEntry> Entries => _entries;

    public static Task<ClothingCatalog> LoadAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            string fullRoot = Path.GetFullPath(rootPath);
            if (!Directory.Exists(fullRoot))
            {
                throw new DirectoryNotFoundException($"EUP directory not found: {fullRoot}");
            }

            var entries = new List<ClothingEntry>();
            foreach (string filePath in Directory.EnumerateFiles(fullRoot, "*.ydd", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ClothingEntry? entry = TryParse(filePath);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            var textureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in Directory.EnumerateFiles(fullRoot, "*.ytd", SearchOption.AllDirectories))
            {
                string? key = GetTextureKey(filePath);
                if (key != null)
                {
                    textureCounts[key] = textureCounts.GetValueOrDefault(key) + 1;
                }
            }

            for (int index = 0; index < entries.Count; index++)
            {
                ClothingEntry entry = entries[index];
                entries[index] = entry with { TextureCount = textureCounts.GetValueOrDefault(GetModelTextureKey(entry)) };
            }

            return new ClothingCatalog(fullRoot, entries);
        }, cancellationToken);
    }

    public ClothingEntry? FindByGlobalIndex(
        Gender gender,
        ComponentDefinition component,
        int globalIndex,
        int baseOffset)
    {
        int collectionIndex = globalIndex - baseOffset;
        if (collectionIndex < 0)
        {
            return null;
        }

        for (int pack = 1; pack <= 5; pack++)
        {
            int packSize = GetPackSize(gender, component, pack);
            if (collectionIndex < packSize)
            {
                return _entries.FirstOrDefault(entry =>
                    entry.Gender == gender &&
                    entry.Component.Code.Equals(component.Code, StringComparison.OrdinalIgnoreCase) &&
                    entry.Pack == pack &&
                    entry.RelativeIndex == collectionIndex);
            }

            collectionIndex -= packSize;
        }

        return null;
    }

    public int GetGlobalIndex(ClothingEntry entry, int baseOffset)
    {
        int priorPackCount = 0;
        for (int pack = 1; pack < entry.Pack; pack++)
        {
            priorPackCount += GetPackSize(entry.Gender, entry.Component, pack);
        }

        return baseOffset + priorPackCount + entry.RelativeIndex;
    }

    public Task<IReadOnlyList<(ClothingEntry Entry, ClothingModelQuality Quality)>> CreateQualityReportAsync(
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<(ClothingEntry, ClothingModelQuality)>>(() =>
    {
        var report = new List<(ClothingEntry, ClothingModelQuality)>();
        foreach (ClothingEntry entry in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClothingModelQuality quality = ClothingImporter.InspectModel(entry.FilePath, entry.TextureCount);
            if (quality.Summary != "OK") report.Add((entry, quality));
        }
        return report
            .OrderBy(item => item.Item2.Summary)
            .ThenBy(item => item.Item1.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }, cancellationToken);

    public async Task<IReadOnlyList<ClothingEntry>> FindDuplicatesAsync(
        string selectedFile,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(selectedFile);
        var selectedInfo = new FileInfo(fullPath);
        if (!selectedInfo.Exists)
        {
            throw new FileNotFoundException("Selected YDD was not found.", fullPath);
        }

        string selectedHash = await Task.Run(() => GetHash(fullPath), cancellationToken);
        var candidates = _entries.Where(entry => entry.Length == selectedInfo.Length).ToArray();
        var matches = new ConcurrentBag<ClothingEntry>();

        await Parallel.ForEachAsync(candidates, cancellationToken, (candidate, _) =>
        {
            if (GetHash(candidate.FilePath).Equals(selectedHash, StringComparison.Ordinal))
            {
                matches.Add(candidate);
            }

            return ValueTask.CompletedTask;
        });

        return matches
            .OrderBy(entry => entry.Gender)
            .ThenBy(entry => entry.Pack)
            .ThenBy(entry => entry.RelativeIndex)
            .ToArray();
    }

    private int GetPackSize(Gender gender, ComponentDefinition component, int pack)
    {
        return _packSizes.TryGetValue((gender, component.Code, pack), out int count) ? count : 0;
    }

    private string GetHash(string filePath)
    {
        return _hashCache.GetOrAdd(filePath, static path =>
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        });
    }

    private static ClothingEntry? TryParse(string filePath)
    {
        Match match = FilePattern.Match(Path.GetFileName(filePath));
        if (!match.Success || !ClothingComponents.ByCode.TryGetValue(match.Groups["component"].Value, out ComponentDefinition? component))
        {
            return null;
        }

        int pack = match.Groups["collection"].Value.ToLowerInvariant() switch
        {
            "i" => 1,
            "ii" => 2,
            "iii" => 3,
            "iv" => 4,
            "v" => 5,
            _ => 0
        };

        if (pack == 0 || !int.TryParse(match.Groups["index"].Value, out int relativeIndex))
        {
            return null;
        }

        var info = new FileInfo(filePath);
        return new ClothingEntry(
            filePath,
            info.Exists ? info.Length : 0,
            match.Groups["gender"].Value.Equals("m", StringComparison.OrdinalIgnoreCase) ? Gender.Male : Gender.Female,
            component,
            pack,
            relativeIndex);
    }

    private static string GetModelTextureKey(ClothingEntry entry)
    {
        string modelName = Path.GetFileNameWithoutExtension(entry.FilePath);
        if (modelName.EndsWith("_r", StringComparison.OrdinalIgnoreCase) ||
            modelName.EndsWith("_u", StringComparison.OrdinalIgnoreCase))
        {
            modelName = modelName[..^2];
        }

        return Path.Combine(Path.GetDirectoryName(entry.FilePath) ?? string.Empty, modelName);
    }

    private static string? GetTextureKey(string filePath)
    {
        Match match = TextureFilePattern.Match(Path.GetFileName(filePath));
        return match.Success
            ? Path.Combine(
                Path.GetDirectoryName(filePath) ?? string.Empty,
                $"{match.Groups["model"].Value}_{match.Groups["index"].Value}")
            : null;
    }

    internal static bool SelfTest(string? rootPath = null)
    {
        const string maleFile = @"C:\pack\mp_m_freemode_01_mp_m_c_addons_i^teef_113_u.ydd";
        const string femaleProp = @"C:\pack\mp_f_freemode_01_p_mp_f_c_addons_iv^p_head_004.ydd";
        ClothingEntry? male = TryParse(maleFile);
        ClothingEntry? prop = TryParse(femaleProp);

        if (male is null || male.Gender != Gender.Male || male.Pack != 1 || male.RelativeIndex != 113 || male.Component.Code != "teef")
        {
            return false;
        }

        if (prop is null || prop.Gender != Gender.Female || prop.Pack != 4 || prop.RelativeIndex != 4 || prop.Component.Code != "p_head")
        {
            return false;
        }

        const string maleTexture = @"C:\pack\mp_m_freemode_01_mp_m_c_addons_i^teef_diff_113_a_uni.ytd";
        if (!string.Equals(GetModelTextureKey(male), GetTextureKey(maleTexture), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var catalog = new ClothingCatalog("test", new List<ClothingEntry> { male });
        bool basicResult = catalog.GetGlobalIndex(male, 178) == 291 &&
                           ReferenceEquals(catalog.FindByGlobalIndex(Gender.Male, male.Component, 291, 178), male);
        if (!basicResult || string.IsNullOrWhiteSpace(rootPath))
        {
            return basicResult;
        }

        ClothingCatalog realCatalog = LoadAsync(rootPath).GetAwaiter().GetResult();
        if (!realCatalog._entries.Any(entry => entry.TextureCount > 0))
        {
            return false;
        }

        ComponentDefinition teef = ClothingComponents.ByCode["teef"];
        ClothingEntry? item113 = realCatalog.FindByGlobalIndex(Gender.Male, teef, 291, 178);
        ClothingEntry? item114 = realCatalog.FindByGlobalIndex(Gender.Male, teef, 292, 178);
        ClothingEntry? item115 = realCatalog.FindByGlobalIndex(Gender.Male, teef, 293, 178);
        if (item113?.RelativeIndex != 113 || item114?.RelativeIndex != 114 || item115?.RelativeIndex != 115)
        {
            return false;
        }

        IReadOnlyList<ClothingEntry> duplicates = realCatalog.FindDuplicatesAsync(item115.FilePath).GetAwaiter().GetResult();
        IReadOnlyList<(ClothingEntry Entry, ClothingModelQuality Quality)> qualityReport =
            realCatalog.CreateQualityReportAsync().GetAwaiter().GetResult();
        return duplicates.Count == 1 &&
            duplicates[0].RelativeIndex == 115 &&
            qualityReport.All(item => item.Quality.Summary != "OK");
    }
}
