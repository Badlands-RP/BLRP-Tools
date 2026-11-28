using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace Badlands.LiveryTool;

internal sealed class VehiclePreviewAssets : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "BLRP-Livery-Preview-" + Guid.NewGuid().ToString("N"));
    public string ModelPath => Path.Combine(DirectoryPath, "vehicle.yft");
    public string Description { get; private set; } = "";
    private static readonly SemaphoreSlim ArchiveLock = new(1);
    private static GameFileCache? cache;
    private static string cacheKey = "";

    public VehiclePreviewAssets() => Directory.CreateDirectory(DirectoryPath);

    internal static async Task<T> ReadGame<T>(string gtaFolder, string dlc, CancellationToken cancellation, IProgress<string> progress, Func<GameFileCache, T> read)
    {
        await ArchiveLock.WaitAsync(cancellation);
        try { return await Task.Run(() => read(GetGame(gtaFolder, dlc, cancellation, progress)), cancellation); }
        finally { ArchiveLock.Release(); }
    }

    private static GameFileCache GetGame(string gtaFolder, string dlc, CancellationToken cancellation, IProgress<string> progress)
    {
        if (!Directory.Exists(gtaFolder)) throw new InvalidDataException("Choose your GTA V Legacy folder to use stock metadata and livery templates.");
        if (string.IsNullOrWhiteSpace(dlc)) throw new InvalidDataException("Choose the GTA DLC level matching your server build. The latest installed DLC is not selected automatically.");
        string key = Path.GetFullPath(gtaFolder) + "|" + dlc;
        if (cache == null || cacheKey != key)
        {
            progress.Report("Indexing GTA archives (first load can take a while)…");
            GTA5Keys.LoadFromPath(gtaFolder, false, null);
            var next = new GameFileCache(512 * 1024 * 1024, 10, gtaFolder, false, dlc, false, "")
            { LoadArchetypes = false, LoadVehicles = false, LoadPeds = false, LoadAudio = false };
            var errors = new List<string>();
            next.Init(message => { cancellation.ThrowIfCancellationRequested(); progress.Report(message); }, errors.Add);
            cancellation.ThrowIfCancellationRequested();
            if (!next.IsInited || !next.DlcNameList.Contains(dlc, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("GTA archive indexing failed or the selected DLC level is unavailable. " + errors.FirstOrDefault());
            cache?.Clear(); cache = next; cacheKey = key;
        }
        return cache;
    }

    public static async Task<VehiclePreviewAssets> Prepare(VehicleCatalog catalog, CatalogVehicle vehicle, LiveryImport? import, string gtaFolder, string dlc,
        CancellationToken cancellation, IProgress<string> progress, ExistingLivery? existing = null)
    {
        await ArchiveLock.WaitAsync(cancellation);
        try { return await Task.Run(() => Build(catalog, vehicle, import, gtaFolder, dlc, cancellation, progress, existing), cancellation); }
        finally { ArchiveLock.Release(); }
    }

    private static VehiclePreviewAssets Build(VehicleCatalog catalog, CatalogVehicle vehicle, LiveryImport? import, string gtaFolder, string dlc,
        CancellationToken cancellation, IProgress<string> progress, ExistingLivery? existing)
    {
        var result = new VehiclePreviewAssets();
        try
        {
            if (existing != null) ExistingLiveries.Validate(catalog, vehicle, existing);
            GameFileCache? game = null;
            GameFileCache Game()
            {
                if (game != null) return game;
                game = GetGame(gtaFolder, dlc, cancellation, progress);
                return game;
            }
            YtdFile? ArchiveTextures(string name)
            {
                RpfFileEntry? entry = Game().GetYtdEntry(JenkHash.GenHash(name.ToLowerInvariant()));
                if (entry == null) return null;
                var ytd = new YtdFile(); ytd.Load(entry.File.ExtractFile(entry), entry);
                progress.Report($"Resolved {name}: {ytd.TextureDict?.Textures?.data_items?.Length ?? 0} textures from {entry.Path}");
                return ytd;
            }
            if (vehicle.ModelPaths.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                throw new InvalidDataException("Duplicate streamed vehicle model names. Resolve resource load order before previewing.");
            string[] high = vehicle.ModelPaths.Where(path => path.EndsWith("_hi.yft", StringComparison.OrdinalIgnoreCase)).ToArray();
            string[] candidates = high.Length > 0 ? high : vehicle.ModelPaths;
            if (candidates.Length > 1) throw new InvalidDataException("Multiple streamed vehicle models have the same name. Resolve duplicate assets before previewing.");
            var model = new YftFile();
            string modelSource;
            if (candidates.Length == 1) { modelSource = candidates[0]; model.Load(NativeAssetReader.Read(modelSource)); }
            else
            {
                RpfFileEntry? entry = GameLiveryTemplates.FindActiveYft(Game(), vehicle.Model + "_hi") ??
                    GameLiveryTemplates.FindActiveYft(Game(), vehicle.Model);
                if (entry == null) throw new InvalidDataException("Actual vehicle model is absent from the selected GTA DLC level.");
                modelSource = entry.Path;
                model.Load(entry.File.ExtractFile(entry), entry);
            }
            if (model.Fragment?.Drawable == null) throw new InvalidDataException("The selected asset has no drawable vehicle fragment.");
            cancellation.ThrowIfCancellationRequested();
            progress.Report(import == null ? "Resolving vehicle materials…" : "Preparing the pending livery and resolving vehicle materials…");
            string pending = Path.Combine(result.DirectoryPath, "pending.yft");
            string unique = "blrp_preview_" + Guid.NewGuid().ToString("N");
            YftFile? mod = null;
            if (import != null)
            {
                LiveryAssetBuilder.Build(import.Template.Path, import.Texture, import.Image, unique, pending, import.FitImage, cancellation);
                mod = new YftFile(); mod.Load(File.ReadAllBytes(pending));
            }
            else if (existing != null)
            {
                if (existing.Paths.Length > 1) throw new InvalidDataException("The existing livery has duplicate streamed filenames. Resolve the ambiguity before previewing it.");
                string source;
                if (existing.Paths.Length == 1) source = existing.Paths[0];
                else
                {
                    RpfFileEntry? entry = GameLiveryTemplates.FindActiveYft(Game(), existing.Model);
                    if (entry == null) throw new InvalidDataException("This livery YFT is absent from both the repository and the selected GTA DLC level.");
                    source = Path.Combine(result.DirectoryPath, "existing-source.yft");
                    File.WriteAllBytes(source, GameLiveryTemplates.ReadNative(entry));
                }
                string texture = LiveryAssetBuilder.SuggestTexture(source) ?? throw new InvalidDataException("Select a livery with an unambiguous embedded livery sampler for preview.");
                LiveryAssetBuilder.CopyForPreview(source, texture, unique, pending, cancellation);
                mod = new YftFile(); mod.Load(File.ReadAllBytes(pending));
            }
            var modXml = mod == null ? new XDocument(new XElement("Fragment")) : XDocument.Parse(YftXml.GetXml(mod));
            var modelXml = XDocument.Parse(YftXml.GetXml(model));
            PreviewMaterialSupport.Validate(modelXml);
            var samplers = modXml.Descendants("Shaders").Elements("Item").SelectMany(shader =>
                shader.Element("Parameters")?.Elements("Item").Where(item => (string?)item.Attribute("type") == "Texture" && item.Element("Name")?.Value == unique)
                    .Select(item => (Shader: shader.Element("Name")?.Value, File: shader.Element("FileName")?.Value, Sampler: (string?)item.Attribute("name"))) ?? []).ToArray();
            if (mod != null && (samplers.Length == 0 || samplers.Any(sampler => sampler.Sampler != "DiffuseSampler2")))
                throw new InvalidDataException("This livery uses an unsupported material binding. Preview requires a matching vehicle livery sampler; a generic overlay would be inaccurate.");
            int applied = 0;
            foreach (XElement shader in modelXml.Descendants("Shaders").Elements("Item"))
            foreach (var sampler in samplers.Where(sampler => PreviewMaterialSupport.LiveryShaderMatches(shader.Element("Name")?.Value, shader.Element("FileName")?.Value, sampler.Shader, sampler.File)))
            foreach (XElement parameter in shader.Element("Parameters")?.Elements("Item").Where(item => (string?)item.Attribute("name") == sampler.Sampler && (string?)item.Attribute("type") == "Texture") ?? [])
            {
                parameter.SetElementValue("Name", unique);
                applied++;
            }
            if (mod != null && applied == 0) throw new InvalidDataException("The real vehicle has no material matching the template's livery sampler. Preview stopped to avoid showing an inaccurate result.");
            var textures = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
            void Add(YtdFile? ytd)
            {
                foreach (Texture texture in ytd?.TextureDict?.Textures?.data_items ?? []) textures.TryAdd(texture.Name, texture);
            }
            foreach (Texture texture in mod?.Fragment?.Drawable?.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? []) textures.TryAdd(texture.Name, texture);
            // Each drawable's own embedded textures take precedence when constructing its dictionary below.
            if (vehicle.TexturePaths.Length > 1) throw new InvalidDataException("Multiple texture dictionaries match the vehicle. Resolve duplicates before previewing.");
            if (vehicle.TexturePaths.Length == 1)
            {
                var ytd = new YtdFile(); ytd.Load(NativeAssetReader.Read(vehicle.TexturePaths[0])); Add(ytd);
            }
            else if (vehicle.ModelPaths.Length == 0 || model.Fragment.Drawable.ShaderGroup?.TextureDictionary?.Textures?.data_items?.Length == 0)
                Add(ArchiveTextures(vehicle.TextureDictionary));
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string parentName = vehicle.TextureDictionary;
            while (parents.Add(parentName))
            {
                string[] repoParents = vehicle.MetadataPaths.SelectMany(path => XDocument.Load(path).Descendants("txdRelationships").Elements("Item"))
                    .Where(item => item.Element("child")?.Value.Equals(parentName, StringComparison.OrdinalIgnoreCase) == true)
                    .Select(item => item.Element("parent")?.Value).Where(value => value != null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (repoParents.Length > 1) throw new InvalidDataException("Ambiguous texture dictionary parent.");
                if (repoParents.Length == 0) break;
                parentName = repoParents[0];
                string[] paths = catalog.Files.Where(path => Path.GetFileName(path).Equals(parentName + ".ytd", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (paths.Length > 1) throw new InvalidDataException("Duplicate parent texture dictionaries: " + parentName);
                if (paths.Length == 1) { var ytd = new YtdFile(); ytd.Load(NativeAssetReader.Read(paths[0])); Add(ytd); }
                else Add(ArchiveTextures(parentName));
            }
            // A repository parent can itself inherit shared textures from a game dictionary.
            uint parentHash = JenkHash.GenHash(parentName.ToLowerInvariant());
            var visitedHashes = new HashSet<uint>();
            if (game != null)
            while (visitedHashes.Add(parentHash) && (parentHash = Game().TryGetParentYtdHash(parentHash)) != 0)
            {
                RpfFileEntry? entry = Game().GetYtdEntry(parentHash);
                if (entry == null) break;
                var inherited = new YtdFile(); inherited.Load(entry.File.ExtractFile(entry), entry); Add(inherited);
            }
            var embeddedNames = modelXml.Descendants("TextureDictionary").Elements("Item").Select(item => item.Element("Name")?.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool needShared = modelXml.Descendants("Parameters").Elements("Item").Where(item => (string?)item.Attribute("type") == "Texture")
                .Select(item => item.Element("Name")?.Value).Any(name => name != null && !textures.ContainsKey(name) && !embeddedNames.Contains(name));
            if (needShared) Add(ArchiveTextures("vehshare"));
            // Some addons reference a stock texture from a vehicle dictionary outside their declared
            // parent chain. This exact texture is supplied by dinghy.ytd (not vehshare.ytd).
            // Resolve the real payload from the user's game installation; never invent a normal map.
            if (modelXml.Descendants("Parameters").Elements("Item").Any(item =>
                (string?)item.Attribute("type") == "Texture" && item.Element("Name")?.Value == "vehicle_generic_rubber_bump") &&
                !textures.ContainsKey("vehicle_generic_rubber_bump") && !embeddedNames.Contains("vehicle_generic_rubber_bump"))
            {
                RpfFileEntry? dependency = Game().GetYtdEntry(JenkHash.GenHash("dinghy"));
                if (dependency != null && (Game().BaseRpfs.Contains(dependency.File) || Game().ActiveMapRpfFiles.Values.Contains(dependency.File) ||
                    Game().DlcActiveRpfs.Any(rpf => dependency.Path.StartsWith(rpf.Path + "\\", StringComparison.OrdinalIgnoreCase))))
                {
                    var donor = new YtdFile(); donor.Load(dependency.File.ExtractFile(dependency), dependency);
                    Texture? rubber = donor.TextureDict?.Textures?.data_items?.SingleOrDefault(texture => texture.Name == "vehicle_generic_rubber_bump");
                    if (rubber != null)
                    {
                        textures.Add(rubber.Name, rubber);
                        progress.Report($"Resolved {rubber.Name} from {dependency.Path}");
                    }
                }
            }
            string export = Path.Combine(result.DirectoryPath, "textures");
            Directory.CreateDirectory(export);
            // Export the actual embedded textures first, then supplement with resolved dictionaries.
            modelXml = XDocument.Parse(YftXml.GetXml(model, export));
            foreach (XElement shader in modelXml.Descendants("Shaders").Elements("Item"))
            foreach (var sampler in samplers.Where(sampler => PreviewMaterialSupport.LiveryShaderMatches(shader.Element("Name")?.Value, shader.Element("FileName")?.Value, sampler.Shader, sampler.File)))
            foreach (XElement parameter in shader.Element("Parameters")?.Elements("Item").Where(item => (string?)item.Attribute("name") == sampler.Sampler && (string?)item.Attribute("type") == "Texture") ?? [])
                parameter.SetElementValue("Name", unique);
            var dictionary = new TextureDictionary(); dictionary.BuildFromTextureList(textures.Values.ToList());
            string supplement = Path.Combine(result.DirectoryPath, "supplement");
            Directory.CreateDirectory(supplement);
            var ytdXml = XDocument.Parse(YtdXml.GetXml(new YtdFile { TextureDict = dictionary }, supplement));
            foreach (string file in Directory.GetFiles(supplement, "*.dds"))
            {
                string target = Path.Combine(export, Path.GetFileName(file));
                if (!File.Exists(target)) File.Copy(file, target);
            }
            XElement[] textureItems = ytdXml.Descendants("TextureDictionary").Elements("Item").ToArray();
            // YtdXml uses TextureDictionary as the document root.
            if (textureItems.Length == 0) textureItems = ytdXml.Root!.Elements("Item").ToArray();
            var missing = new HashSet<string>();
            foreach (XElement shaderGroup in modelXml.Descendants("ShaderGroup"))
            {
                XElement embedded = shaderGroup.Element("TextureDictionary") ?? new XElement("TextureDictionary");
                if (embedded.Parent == null) shaderGroup.AddFirst(embedded);
                var shaderTextureNames = embedded.Elements("Item").Select(item => item.Element("Name")?.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string name in shaderGroup.Descendants("Parameters").Elements("Item")
                    .Where(item => (string?)item.Attribute("type") == "Texture").Select(item => item.Element("Name")?.Value).Where(value => value != null).Cast<string>().Distinct())
                {
                    if (shaderTextureNames.Contains(name)) continue;
                    XElement? item = textureItems.FirstOrDefault(item => item.Element("Name")?.Value.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
                    if (item == null) missing.Add(name); else embedded.Add(new XElement(item));
                }
            }
            if (missing.Count > 0) throw new InvalidDataException("Preview dependencies missing: " + string.Join(", ", missing.Take(20)) + ". Supply the vehicle's texture parent/dependencies; no substitute textures were used.");
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllBytes(result.ModelPath, XmlYft.GetYft(modelXml.ToString(), export).Save());
            var verify = new YftFile(); verify.Load(File.ReadAllBytes(result.ModelPath));
            if (verify.Fragment?.Drawable == null) throw new InvalidDataException("Staged vehicle failed native reload.");
            result.Description = $"{vehicle.Model} • {(existing != null ? $"{existing.DisplayName} · slot {existing.Slot} · {applied} existing livery bindings" : import == null ? "Vehicle only" : $"{applied} pending livery bindings")} • {modelSource}\r\nOffline BadWalker preview: paint/lighting/reflections and mod selection can differ from FiveM. No repository files changed.";
            // Exported DDS files are no longer needed once the self-contained native preview has been compiled.
            Directory.Delete(export, true); Directory.Delete(supplement, true); File.Delete(pending);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
}
