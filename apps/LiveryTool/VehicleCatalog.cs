using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace Badlands.LiveryTool;

internal sealed record VehicleKit(string Path, string Name, XElement Xml)
{
    public XElement[] Liveries => Xml.Element("visibleMods")?.Elements("Item")
        .Where(item => item.Element("type")?.Value == "VMT_LIVERY_MOD").ToArray() ?? [];
    public override string ToString() => $"{Name} — {System.IO.Path.GetDirectoryName(Path)}";
}

internal sealed record VehicleTemplate(string Path, XElement Mod)
{
    public override string ToString() => System.IO.Path.GetFileName(Path);
}

internal sealed record CatalogVehicle(string Model, string DisplayName, string Resource, string ResourcePath,
    string[] MetadataPaths, string[] ModelPaths, string[] TexturePaths, string TextureDictionary,
    VehicleKit[] Kits, string[] Problems)
{
    public string Status => Problems.Length > 0 ? string.Join("; ", Problems) :
        Kits.Length == 0 ? "Needs modkit / metadata setup" :
        Kits.Length > 1 ? "Choose modkit in Advanced" :
        Kits[0].Liveries.Length == 0 ? "No compatible livery mod template yet" : $"{Kits[0].Liveries.Length} livery slots";
    public string Key => Model + " | " + Resource;
    public override string ToString() => $"{Model} — {DisplayName} ({Resource})";
}

internal sealed class VehicleCatalog
{
    public required string Root { get; init; }
    public required string[] Files { get; init; }
    public required CatalogVehicle[] Vehicles { get; init; }
    public required string[] Warnings { get; init; }
    public required string GameBuild { get; init; }
    public VehicleKit[] Kits { get; init; } = [];

    public VehicleTemplate[] Templates(VehicleKit kit) => kit.Liveries.SelectMany(mod =>
        Files.Where(file => System.IO.Path.GetExtension(file).Equals(".yft", StringComparison.OrdinalIgnoreCase) &&
            System.IO.Path.GetFileNameWithoutExtension(file).Equals(mod.Element("modelName")?.Value, StringComparison.OrdinalIgnoreCase))
        .Select(file => new VehicleTemplate(file, mod))).ToArray();

