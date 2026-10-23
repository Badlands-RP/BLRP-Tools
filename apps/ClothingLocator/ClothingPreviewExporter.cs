using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeWalker.GameFiles;
using SkiaSharp;
using System.Collections.Concurrent;

namespace BLRP.ClothingLocator;

internal sealed record ClothingPreviewTexture(int Index, string Name, Func<byte[]> Read);
internal sealed record ClothingPreviewSource(Gender Gender, ComponentDefinition Component, int Drawable,
    string Collection, int RelativeDrawable, string Origin, Func<byte[]> ReadModel,
    IReadOnlyList<ClothingPreviewTexture> Textures, System.Numerics.Matrix4x4? Attachment = null, uint CollectionHash = 0,
    byte[]? ReferenceHead = null);
internal sealed record ClothingPreviewProgress(int Completed, int Total, int Written, int Unchanged, int Restricted, int Failed);
internal sealed record ClothingPreviewResult(int Written, int Unchanged, int Restricted, int Failed, string ManifestPath);
internal sealed record ClothingPreviewImage(string Fingerprint, string ImageHash, string File, string Gender,
    string Category, int Drawable, int Texture, string Collection, int RelativeDrawable, string Source,
    string TextureName, string? Restriction, uint CollectionHash = 0, bool FacialHair = false);
internal sealed class ClothingPreviewManifest
{
    public int Version { get; set; } = 1;
    public int GameBuild { get; set; }
    public int Size { get; set; }
    public bool PublicOnly { get; set; }
    public bool Completed { get; set; }
    public ConcurrentDictionary<string, ClothingPreviewImage> Images { get; set; } = new();
    public ConcurrentDictionary<string, string> Errors { get; set; } = new();
    public ConcurrentDictionary<string, string> Placeholders { get; set; } = new();
}

internal static class ClothingPreviewExporter
{
    public const string ManifestName = "clothing-previews.json";
    public const string CatalogueName = "catalogue.json";
    public const string LookupName = "lookup.json";
    // Bump when framing, material handling or rendering changes.
    private const int RendererVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static bool IsFacialHair(ClothingPreviewSource source)
    {
        if (source.Component.IsProp) return false;
        // Reviewed source identities, not global drawable numbers or shader/bounds
        // guesses: police equipment can use the same shaders and attachment area.
        // New accessories stay in their native category until reviewed here.
        return (source.Gender, source.Collection, source.Component.Slot) switch
        {
            (Gender.Male, "mp_m_freemode_01_mp_m_c_addons_i", 1) =>
                source.RelativeDrawable is >= 0 and <= 3 or 63 or 70 or >= 72 and <= 83 or >= 93 and <= 96,
            (Gender.Male, "mp_m_freemode_01_mp_m_c_addons_i", 5) =>
                source.RelativeDrawable is >= 58 and <= 90 or 106 or 124,
            (Gender.Male, "mp_m_freemode_01_mp_m_c_addons_ii", 5) =>
                source.RelativeDrawable is >= 15 and <= 21,
            (Gender.Female, "mp_f_freemode_01_mp_f_c_addons_i", 5) =>
                source.RelativeDrawable == 72,
            _ => false,
        };
    }

