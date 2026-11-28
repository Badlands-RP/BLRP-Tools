using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace Badlands.LiveryTool;

internal static class GameLiveryTemplates
{
    private sealed record Cached(string Key, string Source, byte[] Hash);
    private static readonly ConcurrentDictionary<string, Cached> assets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string staging = Path.Combine(Path.GetTempPath(), "BLRP-Stock-Livery-" + Guid.NewGuid().ToString("N"));

    static GameLiveryTemplates()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }

    public static Task<VehicleTemplate[]> Resolve(VehicleCatalog catalog, VehicleKit kit, string gtaFolder, string dlc,
        CancellationToken cancellation, IProgress<string> progress)
    {
        VehicleTemplate[] repository = catalog.Templates(kit);
        if (repository.Length > 0) return Task.FromResult(repository);
        if (kit.Liveries.Length == 0) return Task.FromResult(Array.Empty<VehicleTemplate>());
        return VehiclePreviewAssets.ReadGame(gtaFolder, dlc, cancellation, progress, game =>
        {
            var result = new List<VehicleTemplate>();
            string key = Path.GetFullPath(gtaFolder) + "|" + dlc;
            string folder = Path.Combine(staging, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16]);
            foreach (XElement mod in kit.Liveries)
            {
                cancellation.ThrowIfCancellationRequested();
                string name = mod.Element("modelName")?.Value.Trim() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_]+$")) continue;
                RpfFileEntry? entry = FindActiveYft(game, name);
                if (entry == null) continue;
                string target = Path.Combine(folder, name + ".yft");
                if (!assets.TryGetValue(target, out Cached? cached) || !File.Exists(target))
                {
                    progress.Report("Reading stock livery template: " + name);
                    byte[] bytes = ReadNative(entry);
                    var native = new YftFile(); native.Load(bytes);
                    if (native.Fragment?.Drawable == null) throw new InvalidDataException("Stock livery has no drawable fragment: " + entry.Path);
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(target, bytes);
                    cached = new Cached(key, entry.Path, SHA256.HashData(bytes));
                    assets[target] = cached;
                }
                var template = new VehicleTemplate(target, mod, cached.Source);
                if (LiveryAssetBuilder.SuggestTexture(target) != null) result.Add(template);
            }
            return result.ToArray();
        });
    }

    public static bool Matches(VehicleCatalog catalog, VehicleKit kit, VehicleTemplate template)
    {
        if (template.GameSource == null) return catalog.Templates(kit).Any(candidate => candidate.Path == template.Path && XNode.DeepEquals(candidate.Mod, template.Mod));
        return assets.TryGetValue(template.Path, out Cached? cached) && cached.Source == template.GameSource &&
            kit.Liveries.Any(mod => XNode.DeepEquals(mod, template.Mod) && Path.GetFileNameWithoutExtension(template.Path).Equals(mod.Element("modelName")?.Value, StringComparison.OrdinalIgnoreCase)) &&
            File.Exists(template.Path) && SHA256.HashData(File.ReadAllBytes(template.Path)).SequenceEqual(cached.Hash);
    }

    internal static byte[] ReadNative(RpfFileEntry entry)
    {
        if (entry is not RpfResourceFileEntry resource) throw new InvalidDataException("Expected a native GTA resource: " + entry.Path);
        byte[] payload = entry.File.ExtractFile(entry) ?? throw new IOException("Could not read GTA resource: " + entry.Path);
        // RPF extraction returns the decompressed payload. A standalone native file
        // needs its actual resource flags/header and a compressed payload, without reserializing the mesh.
        return ResourceBuilder.AddResourceHeader(resource, ResourceBuilder.Compress(payload));
    }

    internal static RpfFileEntry? FindActiveYft(GameFileCache game, string name)
    {
        bool Active(RpfFile file) => game.BaseRpfs.Contains(file) || game.ActiveMapRpfFiles.Values.Contains(file) ||
            game.DlcActiveRpfs.Any(active => file.Path.StartsWith(active.Path + "\\", StringComparison.OrdinalIgnoreCase));
        uint hash = JenkHash.GenHash(name.ToLowerInvariant());
        RpfFileEntry? indexed = game.GetYftEntry(hash);
        if (indexed != null && Active(indexed.File)) return indexed;
        return game.AllRpfs.Where(Active).SelectMany(file => file.AllEntries ?? []).OfType<RpfFileEntry>()
            .LastOrDefault(entry => entry.NameLower.EndsWith(".yft", StringComparison.OrdinalIgnoreCase) && entry.ShortNameHash == hash);
    }

    public static bool MatchesConfiguration(VehicleTemplate template, string gtaFolder, string dlc) => template.GameSource == null ||
        assets.TryGetValue(template.Path, out Cached? cached) && cached.Key.Equals(Path.GetFullPath(gtaFolder) + "|" + dlc, StringComparison.OrdinalIgnoreCase);
}