    public static VehicleCatalog Scan(string root, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        root = System.IO.Path.GetFullPath(root);
        string resources = System.IO.Path.Combine(root, "resources");
        if (!Directory.Exists(resources)) throw new DirectoryNotFoundException("Select the BadlandsRP repository containing resources.");
        var warnings = new List<string>();
        var files = new List<string>();
        int lastProgress = -500;
        // Skip junctions so a resource cannot redirect installation or scanning outside the repository.
        var directories = new Stack<string>();
        directories.Push(resources);
        while (directories.TryPop(out string? directory))
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (string child in Directory.EnumerateDirectories(directory))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0 && !System.IO.Path.GetFileName(child).StartsWith('.')) directories.Push(child);
            files.AddRange(Directory.EnumerateFiles(directory));
            if (files.Count - lastProgress >= 500) { progress?.Report($"Indexing resources: {files.Count:N0} files"); lastProgress = files.Count; }
        }
        var documents = new List<(string Path, XDocument Xml)>();
        foreach (string file in files.Where(file => file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                XDocument document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
                if (document.Descendants("InitDatas").Any() || document.Descendants("variationData").Any() || document.Descendants("Kits").Any()) documents.Add((file, document));
            }
            catch (Exception ex) { warnings.Add($"{System.IO.Path.GetRelativePath(root, file)}: {ex.Message}"); }
        }
        string ResourcePath(string file)
        {
            var directory = new DirectoryInfo(System.IO.Path.GetDirectoryName(file)!);
            while (directory.FullName.StartsWith(resources + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "fxmanifest.lua")) || File.Exists(System.IO.Path.Combine(directory.FullName, "__resource.lua"))) return directory.FullName;
                directory = directory.Parent!;
            }
            return System.IO.Path.Combine(resources, System.IO.Path.GetRelativePath(resources, file).Split(System.IO.Path.DirectorySeparatorChar)[0]);
        }
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files.Where(file => file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) &&
            (file.Contains("names", StringComparison.OrdinalIgnoreCase) || file.Contains("text_entries", StringComparison.OrdinalIgnoreCase))))
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "AddTextEntry\\(\\s*['\"](?<key>[^'\"]+)['\"]\\s*,\\s*(?<q>['\"])(?<name>.*?)\\k<q>\\s*\\)"))
                names[match.Groups["key"].Value] = match.Groups["name"].Value;
        }
        var kits = documents.SelectMany(doc => doc.Xml.Descendants("Kits").Elements("Item")
            .Where(item => item.Element("kitName") != null).Select(item => new VehicleKit(doc.Path, item.Element("kitName")!.Value.Trim(), item))).ToArray();
        var definitions = documents.SelectMany(doc => doc.Xml.Descendants("Item")
            .Where(item => item.Parent?.Name.LocalName is "InitDatas" or "variationData")
            .Where(item => item.Element("modelName") != null)
            .Select(item => (Model: item.Element("modelName")!.Value.Trim(), doc.Path, Xml: item, Resource: ResourcePath(doc.Path)))).ToArray();
        var vehicles = new List<CatalogVehicle>();
        var modelFiles = files.Where(file => System.IO.Path.GetExtension(file).Equals(".yft", StringComparison.OrdinalIgnoreCase))
            .ToLookup(file => System.IO.Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase);
        var textureFiles = files.Where(file => System.IO.Path.GetExtension(file).Equals(".ytd", StringComparison.OrdinalIgnoreCase))
            .ToLookup(file => System.IO.Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase);
        var kitsByHash = kits.ToLookup(kit => Hash(kit.Name));
        foreach (var group in definitions.GroupBy(item => (item.Model.ToLowerInvariant(), item.Resource)))
        {
            cancellation.ThrowIfCancellationRequested();
            var first = group.First();
            var problems = new List<string>();
            var variations = group.Where(item => item.Xml.Parent?.Name.LocalName == "variationData").ToArray();
            if (variations.Length != 1) problems.Add(variations.Length == 0 ? "Missing carvariations" : "Multiple carvariations definitions; resolve load order");
            if (group.Count(item => item.Xml.Parent?.Name.LocalName == "InitDatas") > 1) problems.Add("Multiple vehicle definitions; resolve load order");
            var kitRefs = variations.SelectMany(item => item.Xml.Element("kits")?.Elements("Item") ?? []).Select(item => Hash(item.Value.Trim())).ToHashSet();
            var linked = kitRefs.SelectMany(hash => kitsByHash[hash]).ToArray();
            if (linked.GroupBy(kit => Hash(kit.Name)).Any(items => items.Count() > 1)) problems.Add("Duplicate modkit definitions; resolve load order");
            string[] models = modelFiles[first.Model].Concat(modelFiles[first.Model + "_hi"]).ToArray();
            string txd = group.Select(item => item.Xml.Element("txdName")?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? first.Model;
            string[] textures = textureFiles[txd].ToArray();
            string display = group.Select(item => item.Xml.Element("gameName")?.Value).FirstOrDefault(value => value != null) ?? first.Model;
            vehicles.Add(new CatalogVehicle(first.Model, names.GetValueOrDefault(display, display), System.IO.Path.GetFileName(first.Resource), first.Resource,
                group.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), models, textures, txd, linked, problems.ToArray()));
        }
        // Streamed models without metadata remain visible, but are never silently assigned another vehicle's kit.
        var knownModels = vehicles.Select(vehicle => vehicle.Model).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modModels = kits.SelectMany(kit => kit.Xml.Element("visibleMods")?.Elements("Item") ?? [])
            .Select(mod => mod.Element("modelName")?.Value).Where(value => value != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in modelFiles.SelectMany(group => group).Where(file => !file.EndsWith("_hi.yft", StringComparison.OrdinalIgnoreCase)))
        {
            cancellation.ThrowIfCancellationRequested();
            string model = System.IO.Path.GetFileNameWithoutExtension(file);
            if (knownModels.Contains(model) || modModels.Contains(model)) continue;
            knownModels.Add(model);
            string resource = ResourcePath(file);
            vehicles.Add(new CatalogVehicle(model, model, System.IO.Path.GetFileName(resource), resource, [], modelFiles[model].Concat(modelFiles[model + "_hi"]).ToArray(), [], model, [], ["Missing vehicle metadata / unknown model role"]));
        }
        string config = System.IO.Path.Combine(root, "server.cfg");
        string build = File.Exists(config) ? Regex.Match(File.ReadAllText(config), @"(?m)^\s*(?:set\s+)?sv_enforceGameBuild\s+[""']?(\d+)").Groups[1].Value : "";
        return new VehicleCatalog { Root = root, Files = files.ToArray(), Vehicles = vehicles.OrderBy(vehicle => vehicle.Model).ToArray(), Kits = kits, Warnings = warnings.ToArray(), GameBuild = build.Length == 0 ? "unknown (confirm server build)" : build };
    }

    private static uint Hash(string value) => uint.TryParse(value, out uint number) ? number :
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out number) ? number : JenkHash.GenHash(value.ToLowerInvariant());
}