    public static async Task<ClothingPreviewResult> RunAsync(string root, string blacklistDirectory, string output,
        int build, int size, bool publicOnly, IProgress<ClothingPreviewProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (build <= 0) throw new ArgumentException("Select the server game build.");
        string destination = Path.GetFullPath(output);
        foreach (string sourceDirectory in new[] { root, blacklistDirectory })
        {
            string source = Path.GetFullPath(sourceDirectory);
            if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)
                || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an output folder outside the source clothing and blacklist folders.");
        }
        IReadOnlyDictionary<string, string> rules = ClothingBlacklist.ReadPreviewRestrictions(blacklistDirectory);
        ClothingCatalog custom = await ClothingCatalog.LoadAsync(root, cancellationToken);
        BaseGameCatalog rockstar = await BaseGameCatalog.LoadAsync(cancellationToken, build);
        return await Task.Run(() =>
        {
            var offsets = new Dictionary<(Gender, string), int>();
            foreach (Gender gender in Enum.GetValues<Gender>())
                foreach (ComponentDefinition component in ClothingComponents.All)
                    offsets[(gender, component.Code)] = rockstar.GetDrawableCount(gender, component);
            var attachments = new Dictionary<(Gender, int), System.Numerics.Matrix4x4>();
            foreach (Gender gender in Enum.GetValues<Gender>())
                foreach (ComponentDefinition component in ClothingComponents.All.Where(item => item.IsProp))
                    attachments[(gender, component.Slot)] = rockstar.GetPropTransform(gender, component.Slot);
            var referenceHeads = Enum.GetValues<Gender>().ToDictionary(gender => gender, rockstar.GetReferenceHead);
            ClothingPreviewSource[] sources = rockstar.GetPreviewSources().Concat(CustomSources(custom, offsets))
                .Select(source => source with
                {
                    Attachment = source.Component.IsProp ? attachments[(source.Gender, source.Component.Slot)] : null,
                    ReferenceHead = !source.Component.IsProp && source.Component.Slot is 1 or 5 ? referenceHeads[source.Gender] : null,
                }).ToArray();
            return Export(sources, output, build, size, publicOnly, rules, progress, cancellationToken, bundleImages: false);
        }, cancellationToken);
    }

    public static IEnumerable<ClothingPreviewSource> CustomSources(ClothingCatalog catalog,
        IReadOnlyDictionary<(Gender Gender, string Component), int> offsets)
    {
        var metadata = new Dictionary<string, PedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (ClothingEntry entry in catalog.Entries)
        {
            string letter = entry.Gender == Gender.Male ? "m" : "f";
            string collection = $"mp_{letter}_freemode_01_mp_{letter}_c_addons_{new[] { "i", "ii", "iii", "iv", "v" }[entry.Pack - 1]}";
            string ymtPath = Path.Combine(catalog.RootPath, $"clothing_addon_{entry.Pack}", "stream", collection + ".ymt");
            if (!metadata.TryGetValue(ymtPath, out PedFile? ped))
            {
                ped = RpfFile.GetResourceFile<PedFile>(File.ReadAllBytes(ymtPath))
                    ?? throw new InvalidDataException("Could not read clothing YMT: " + ymtPath);
                metadata.Add(ymtPath, ped);
            }
            string prefix = Path.GetFileName(entry.FilePath).Split('^')[0];
            string folder = Path.GetDirectoryName(entry.FilePath)!;
            var textures = new List<ClothingPreviewTexture>();
            if (entry.Component.IsProp)
            {
                MCPedPropMetaData prop = ped.VariationInfo?.PropInfo?.PropMetaData?.FirstOrDefault(item =>
                    item.Data.anchorId == entry.Component.Slot && item.Data.propId == entry.RelativeIndex)
                    ?? throw new InvalidDataException("Missing prop metadata: " + entry.FilePath);
                for (int index = 0; index < (prop.TexData?.Length ?? 0); index++)
                {
                    string name = $"{entry.Component.Code}_diff_{entry.RelativeIndex:000}_{(char)('a' + index)}";
                    string path = Path.Combine(folder, prefix + "^" + name + ".ytd");
                    textures.Add(new ClothingPreviewTexture(index, name, () => File.ReadAllBytes(path)));
                }
            }
            else
            {
                MCPVDrawblData drawable = ped.VariationInfo?.GetComponentData(entry.Component.Slot)?.DrawblData3?
                    .ElementAtOrDefault(entry.RelativeIndex) ?? throw new InvalidDataException("Missing drawable metadata: " + entry.FilePath);
                drawable.ComponentType = entry.Component.Slot;
                drawable.DrawableIndex = entry.RelativeIndex;
                for (int index = 0; index < (drawable.TexData?.Length ?? 0); index++)
                {
                    string name = drawable.GetTextureName(index);
                    string path = Path.Combine(folder, prefix + "^" + name + ".ytd");
                    textures.Add(new ClothingPreviewTexture(index, name, () => File.ReadAllBytes(path)));
                }
            }
            yield return new ClothingPreviewSource(entry.Gender, entry.Component,
                catalog.GetGlobalIndex(entry, offsets[(entry.Gender, entry.Component.Code)]), collection,
                entry.RelativeIndex, "Custom", () => File.ReadAllBytes(entry.FilePath), textures,
                CollectionHash: ped.VariationInfo!.Data.dlcName.Hash);
        }
    }

    public static ClothingPreviewResult Export(IReadOnlyList<ClothingPreviewSource> sources, string outputDirectory,
        int gameBuild, int size, bool publicOnly, IReadOnlyDictionary<string, string> restrictions,
        IProgress<ClothingPreviewProgress>? progress = null, CancellationToken cancellationToken = default, bool bundleImages = true)
    {
        if (size is < 64 or > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        using var exportLock = new FileStream(Path.Combine(output, ".clothing-previews.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        string manifestPath = Path.Combine(output, ManifestName);
        ClothingPreviewManifest previous = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<ClothingPreviewManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("The preview manifest is empty.")
            : new ClothingPreviewManifest();
        if (previous.Version != 1) throw new InvalidDataException("Unsupported preview manifest version.");
        foreach (ClothingPreviewImage image in previous.Images.Values) ValidateFileName(image.File);
        var managedFiles = previous.Images.Values.Select(image => image.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifest = new ClothingPreviewManifest { GameBuild = gameBuild, Size = size, PublicOnly = publicOnly };
        int total = sources.Sum(source => Math.Max(1, source.Textures.Count));
        int completed = 0, written = 0, unchanged = 0, restricted = 0;
        var fileNames = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var referenceScenes = new ConcurrentDictionary<string, Lazy<LodPreviewScene>>();
        bool completedExport = false;
        try
        {
            Parallel.ForEach(sources, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(6, Environment.ProcessorCount / 2)),
            }, source =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string category = source.Component.IsProp ? "p" + source.Component.Slot : source.Component.Slot.ToString(CultureInfo.InvariantCulture);
                string gender = source.Gender.ToString().ToLowerInvariant();
                string key = $"{gender}/{category}/{source.Drawable}";
                byte[]? model = null;
                string? modelHash = null;
                LodPreviewModelInfo? modelInfo = null;
                if (source.Textures.Count == 0)
                {
                    manifest.Errors[key] = "No registered YMT textures.";
                    Interlocked.Increment(ref completed);
                }
                foreach (ClothingPreviewTexture texture in source.Textures)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string textureKey = key + "/" + texture.Index;
                    restrictions.TryGetValue(key + "/*", out string? restriction);
                    if (restriction == null) restrictions.TryGetValue(textureKey, out restriction);
                    if (restriction == "!blocked" || (publicOnly && restriction != null))
                    {
                        Interlocked.Increment(ref restricted);
                        Interlocked.Increment(ref completed);
                        continue;
                    }
                    string name = $"{category}_{gender}_{source.Drawable}_{texture.Index}.webp";
                    if (!fileNames.TryAdd(name, 0)) throw new InvalidDataException("Duplicate clothing preview destination: " + name);
                    try
                    {
                        model ??= source.ReadModel();
                        modelInfo ??= LodPreviewScene.InspectModel(model, !source.Component.IsProp && source.Component.Slot is 1 or 5);
                        if (modelInfo.Placeholder)
                        {
                            manifest.Placeholders[key] = "Empty slot: tiny placeholder quad.";
                            Interlocked.Increment(ref completed);
                            continue;
                        }
                        modelHash ??= Hash(model);
                        byte[] textureBytes = texture.Read();
                        // Invalidate only models affected by the rendering changes.
                        int rendererVersion = source.Component.IsProp && source.Component.Slot == 0 ? 4 : RendererVersion;
                        if (modelInfo.Emissive || (source.Component.IsProp && modelInfo.Skinned)) rendererVersion = 5;
                        bool facialHair = IsFacialHair(source);
                        bool faceAccessory = facialHair && source.ReferenceHead != null;
                        bool frontMounted = faceAccessory || (!source.Component.IsProp && source.Component.Slot == 5 && modelInfo.FrontMounted);
                        if (frontMounted) rendererVersion = 6;
                        string fingerprint = Hash(Encoding.UTF8.GetBytes($"{rendererVersion}|{gameBuild}|{size}|{name}|{source.Attachment}|{modelHash}|{Hash(textureBytes)}"));
                        string? referenceHash = faceAccessory ? Hash(source.ReferenceHead!) : null;
                        if (referenceHash != null) fingerprint = Hash(Encoding.UTF8.GetBytes(fingerprint + "|head:" + referenceHash));
                        string destination = Path.Combine(output, name);
                        ClothingPreviewImage? old = previous.Images.GetValueOrDefault(textureKey);
                        string imageHash;
                        if (old?.Fingerprint == fingerprint && File.Exists(destination) && Hash(File.ReadAllBytes(destination)) == old.ImageHash)
                        {
                            Interlocked.Increment(ref unchanged);
                            imageHash = old.ImageHash;
                        }
                        else
                        {
                            LodPreviewScene scene = LodPreviewScene.Load(model, ClothingPreviewLod.High, textureBytes, true, source.Attachment);
                            if (referenceHash != null)
                            {
                                LodPreviewScene head = referenceScenes.GetOrAdd(referenceHash, _ => new Lazy<LodPreviewScene>(() =>
                                    LodPreviewScene.Load(source.ReferenceHead!, ClothingPreviewLod.High, null))).Value;
                                scene = scene.WithReferenceHead(head);
                            }
                            using Bitmap bitmap = scene.RenderThumbnail(size, size, source.Component.Slot, source.Component.IsProp, frontMounted: frontMounted);
                            using var buffer = new MemoryStream();
                            bitmap.Save(buffer, ImageFormat.Png);
                            using var webpBitmap = SKBitmap.Decode(buffer.ToArray());
                            using var image = SKImage.FromBitmap(webpBitmap);
                            using SKData encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
                            byte[] bytes = encoded.ToArray();
                            imageHash = Hash(bytes);
                            if (File.Exists(destination) && !managedFiles.Contains(name)
                                && Hash(File.ReadAllBytes(destination)) != imageHash)
                                throw new IOException("An unmanaged file already exists: " + name);
                            AtomicWrite(destination, bytes);
                            Interlocked.Increment(ref written);
                        }
                        manifest.Images[textureKey] = new ClothingPreviewImage(fingerprint, imageHash, name, gender,
                            category, source.Drawable, texture.Index, source.Collection, source.RelativeDrawable,
                            source.Origin, texture.Name, restriction, source.CollectionHash, facialHair);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        manifest.Errors[textureKey] = exception.Message;
                        if (manifest.Errors.Count <= 8) Console.Error.WriteLine(textureKey + ": " + exception.Message);
                    }
                    Interlocked.Increment(ref completed);
                    progress?.Report(new ClothingPreviewProgress(completed, total, written, unchanged, restricted, manifest.Errors.Count));
                }
            });
            cancellationToken.ThrowIfCancellationRequested();
            completedExport = true;
        }
        finally
        {
            // A cancelled run remains resumable without re-rendering the work
            // already completed. Incomplete manifests are never publishable.
            if (!completedExport)
            {
                foreach ((string key, ClothingPreviewImage value) in previous.Images)
                    manifest.Images.TryAdd(key, value);
                AtomicWrite(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
            }
        }
        // Only prune files recorded by our previous manifest. This also removes
        // public previews that became restricted since the last export.
        var currentFiles = manifest.Images.Values.Select(image => image.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string name in previous.Images.Values.Select(image => image.File).Distinct())
        {
            if (!currentFiles.Contains(name)) File.Delete(Path.Combine(output, name));
        }
        manifest.Completed = true;
        if (bundleImages) WriteBundles(output, manifest.Images.Values, cancellationToken);
        else AtomicWrite(Path.Combine(output, LookupName), JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Images = manifest.Images.Values.OrderBy(image => image.File, StringComparer.Ordinal)
                .ToDictionary(image => image.File, image => image.ImageHash),
        }));
        // Global IDs depend on the game's collection order. The client resolves
        // these source images using the YMT collection hash and local index.
        var catalogue = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var facialHair = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (ClothingPreviewImage image in manifest.Images.Values)
        {
            string key = $"{image.Gender}/{image.Category}/{image.CollectionHash}/{image.RelativeDrawable}";
            if (catalogue.TryGetValue(key, out int drawable) && drawable != image.Drawable)
                throw new InvalidDataException("Duplicate preview collection identity: " + key);
            catalogue[key] = image.Drawable;
            if (image.FacialHair) facialHair[key] = true;
        }
        AtomicWrite(Path.Combine(output, CatalogueName), JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, GameBuild = gameBuild, Drawables = catalogue, FacialHair = facialHair
        }));
        AtomicWrite(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        progress?.Report(new ClothingPreviewProgress(total, total, written, unchanged, restricted, manifest.Errors.Count));
        return new ClothingPreviewResult(written, unchanged, restricted, manifest.Errors.Count, manifestPath);
    }

    private static void WriteBundles(string output, IEnumerable<ClothingPreviewImage> images, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(output, "bundles");
        Directory.CreateDirectory(directory);
        string lookupPath = Path.Combine(output, LookupName);
        string[] previousBundles = [];
        if (File.Exists(lookupPath))
        {
            using JsonDocument previous = JsonDocument.Parse(File.ReadAllText(lookupPath));
            previousBundles = previous.RootElement.GetProperty("Bundles").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            if (previousBundles.Any(name => !System.Text.RegularExpressions.Regex.IsMatch(name, @"^bundle_[A-F0-9]{64}\.bin$")))
                throw new InvalidDataException("Unsafe bundle filename in preview lookup.");
        }
        var bundles = new List<string>();
        var entries = new Dictionary<string, long[]>();
        // Sixteen local drawables per bundle keeps later additions from
        // repacking unrelated collections and avoids thousands of resource files.
        foreach (var group in images.GroupBy(image => $"{image.Gender}/{image.Category}/{image.CollectionHash}/{image.RelativeDrawable / 16}")
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var buffer = new MemoryStream();
            foreach (ClothingPreviewImage image in group.OrderBy(image => image.RelativeDrawable).ThenBy(image => image.Texture))
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(output, image.File));
                entries[image.File] = [bundles.Count, buffer.Position, bytes.Length];
                buffer.Write(bytes);
            }
            byte[] contents = buffer.ToArray();
            string hash = Hash(contents);
            string name = "bundle_" + hash + ".bin";
            string destination = Path.Combine(directory, name);
            if (!File.Exists(destination) || Hash(File.ReadAllBytes(destination)) != hash) AtomicWrite(destination, contents);
            bundles.Add(name);
        }
        AtomicWrite(lookupPath, JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Bundles = bundles, Images = entries }));
        var currentBundles = bundles.ToHashSet(StringComparer.Ordinal);
        foreach (string name in previousBundles.Where(name => !currentBundles.Contains(name)))
            File.Delete(Path.Combine(directory, name));
    }

    private static void ValidateFileName(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^(?:p)?\d+_(?:male|female)_\d+_\d+\.webp$"))
            throw new InvalidDataException("Unsafe filename in preview manifest: " + name);
    }

    internal static bool SelfTest(string modelPath, string firstTexturePath, string secondTexturePath)
    {
        string output = Path.Combine(Path.GetTempPath(), "BLRP-Preview-Export-Check-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] model = File.ReadAllBytes(modelPath);
            if (LodPreviewScene.InspectModel(model).Placeholder) throw new ArgumentException("Choose a garment, not an empty-slot placeholder.");
            byte[] first = File.ReadAllBytes(firstTexturePath);
            byte[] second = File.ReadAllBytes(secondTexturePath);
            if (!LodPreviewScene.SelfTest(modelPath, firstTexturePath)) throw new InvalidOperationException("Front/back accessory rendering failed.");
            if (Hash(first) == Hash(second)) throw new ArgumentException("Choose two different textures for the same garment.");
            byte[] selected = first;
            ComponentDefinition component = ClothingComponents.ByCode["jbib"];
            ClothingPreviewSource Source(int drawable, IReadOnlyList<ClothingPreviewTexture> textures) =>
                new(Gender.Male, component, drawable, "check", drawable + 40, "Check", () => model, textures,
                    CollectionHash: JenkHash.GenHash("check"));
            var sources = new[]
            {
                Source(0, [new(0, "first", () => first), new(1, "selected", () => selected)]),
                Source(1, [new(0, "blocked", () => first)]),
                Source(2, [new(0, "restricted", () => first)]),
            };
            var rules = new Dictionary<string, string> { ["male/11/1/*"] = "!blocked", ["male/11/2/*"] = "LEO" };
            void Check(bool success, string message)
            {
                if (!success) throw new InvalidOperationException(message);
            }
            var latestProgress = new LatestPreviewProgress<ClothingPreviewProgress>();
            Parallel.For(0, 100000, index => latestProgress.Report(new(index, 100000, 0, index, 0, 0)));
            var finalProgress = new ClothingPreviewProgress(100000, 100000, 0, 100000, 0, 0);
            latestProgress.Report(finalProgress);
            Check(latestProgress.Take() == finalProgress && latestProgress.Take() == null,
                "Rapid progress reports must collapse to one latest snapshot, not queue UI callbacks.");
            ClothingPreviewSource Accessory(Gender gender, string collection, string code, int relative) =>
                Source(9000, []) with { Gender = gender, Collection = collection,
                    Component = ClothingComponents.ByCode[code], RelativeDrawable = relative };
            const string malePack = "mp_m_freemode_01_mp_m_c_addons_i";
            Check(IsFacialHair(Accessory(Gender.Male, malePack, "hand", 84))
                && IsFacialHair(Accessory(Gender.Male, malePack, "berd", 0))
                && IsFacialHair(Accessory(Gender.Male, "mp_m_freemode_01_mp_m_c_addons_ii", "hand", 15))
                && IsFacialHair(Accessory(Gender.Female, "mp_f_freemode_01_mp_f_c_addons_i", "hand", 72)),
                "Reviewed facial hair must survive changes to global drawable numbering.");
            Check(!IsFacialHair(Accessory(Gender.Male, malePack, "berd", 4))
                && !IsFacialHair(Accessory(Gender.Male, malePack, "hand", 10))
                && !IsFacialHair(Accessory(Gender.Male, malePack, "hand", 108))
                && !IsFacialHair(Accessory(Gender.Male, malePack, "hand", 123))
                && !IsFacialHair(Accessory(Gender.Male, "unreviewed", "hand", 84)),
                "Body cameras, badges, earrings, flowers and unreviewed items must not become facial hair.");
            ClothingPreviewResult initial = Export(sources, output, 3095, 128, false, rules);
            Check(initial.Written == 3 && initial.Restricted == 1 && initial.Failed == 0, "Initial export or whole-drawable restrictions failed.");
            using (JsonDocument catalogue = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, CatalogueName))))
            {
                JsonElement drawables = catalogue.RootElement.GetProperty("Drawables");
                string prefix = $"male/11/{JenkHash.GenHash("check")}/";
                Check(drawables.GetProperty(prefix + "40").GetInt32() == 0
                    && !drawables.TryGetProperty(prefix + "41", out _), "Collection lookup or blacklist exclusion failed.");
            }
            using (JsonDocument lookup = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, LookupName))))
            {
                JsonElement entry = lookup.RootElement.GetProperty("Images").GetProperty("11_male_0_1.webp");
                string bundle = lookup.RootElement.GetProperty("Bundles")[entry[0].GetInt32()].GetString()!;
                byte[] packed = File.ReadAllBytes(Path.Combine(output, "bundles", bundle));
                Check(packed.AsSpan(entry[1].GetInt32(), entry[2].GetInt32()).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "11_male_0_1.webp"))),
                    "The bundled preview does not match its WebP source.");
            }
            ClothingPreviewResult cached = Export(sources, output, 3095, 128, false, rules);
            Check(cached.Unchanged == 3 && cached.Written == 0, "Unchanged images were rendered again.");
            string originalHash = JsonSerializer.Deserialize<ClothingPreviewManifest>(File.ReadAllText(cached.ManifestPath))!
                .Images["male/11/0/1"].ImageHash;
            selected = second;
            ClothingPreviewResult changed = Export(sources, output, 3095, 128, false, rules);
            Check(changed.Written == 1 && changed.Unchanged == 2, "A texture change did not invalidate exactly one preview.");
            Check(JsonSerializer.Deserialize<ClothingPreviewManifest>(File.ReadAllText(changed.ManifestPath))!
                .Images["male/11/0/1"].ImageHash != originalHash, "The selected texture did not change the visible preview.");
            ClothingPreviewResult publicOnly = Export(sources, output, 3095, 128, true, rules);
            Check(publicOnly.Unchanged == 2 && publicOnly.Restricted == 2 && !File.Exists(Path.Combine(output, "11_male_2_0.webp")), "Restricted previews were not pruned.");
            rules["male/11/0/1"] = "!blocked";
            ClothingPreviewResult textureRule = Export(sources, output, 3095, 128, true, rules);
            Check(textureRule.Unchanged == 1 && textureRule.Restricted == 3 && !File.Exists(Path.Combine(output, "11_male_0_1.webp")), "Texture restrictions were not honoured.");
            using (JsonDocument lookup = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, LookupName))))
            {
                Check(lookup.RootElement.GetProperty("Images").EnumerateObject().Count() == 1, "Blocked previews remained in the bundles.");
                string bundle = lookup.RootElement.GetProperty("Bundles")[0].GetString()!;
                string bundlePath = Path.Combine(output, "bundles", bundle);
                string originalBundleHash = Hash(File.ReadAllBytes(bundlePath));
                File.WriteAllBytes(bundlePath, [0]);
                Check(Export(sources, output, 3095, 128, true, rules).Written == 0
                    && Hash(File.ReadAllBytes(bundlePath)) == originalBundleHash, "A corrupt bundle was not repaired from the image cache.");
            }
            string imagePath = Path.Combine(output, "11_male_0_0.webp");
            using (SKBitmap image = SKBitmap.Decode(imagePath))
            {
                Check(image.Width == 128 && image.Height == 128 && image.GetPixel(0, 0).Alpha == 0, "Image size or transparent background failed.");
                bool visible = false;
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                    {
                        visible |= image.GetPixel(x, y).Alpha > 0;
                        if (x == 0 || y == 0 || x == image.Width - 1 || y == image.Height - 1)
                            Check(image.GetPixel(x, y).Alpha == 0, "Automatic framing clipped the garment.");
                    }
                Check(visible, "The preview contains no visible garment.");
            }
            File.WriteAllBytes(imagePath, [0]);
            Check(Export(sources, output, 3095, 128, true, rules).Written == 1, "A corrupt cached image was not repaired.");
            Export(sources, output, 3095, 128, true, rules, bundleImages: false);
            using (JsonDocument lookup = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, LookupName))))
            {
                foreach (JsonProperty image in lookup.RootElement.GetProperty("Images").EnumerateObject())
                    Check(image.Value.GetString() == Hash(File.ReadAllBytes(Path.Combine(output, image.Name))), "Remote preview lookup has a stale content hash.");
            }
            BackblazePreviewUpload.SelfTest(output);
            Console.WriteLine("Preview checks passed: alpha, collection lookup, local/remote layouts, cache, corruption, restrictions, pruning, Backblaze retries, checksums and anonymous access.");
            return true;
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void AtomicWrite(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
