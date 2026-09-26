using System.Text.Json;
using CodeWalker.GameFiles;

namespace BLRP.NavMesh;

// Explicit archive order avoids silently using a newer installed update or DLC.
public sealed class GameSourceManifest
{
    public int GameBuild { get; set; }
    public string Source { get; set; } = "";
    public string GameDirectory { get; set; } = "";
    public bool ValidatedForBuild { get; set; }
    public ArchiveInput[] Archives { get; set; } = [];
}

public sealed class ArchiveInput
{
    public string Path { get; set; } = "";
    public string LogicalPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class GameSource
{
    public GameSourceManifest Manifest { get; }
    private readonly Dictionary<(uint, string), RpfFileEntry> entries = [];
    private readonly Dictionary<uint, Archetype> archetypes = [];
    private readonly CollisionScene scene;

    public GameSource(BakeSettings settings, CollisionScene scene)
    {
        this.scene = scene;
        string root = Path.GetDirectoryName(settings.GameSourceFile)!;
        Manifest = JsonSerializer.Deserialize<GameSourceManifest>(File.ReadAllText(settings.GameSourceFile), BakeSettings.Json)
            ?? throw new InvalidDataException("Empty game source manifest.");
        if (Manifest.GameBuild != settings.GameBuild || Manifest.Source.Length == 0 || Manifest.Archives.Length == 0)
            throw new InvalidDataException("Game source requires matching gameBuild, source and ordered archives.");
        scene.Inputs[settings.GameSourceFile] = CollisionScene.Hash(settings.GameSourceFile);
        if (!Manifest.ValidatedForBuild) scene.Issues.Add("Game archive selection has not been validated for the target build.");
        GTA5Keys.LoadFromPath(Path.GetFullPath(Manifest.GameDirectory, root), false, null);
        foreach (var archive in Manifest.Archives)
        {
            string path = Path.GetFullPath(archive.Path, root);
            if (archive.Sha256.Length == 0)
            {
                if (Manifest.ValidatedForBuild) throw new InvalidDataException($"Validated archive requires SHA256: {path}");
                scene.Issues.Add($"Archive not pinned by SHA256: {path}");
            }
            else
            {
                string hash = CollisionScene.Hash(path);
                if (!hash.Equals(archive.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Archive hash mismatch: {path}");
                scene.Inputs[path] = hash;
            }
            if (string.IsNullOrWhiteSpace(archive.LogicalPath)) throw new InvalidDataException("Archive logicalPath is required.");
            var rpf = new RpfFile(path, archive.LogicalPath);
            // Cached archives have hash-suffixed physical filenames; NG decryption uses the original name.
            rpf.Name = Path.GetFileName(archive.LogicalPath); rpf.NameLower = rpf.Name.ToLowerInvariant();
            rpf.ScanStructure(_ => { }, error => throw new InvalidDataException(error));
            void Index(RpfFile file)
            {
                foreach (var entry in file.AllEntries.OfType<RpfFileEntry>())
                {
                    string extension = Path.GetExtension(entry.NameLower);
                    if (extension is not (".ytyp" or ".ybn" or ".ydr" or ".ydd" or ".yft" or ".ynv")) continue;
                    string name = Path.GetFileNameWithoutExtension(entry.NameLower);
                    JenkIndex.Ensure(name);
                    entries[(JenkHash.GenHash(name), extension)] = entry;
                }
                foreach (var child in file.Children ?? []) Index(child);
            }
            Index(rpf);
        }
        // Only selected archives participate. Resource YTYPs take precedence in CollisionScene.
        foreach (var entry in entries.Where(p => p.Key.Item2 == ".ytyp").Select(p => p.Value))
        {
            var ytyp = Load<YtypFile>(entry);
            foreach (var archetype in ytyp.AllArchetypes ?? []) archetypes[archetype.Hash] = archetype;
        }
        Console.WriteLine($"Game sources: {entries.Count} assets, {archetypes.Count} archetypes.");
    }

    public Archetype? FindArchetype(uint hash) => archetypes.GetValueOrDefault(hash);
    public bool Contains(uint hash, string extension) => entries.ContainsKey((hash, extension));
    public T Load<T>(uint hash, string extension) where T : class, PackedFile, new() => Load<T>(entries[(hash, extension)]);

    private T Load<T>(RpfFileEntry entry) where T : class, PackedFile, new()
    {
        var data = entry.File.ExtractFile(entry) ?? throw new InvalidDataException($"Cannot extract {entry.Path}");
        scene.Inputs[entry.File.GetPhysicalFilePath() + "::" + entry.Path] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        var file = new T(); file.Load(data, entry); return file;
    }

    public void CaptureBaseline(BakeSettings settings, string directory)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Baseline output must be empty.");
        Directory.CreateDirectory(directory);
        var baseline = new BaselineManifest { GameBuild = Manifest.GameBuild, Source = Manifest.Source,
            ValidatedForBuild = Manifest.ValidatedForBuild };
        int x0 = Math.Max(0, NavMeshCompiler.Cell(settings.Min[0]) - 1), x1 = Math.Min(99, NavMeshCompiler.Cell(settings.Max[0]) + 1);
        int y0 = Math.Max(0, NavMeshCompiler.Cell(settings.Min[1]) - 1), y1 = Math.Min(99, NavMeshCompiler.Cell(settings.Max[1]) + 1);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            string name = NavMeshCompiler.TileName(x + y * 100);
            uint hash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(name));
            if (!Contains(hash, ".ynv")) throw new InvalidDataException($"Selected archives do not contain {name}");
            var tile = Load<YnvFile>(hash, ".ynv");
            tile.BuildStructsOnSave = false; // Wrap the original resource blocks; do not rebuild or requantize this baseline.
            var data = tile.Save(); var reload = new YnvFile(); reload.Load(data);
            if (reload.AreaID != tile.AreaID || reload.Polys.Count != tile.Polys.Count)
                throw new InvalidDataException($"Baseline export changed {name}");
            for (int i = 0; i < tile.Polys.Count; i++)
            {
                var a = tile.Polys[i]; var b = reload.Polys[i];
                if (!a.Vertices.SequenceEqual(b.Vertices) || !a.RawData.Equals(b.RawData) ||
                    !a.Edges.Select(e => e.RawData).SequenceEqual(b.Edges.Select(e => e.RawData)))
                    throw new InvalidDataException($"Baseline export changed original polygon data: {name}:{i}");
            }
            File.WriteAllBytes(Path.Combine(directory, name), data);
            baseline.Files[name] = CollisionScene.Hash(Path.Combine(directory, name));
        }
        File.WriteAllText(Path.Combine(directory, "baseline.json"), JsonSerializer.Serialize(baseline, BakeSettings.Json));
        File.WriteAllText(Path.Combine(directory, "sources.json"), JsonSerializer.Serialize(scene.Inputs, BakeSettings.Json));
        Console.WriteLine($"Captured {baseline.Files.Count} original tiles: {directory}");
    }
}
