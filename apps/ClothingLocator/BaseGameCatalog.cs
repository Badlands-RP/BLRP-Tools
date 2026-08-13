using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace BLRP.ClothingLocator;

internal sealed record GtaInstallation(string RootPath, int GameBuild, string DlcName);

internal sealed record BaseGameClothingEntry(
    Gender Gender,
    ComponentDefinition Component,
    int GlobalIndex,
    int RelativeIndex,
    string Collection,
    string ModelName,
    string ModelArchivePath,
    IReadOnlyList<string> TextureArchivePaths,
    RpfFileEntry ModelEntry,
    IReadOnlyList<RpfFileEntry> TextureEntries);

internal sealed class BaseGameCatalog
{
    private static readonly Regex DlcPackPattern = new(
        @"dlcpacks\\(?<pack>[^\\]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly GameFileCache _cache;
    private readonly Dictionary<(Gender Gender, int Component), List<CollectionData>> _collections;
    private readonly Dictionary<string, RpfFileEntry> _assets;

    private BaseGameCatalog(
        GtaInstallation installation,
        GameFileCache cache,
        Dictionary<(Gender Gender, int Component), List<CollectionData>> collections,
        Dictionary<string, RpfFileEntry> assets)
    {
        Installation = installation;
        _cache = cache;
        _collections = collections;
        _assets = assets;
    }

    public GtaInstallation Installation { get; }

    public static GtaInstallation DetectInstallation()
    {
        string iniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FiveM", "FiveM.app", "CitizenFX.ini");
        if (!File.Exists(iniPath))
        {
            throw new FileNotFoundException("FiveM CitizenFX.ini was not found. Start FiveM once, then retry.", iniPath);
        }

        var values = File.ReadLines(iniPath)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        if (!values.TryGetValue("IVPath", out string? gtaRoot) || !Directory.Exists(gtaRoot))
        {
            throw new DirectoryNotFoundException("The GTA V path in FiveM CitizenFX.ini is missing or invalid.");
        }

        int build = 0;
        if (values.TryGetValue("SavedBuildNumber", out string? savedBuild))
        {
            int.TryParse(savedBuild, out build);
        }
        if (build == 0 && values.TryGetValue("DefaultBuild", out string? defaultBuild))
        {
            int.TryParse(defaultBuild, out build);
        }

        return new GtaInstallation(Path.GetFullPath(gtaRoot), build, GetDlcForBuild(build));
    }

    public static Task<BaseGameCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            GtaInstallation installation = DetectInstallation();
            GTA5Keys.LoadFromPath(installation.RootPath, false, null);

            var errors = new List<string>();
            var cache = new GameFileCache(
                512L * 1024L * 1024L,
                60.0,
                installation.RootPath,
                false,
                installation.DlcName,
                false,
                string.Empty)
            {
                LoadArchetypes = false,
                LoadVehicles = false,
                LoadAudio = false,
                LoadPeds = true,
                DoFullStringIndex = false,
                BuildExtendedJenkIndex = false
            };
            cache.Init(_ => { }, errors.Add);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("CodeWalker could not read the GTA archives: " + errors[0]);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var assets = new Dictionary<string, RpfFileEntry>(StringComparer.OrdinalIgnoreCase);
            var orderedCollections = new Dictionary<Gender, OrderedCollections>
            {
                [Gender.Male] = new OrderedCollections(),
                [Gender.Female] = new OrderedCollections()
            };

            foreach (RpfFile rpf in cache.BaseRpfs)
            {
                IndexRpf(cache, rpf, false, orderedCollections, assets);
            }

            foreach (RpfFile topLevelRpf in cache.DlcActiveRpfs)
            {
                string pack = DlcPackPattern.Match(topLevelRpf.Path).Groups["pack"].Value;
                bool patchOnly = pack.StartsWith("patch", StringComparison.OrdinalIgnoreCase);
                foreach (RpfFile rpf in EnumerateRpfTree(topLevelRpf))
                {
                    IndexRpf(cache, rpf, patchOnly, orderedCollections, assets);
                }
            }

            var collections = new Dictionary<(Gender Gender, int Component), List<CollectionData>>();
            foreach ((Gender gender, OrderedCollections source) in orderedCollections)
            {
                for (int componentId = 0; componentId <= 11; componentId++)
                {
                    var list = new List<CollectionData>();
                    foreach (PedCollection pedCollection in source.Values)
                    {
                        MCPVComponentData? component = pedCollection.PedFile.VariationInfo?.GetComponentData(componentId);
                        if (component?.DrawblData3 is { Length: > 0 } drawables)
                        {
                            list.Add(new CollectionData(pedCollection.Name, drawables));
                        }
                    }
                    collections[(gender, componentId)] = list;
                }
            }

            return new BaseGameCatalog(installation, cache, collections, assets);
        }, cancellationToken);
    }

    public BaseGameClothingEntry? Find(Gender gender, ComponentDefinition component, int globalIndex)
    {
        if (component.IsProp || globalIndex < 0 ||
            !_collections.TryGetValue((gender, component.Slot), out List<CollectionData>? collections))
        {
            return null;
        }

        int start = 0;
        foreach (CollectionData collection in collections)
        {
            int end = start + collection.Drawables.Length;
            if (globalIndex < end)
            {
                int relative = globalIndex - start;
                MCPVDrawblData drawable = collection.Drawables[relative];
                string modelName = drawable.GetDrawableName();
                string assetPrefix = AssetKey(collection.Name, string.Empty);
                string modelKey = assetPrefix + modelName + ".ydd";
                if (!_assets.TryGetValue(modelKey, out RpfFileEntry? modelEntry))
                {
                    return null;
                }

                var textureEntries = new List<RpfFileEntry>();
                if (drawable.TexData != null)
                {
                    for (int texture = 0; texture < drawable.TexData.Length; texture++)
                    {
                        string textureKey = assetPrefix + drawable.GetTextureName(texture) + ".ytd";
                        if (_assets.TryGetValue(textureKey, out RpfFileEntry? textureEntry))
                        {
                            textureEntries.Add(textureEntry);
                        }
                    }
                }

                return new BaseGameClothingEntry(
                    gender,
                    component,
                    globalIndex,
                    relative,
                    collection.Name,
                    modelName,
                    modelEntry.Path,
                    textureEntries.Select(entry => entry.Path).ToArray(),
                    modelEntry,
                    textureEntries);
            }
            start = end;
        }

        return null;
    }

    public int GetDrawableCount(Gender gender, ComponentDefinition component) =>
        component.IsProp || !_collections.TryGetValue((gender, component.Slot), out List<CollectionData>? collections)
            ? 0
            : collections.Sum(collection => collection.Drawables.Length);

    public IReadOnlyList<string> Extract(BaseGameClothingEntry entry, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();
        Extract(entry.ModelEntry, outputDirectory, written);
        foreach (RpfFileEntry texture in entry.TextureEntries)
        {
            Extract(texture, outputDirectory, written);
        }
        return written;
    }

    private static void Extract(RpfFileEntry entry, string outputDirectory, List<string> written)
    {
        byte[] bytes = entry.File.ExtractFile(entry);
        if (entry is RpfResourceFileEntry resourceEntry)
        {
            bytes = ResourceBuilder.Compress(bytes);
            bytes = ResourceBuilder.AddResourceHeader(resourceEntry, bytes);
        }
        string outputPath = Path.Combine(outputDirectory, entry.Name);
        File.WriteAllBytes(outputPath, bytes);
        written.Add(outputPath);
    }

    private static void IndexRpf(
        GameFileCache cache,
        RpfFile rpf,
        bool patchOnly,
        Dictionary<Gender, OrderedCollections> collections,
        Dictionary<string, RpfFileEntry> assets)
    {
        if (rpf.AllEntries == null)
        {
            return;
        }

        foreach (RpfEntry rawEntry in rpf.AllEntries)
        {
            if (rawEntry is not RpfFileEntry entry)
            {
                continue;
            }

            Gender? gender = GetGender(entry.Path);
            if (gender == null)
            {
                continue;
            }

            string pedName = gender == Gender.Male ? "mp_m_freemode_01" : "mp_f_freemode_01";
            if (entry.NameLower.EndsWith(".ymt", StringComparison.Ordinal) &&
                entry.GetShortNameLower().StartsWith(pedName, StringComparison.Ordinal))
            {
                string collectionName = entry.GetShortNameLower();
                bool isBase = collectionName.Equals(pedName, StringComparison.OrdinalIgnoreCase);
                OrderedCollections target = collections[gender.Value];
                if (patchOnly && !isBase && !target.Contains(collectionName))
                {
                    continue;
                }

                PedFile? pedFile = cache.RpfMan.GetFile<PedFile>(entry);
                if (pedFile?.VariationInfo != null)
                {
                    target.Set(collectionName, pedFile, isBase);
                }
                continue;
            }

            if (!entry.NameLower.EndsWith(".ydd", StringComparison.Ordinal) &&
                !entry.NameLower.EndsWith(".ytd", StringComparison.Ordinal))
            {
                continue;
            }

            string? collection = GetAssetCollection(entry.Path, pedName);
            if (collection != null)
            {
                assets[AssetKey(collection, entry.NameLower)] = entry;
            }
        }
    }

    private static IEnumerable<RpfFile> EnumerateRpfTree(RpfFile root)
    {
        yield return root;
        if (root.Children == null)
        {
            yield break;
        }
        foreach (RpfFile child in root.Children)
        {
            foreach (RpfFile descendant in EnumerateRpfTree(child))
            {
                yield return descendant;
            }
        }
    }

    private static Gender? GetGender(string path)
    {
        if (path.Contains("mp_m_freemode_01", StringComparison.OrdinalIgnoreCase)) return Gender.Male;
        if (path.Contains("mp_f_freemode_01", StringComparison.OrdinalIgnoreCase)) return Gender.Female;
        return null;
    }

    private static string? GetAssetCollection(string path, string pedName)
    {
        string[] segments = path.Split('\\');
        for (int index = segments.Length - 2; index >= 0; index--)
        {
            string segment = segments[index];
            if (!segment.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) &&
                segment.StartsWith(pedName, StringComparison.OrdinalIgnoreCase))
            {
                return segment.ToLowerInvariant();
            }
        }
        return null;
    }

    private static string AssetKey(string collection, string fileName) =>
        collection.ToLowerInvariant() + "|" + fileName.ToLowerInvariant();

    private static string GetDlcForBuild(int build) => build switch
    {
        >= 3717 => "mp2025_02",
        >= 3570 => "mp2025_01",
        >= 3407 => "mp2024_02",
        >= 3258 => "mp2024_01",
        >= 3095 => "mp2023_02",
        >= 2944 => "mp2023_01",
        >= 2802 => "mpchristmas3",
        >= 2699 => "mpsum2",
        >= 2612 => "mpsecurity",
        >= 2545 => "mptuner",
        >= 2372 => "mpheist4",
        _ => "mpchristmas2018"
    };

    internal static bool SelfTest(string outputDirectory)
    {
        BaseGameCatalog catalog = LoadAsync().GetAwaiter().GetResult();
        ComponentDefinition teef = ClothingComponents.ByCode["teef"];
        int count = catalog.GetDrawableCount(Gender.Male, teef);
        BaseGameClothingEntry? first = catalog.Find(Gender.Male, teef, 0);
        BaseGameClothingEntry? last = catalog.Find(Gender.Male, teef, count - 1);
        CollectionData lastCollection = catalog._collections[(Gender.Male, teef.Slot)][^1];
        MCPVDrawblData lastDrawable = lastCollection.Drawables[^1];
        string expectedModel = lastDrawable.GetDrawableName() + ".ydd";
        string expectedKey = AssetKey(lastCollection.Name, expectedModel);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "self-test.txt"),
            $"Build={catalog.Installation.GameBuild}{Environment.NewLine}" +
            $"DLC={catalog.Installation.DlcName}{Environment.NewLine}" +
            $"Count={count}{Environment.NewLine}" +
            $"First={first?.Collection} / {first?.ModelName}{Environment.NewLine}" +
            $"Last={last?.Collection} / {last?.ModelName}{Environment.NewLine}" +
            $"ExpectedLast={lastCollection.Name} / {lastDrawable.Name} / {expectedModel}{Environment.NewLine}" +
            $"AssetFound={catalog._assets.ContainsKey(expectedKey)}{Environment.NewLine}" +
            $"AssetMatches={string.Join(",", catalog._assets.Keys.Where(key => key.StartsWith(lastCollection.Name + "|", StringComparison.OrdinalIgnoreCase)).Take(10))}{Environment.NewLine}");
        if (count <= 0 || first == null || last == null || catalog.Find(Gender.Male, teef, count) != null)
        {
            return false;
        }

        IReadOnlyList<string> files = catalog.Extract(last, outputDirectory);
        return files.Count > 0 && files.All(path => new FileInfo(path).Length > 0);
    }

    private sealed record CollectionData(string Name, MCPVDrawblData[] Drawables);
    private sealed record PedCollection(string Name, PedFile PedFile);

    private sealed class OrderedCollections
    {
        private readonly List<string> _order = new();
        private readonly Dictionary<string, PedCollection> _values = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<PedCollection> Values => _order.Select(name => _values[name]);
        public bool Contains(string name) => _values.ContainsKey(name);

        public void Set(string name, PedFile file, bool baseCollection)
        {
            if (!_values.ContainsKey(name))
            {
                if (baseCollection) _order.Insert(0, name); else _order.Add(name);
            }
            _values[name] = new PedCollection(name, file);
        }
    }
}
