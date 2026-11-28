using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Badlands.LiveryTool;

internal sealed record LiveryImport(CatalogVehicle Vehicle, VehicleKit Kit, VehicleTemplate Template,
    string Texture, string Image, string DisplayName, string AccessGroup, string Permission, bool FitImage,
    string? OutputName = null, string? Label = null, string? ExportFolder = null, bool ConvertModkit = false, ExistingLivery? Replacement = null, bool AllowSharedModkit = false);

internal sealed class LiveryBatchException(IReadOnlyList<LiveryImport> imports, Exception cause)
    : IOException(string.Join(", ", imports.Select(import => import.Vehicle.Model + " / " + Path.GetFileName(import.Image))) + ": " + cause.Message, cause)
{
    public IReadOnlyList<LiveryImport> Imports { get; } = imports;
}

internal sealed record PlannedLivery(LiveryImport Import, string Name, string Label, int Slot, string Destination, string StagedAsset, string[]? OtherUses = null, string[]? SharedVehicles = null);

internal sealed class LiveryPlan : IDisposable
{
    public string Staging { get; } = Path.Combine(Path.GetTempPath(), "BLRP-Livery-Plan-" + Guid.NewGuid().ToString("N"));
    public List<PlannedLivery> Items { get; } = [];
    public List<PlannedModkit> Modkits { get; } = [];
    public Dictionary<string, byte[]?> Originals { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> StagedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> StagedHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> ExportFolders { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> SelectedMasterLists { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal string[] StreamBaseline { get; set; } = [];
    internal string Resources { get; set; } = "";
    public bool Installed { get; internal set; }
    public LiveryPlan() => Directory.CreateDirectory(Staging);
    public void Dispose() { if (Directory.Exists(Staging)) Directory.Delete(Staging, true); }

    public string Review() => string.Join(Environment.NewLine + Environment.NewLine, Items.Select(item =>
        $"{item.Import.Vehicle.Model} — {item.Import.DisplayName}\r\n  {(item.Import.Replacement != null ? "Replace YFT" : "Export")}: {item.Destination}\r\n  Access: {(item.Import.Replacement != null ? "Unchanged (Lua is not edited)" : item.Import.AccessGroup.Length == 0 ? "Public" : item.Import.AccessGroup + (item.Import.Permission.Length == 0 ? "" : ":" + item.Import.Permission))}\r\n  Livery slot: {item.Slot}" +
            (item.OtherUses?.Length > 0 ? "\r\n  This shared YFT also updates: " + string.Join(", ", item.OtherUses) : "") +
            (item.SharedVehicles?.Length > 1 ? "\r\n  Shared modkit — adds this slot and access rules for: " + string.Join(", ", item.SharedVehicles) : ""))) +
        Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, Modkits.Select(kit =>
            $"{kit.Vehicle} modkit: {kit.OriginalName} → {kit.Name} (ID {kit.Id})\r\n  Master list: {kit.MasterList}")) +
        Environment.NewLine + Environment.NewLine + "Metadata updates:" + Environment.NewLine +
        (StagedFiles.Keys.Any(path => !Items.Any(item => item.Destination == path)) ? string.Join(Environment.NewLine, StagedFiles.Keys.Where(path => !Items.Any(item => item.Destination == path))) : "None");
}

internal sealed class LiveryBatchWorkflow
{
    public LiveryPlan Prepare(VehicleCatalog catalog, IReadOnlyList<LiveryImport> imports, CancellationToken cancellation, IProgress<string>? progress = null, string? accessFile = null, string? masterList = null)
    {
        if (imports.Count == 0) throw new InvalidOperationException("Add images to the queue first.");
        var plan = new LiveryPlan { Resources = Path.Combine(catalog.Root, "resources") };
        try
        {
            plan.StreamBaseline = StreamFiles(plan.Resources);
            LiveryImport[] additions = imports.Where(import => import.Replacement == null).ToArray();
            string? lua = additions.Length > 0 ? LiveryRouting.AccessFile(catalog, accessFile, cancellation) : null;
            var textChanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Text(string path) => PlanText(plan, catalog, textChanges, path);
            foreach (var group in additions.GroupBy(import => import.Vehicle.Key))
                if (group.Select(import => import.ConvertModkit).Distinct().Count() > 1 || group.Select(import => import.Kit.Name).Distinct().Count() > 1)
                    throw new LiveryBatchException(group.ToArray(), new InvalidDataException("Use the same modkit and conversion setting for every queued livery of " + group.First().Vehicle.Model));
            Dictionary<string, VehicleKit> conversions;
            try
            {
                conversions = ApplyConversions(plan, catalog, additions.Where(import => import.ConvertModkit)
                    .GroupBy(import => import.Vehicle.Key).Select(group => new ModkitConversionRequest(group.First().Vehicle, group.First().Kit)).ToArray(),
                    masterList, textChanges, cancellation);
            }
            catch (ModkitConversionException ex) { throw new LiveryBatchException(additions.Where(import => import.Vehicle.Key == ex.VehicleKey).ToArray(), ex); }
            string luaText = lua != null ? Text(lua) : "";
            LuaLiveryEditor.Tokens(luaText);
            var occupiedNames = plan.StreamBaseline.Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Labels have global scope, including labels in unrelated resources.
            var occupiedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in additions.Length == 0 ? [] : catalog.Files.Where(file => file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
            {
                cancellation.ThrowIfCancellationRequested();
                string text = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(text, "<modShopLabel>\\s*([^<]+)\\s*</modShopLabel>|AddTextEntry\\(\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase))
                    occupiedLabels.Add(match.Groups[1].Success ? match.Groups[1].Value.Trim() : match.Groups[2].Value);
            }
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var replacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LiveryImport import in imports)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (import.Vehicle.Problems.Length > 0) throw new InvalidDataException($"{import.Vehicle.Model}: {import.Vehicle.Status}");
                    if (!catalog.Vehicles.Contains(import.Vehicle)) throw new InvalidDataException($"{import.Vehicle.Model}: the vehicle definition changed. Review this queued vehicle again.");
                    if (!import.Vehicle.Kits.Contains(import.Kit)) throw new InvalidDataException($"{import.Vehicle.Model}: modkit '{import.Kit.Name}' does not belong to this vehicle. Select its modkit in Advanced and review again.");
                    if (import.Replacement is ExistingLivery replacement)
                    {
                        ExistingLiveries.Validate(catalog, import.Vehicle, replacement);
                        if (!replacement.CanReplace) throw new InvalidDataException("Choose one existing repository YFT to replace. Game-archive assets and ambiguous filenames cannot be overwritten.");
                        if (import.ConvertModkit) throw new InvalidDataException("A YFT-only replacement cannot convert the modkit.");
                        string target = replacement.Paths.Single();
                        EnsureInside(catalog.Root, target);
                        if (import.Kit.Path != replacement.Kit.Path || import.Kit.Name != replacement.Kit.Name || import.Template.Path != target || !XNode.DeepEquals(import.Template.Mod, replacement.Mod))
                            throw new InvalidDataException("The replacement template must be the selected existing livery itself.");
                        if (!replacements.Add(target)) throw new InvalidDataException("Two queued replacements target the same YFT: " + target);
                        if (!File.Exists(target)) throw new FileNotFoundException("The YFT selected for replacement no longer exists.", target);
                        if (!XNode.DeepEquals(FindKit(File.ReadAllText(import.Kit.Path), import.Kit.Name), import.Kit.Xml))
                            throw new InvalidDataException("Modkit changed since scanning. Scan and select the existing livery again.");
                        foreach (string input in import.Vehicle.MetadataPaths.Append(import.Kit.Path).Append(target).Append(import.Image).Distinct(StringComparer.OrdinalIgnoreCase))
                            plan.Inputs[input] = SHA256.HashData(File.ReadAllBytes(input));
                        string targetFolder = Path.GetDirectoryName(target)!;
                        EnsureNoLinks(targetFolder);
                        plan.ExportFolders.Add(targetFolder);
                        string stagedReplacement = Path.Combine(plan.Staging, Path.GetFileName(target));
                        plan.Items.Add(new PlannedLivery(import, replacement.Model, replacement.Label, replacement.Slot, target, stagedReplacement, ExistingLiveries.OtherUses(catalog, replacement)));
                        plan.Originals[target] = File.ReadAllBytes(target);
                        plan.StagedFiles[target] = stagedReplacement;
                        continue;
                    }
                    VehicleKit activeKit = conversions.GetValueOrDefault(import.Vehicle.Key) ?? import.Kit;
                    CatalogVehicle[] affected = conversions.ContainsKey(import.Vehicle.Key) ? [import.Vehicle] : SharedVehicles(catalog, import.Kit);
                    if (affected.Length > 1 && !import.AllowSharedModkit)
                        throw new InvalidDataException("This modkit is shared with " + string.Join(", ", affected.Select(vehicle => vehicle.Model)) + ". Enable the shared-modkit override in Advanced to extend it for all of them, or convert to a dedicated kit.");
                    if (affected.Any(vehicle => vehicle.Problems.Length > 0 || catalog.Vehicles.Count(other => other.Model.Equals(vehicle.Model, StringComparison.OrdinalIgnoreCase)) != 1))
                        throw new InvalidDataException("Resolve conflicting metadata for shared-modkit vehicles: " + string.Join(", ", affected.Where(vehicle => vehicle.Problems.Length > 0 || catalog.Vehicles.Count(other => other.Model.Equals(vehicle.Model, StringComparison.OrdinalIgnoreCase)) != 1).Select(vehicle => vehicle.Model)));
                    if (catalog.Vehicles.Count(vehicle => vehicle.Model.Equals(import.Vehicle.Model, StringComparison.OrdinalIgnoreCase)) != 1)
                        throw new InvalidDataException($"{import.Vehicle.Model} has multiple resource definitions. Resolve repository load order before importing.");
                    if (!GameLiveryTemplates.Matches(catalog, import.Kit, import.Template))
                        throw new InvalidDataException("Template is not a livery mod referenced by the selected vehicle modkit.");
                    if (plan.StreamBaseline.Count(path => Path.GetFileName(path).Equals(Path.GetFileName(import.Template.Path), StringComparison.OrdinalIgnoreCase)) != (import.Template.GameSource == null ? 1 : 0))
                        throw new InvalidDataException("Template has duplicate streamed filenames across resources. Resolve them first.");
                    if (import.Vehicle.ModelPaths.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                        throw new InvalidDataException("Vehicle has duplicate streamed model names across resources. Resolve them first.");
                    foreach (string metadata in affected.SelectMany(vehicle => vehicle.MetadataPaths).Append(import.Kit.Path).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        string manifest = ResourceMetadata.Manifest(catalog.Root, metadata);
                        ResourceMetadata.ValidateRegistration(manifest, metadata);
                        plan.Inputs[manifest] = SHA256.HashData(File.ReadAllBytes(manifest));
                        plan.Inputs[metadata] = SHA256.HashData(File.ReadAllBytes(metadata));
                    }
                    if (string.IsNullOrWhiteSpace(import.DisplayName) || import.DisplayName.Any(char.IsControl)) throw new InvalidDataException("Enter a livery display name without control characters.");
                    if (import.AccessGroup.Any(char.IsControl) || import.Permission.Any(char.IsControl) || (import.AccessGroup.Length == 0 && import.Permission.Length > 0))
                        throw new InvalidDataException("Permission requires an access group. Leave both empty for public access.");
                    if (import.AccessGroup.Contains(':') || import.Permission.Contains(':')) throw new InvalidDataException("Enter one access group and one permission separately, without colons.");
                    foreach (string input in import.Vehicle.MetadataPaths.Append(import.Template.Path).Append(import.Image).Append(import.Kit.Path))
                        plan.Inputs[input] = SHA256.HashData(File.ReadAllBytes(input));
                    string kitKey = activeKit.Path + "|" + activeKit.Name;
                    if (!counts.TryGetValue(kitKey, out int slot))
                    {
                        string current = Text(activeKit.Path);
                        XElement kit = FindKit(current, activeKit.Name);
                        if (!XNode.DeepEquals(kit, activeKit.Xml)) throw new InvalidDataException("Modkit changed since scanning. Scan again.");
                        slot = kit.Element("visibleMods")?.Elements("Item").Count(item => item.Element("type")?.Value == "VMT_LIVERY_MOD") ?? 0;
                    }
                    counts[kitKey] = slot + 1;
                    foreach (var vehicle in affected)
                        if (LuaLiveryEditor.Slots(luaText, vehicle.Model).Contains(slot)) throw new InvalidDataException($"{vehicle.Model} slot {slot} already has a restriction. Resolve stale slot metadata first.");
                    var suggested = LiveryNaming.Suggest(import.Kit, import.Template, import.Vehicle.Model, occupiedNames, occupiedLabels);
                    string name = import.OutputName?.Trim() is { Length: > 0 } customName ? customName : suggested.Name;
                    string label = import.Label?.Trim() is { Length: > 0 } customLabel ? customLabel :
                        string.IsNullOrWhiteSpace(import.OutputName) ? suggested.Label : name.ToUpperInvariant();
                    if (!Regex.IsMatch(name, @"^[a-zA-Z0-9_]{1,60}$") || !Regex.IsMatch(label, @"^[a-zA-Z0-9_]{1,63}$")) throw new InvalidDataException("Asset names/labels must be ASCII letters, digits or underscores (max 60/63).");
                    if (!occupiedNames.Add(name)) throw new InvalidDataException($"Duplicate streamed asset name: {name}");
                    if (!occupiedLabels.Add(label)) throw new InvalidDataException($"Duplicate text label: {label}");
                    string folder = string.IsNullOrWhiteSpace(import.ExportFolder)
                        ? LiveryRouting.ExportFolder(catalog, import.Kit) ?? throw new InvalidDataException("Multiple or unknown livery stream folders. Choose an export folder for " + import.DisplayName)
                        : Path.GetFullPath(import.ExportFolder);
                    if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing export folder for " + import.DisplayName);
                    EnsureNoLinks(folder);
                    plan.ExportFolders.Add(folder);
                    string destination = Path.Combine(folder, name + ".yft");
                    if (File.Exists(destination)) throw new IOException($"Output already exists: {destination}");
                    var mod = new XElement(import.Template.Mod);
                    mod.Element("modelName")!.Value = name;
                    mod.Element("modShopLabel")!.Value = label;
                    if (mod.Element("linkedModels")?.Elements().Any() == true)
                        throw new InvalidDataException("This template has linked models. It needs a dedicated compatibility review before cloning.");
                    textChanges[activeKit.Path] = AppendMod(Text(activeKit.Path), activeKit.Name, mod);
                    string[] relatedLabels = import.Kit.Liveries.Select(item => item.Element("modShopLabel")?.Value ?? "")
                        .Concat(plan.Items.Where(item => item.Import.Vehicle.Key == import.Vehicle.Key).Select(item => item.Label)).ToArray();
                    luaText = LuaLiveryEditor.Append(luaText, import.Vehicle.Model, slot, label, import.DisplayName, import.AccessGroup, import.Permission, relatedLabels);
                    foreach (var vehicle in affected.Where(vehicle => vehicle.Key != import.Vehicle.Key))
                        luaText = LuaLiveryEditor.AppendAccess(luaText, vehicle.Model, slot, import.AccessGroup, import.Permission);
                    string staged = Path.Combine(plan.Staging, name + ".yft");
                    plan.Items.Add(new PlannedLivery(import, name, label, slot, destination, staged, SharedVehicles: affected.Select(vehicle => vehicle.Model).ToArray()));
                    plan.Originals[destination] = null;
                    plan.StagedFiles[destination] = staged;
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not LiveryBatchException) { throw new LiveryBatchException([import], ex); }
            }
            if (lua != null) textChanges[lua] = luaText;
            // All metadata/slot/name checks above complete before the expensive conversions begin.
            for (int i = 0; i < plan.Items.Count; i++)
            {
                PlannedLivery item = plan.Items[i];
                progress?.Report($"Building and reloading {i + 1}/{plan.Items.Count}: {item.Name}");
                try { LiveryAssetBuilder.Build(item.Import.Template.Path, item.Import.Texture, item.Import.Image, item.Name, item.StagedAsset, item.Import.FitImage, cancellation, preserveIdentities: item.Import.Replacement != null); }
                catch (Exception ex) when (ex is not OperationCanceledException) { throw new LiveryBatchException([item.Import], ex); }
            }
            StageTexts(plan, textChanges);
            return plan;
        }
        catch { plan.Dispose(); throw; }
    }

    internal static CatalogVehicle[] SharedVehicles(VehicleCatalog catalog, VehicleKit kit) => catalog.Vehicles
        .Where(vehicle => vehicle.Kits.Any(candidate => candidate.Path.Equals(kit.Path, StringComparison.OrdinalIgnoreCase) && candidate.Name.Equals(kit.Name, StringComparison.OrdinalIgnoreCase))).ToArray();

    public LiveryPlan PrepareModkits(VehicleCatalog catalog, IReadOnlyList<ModkitConversionRequest> requests, string? masterList, CancellationToken cancellation)
    {
        if (requests.Count == 0) throw new InvalidOperationException("Select a vehicle with a metadata-linked modkit.");
        var plan = new LiveryPlan { Resources = Path.Combine(catalog.Root, "resources") };
        try
        {
            plan.StreamBaseline = StreamFiles(plan.Resources);
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ApplyConversions(plan, catalog, requests, masterList, texts, cancellation);
            StageTexts(plan, texts);
            return plan;
        }
        catch { plan.Dispose(); throw; }
    }

    private static Dictionary<string, VehicleKit> ApplyConversions(LiveryPlan plan, VehicleCatalog catalog, IReadOnlyList<ModkitConversionRequest> requests,
        string? selected, Dictionary<string, string> texts, CancellationToken cancellation)
    {
        var converted = new Dictionary<string, VehicleKit>(StringComparer.OrdinalIgnoreCase);
        if (requests.Count == 0) return converted;
        string master = ModkitConversion.ResolveMasterList(catalog, selected);
        plan.SelectedMasterLists.Add(master);
        foreach (var request in requests)
        {
            cancellation.ThrowIfCancellationRequested();
            if (converted.ContainsKey(request.Vehicle.Key)) throw new InvalidDataException("Choose one modkit conversion per vehicle.");
            try
            {
                foreach (string metadata in request.Vehicle.MetadataPaths.Append(request.Kit.Path).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string manifest = ResourceMetadata.Manifest(catalog.Root, metadata);
                    plan.Inputs[manifest] = SHA256.HashData(File.ReadAllBytes(manifest));
                }
                converted[request.Vehicle.Key] = ModkitConversion.Apply(catalog, request, master,
                    path => PlanText(plan, catalog, texts, path), (path, text) => texts[path] = text, plan.Modkits);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new ModkitConversionException(request.Vehicle.Key, ex); }
        }
        return converted;
    }

    internal static string PlanText(LiveryPlan plan, VehicleCatalog catalog, Dictionary<string, string> texts, string path)
    {
        if (!plan.SelectedMasterLists.Contains(path)) EnsureInside(catalog.Root, path);
        if (!texts.TryGetValue(path, out string? text))
        {
            plan.Originals[path] = File.ReadAllBytes(path);
            text = ReadText(plan.Originals[path]!);
            texts[path] = text;
        }
        return text;
    }

    internal static void StageTexts(LiveryPlan plan, Dictionary<string, string> texts)
    {
        int index = 0;
        foreach (var change in texts)
        {
            if (change.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) XDocument.Parse(change.Value);
            else if (change.Key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) LuaLiveryEditor.Tokens(change.Value);
            if (EncodeText(change.Value, plan.Originals[change.Key]!).SequenceEqual(plan.Originals[change.Key]!)) continue;
            string staged = Path.Combine(plan.Staging, $"metadata-{index++}");
            File.WriteAllBytes(staged, EncodeText(change.Value, plan.Originals[change.Key]!));
            plan.StagedFiles[change.Key] = staged;
        }
        foreach (string file in plan.StagedFiles.Values) plan.StagedHashes[file] = SHA256.HashData(File.ReadAllBytes(file));
        ValidateUnchanged(plan);
    }

    public void Install(LiveryPlan plan, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        if (plan.Installed) throw new InvalidOperationException("This review has already been exported.");
        ValidateUnchanged(plan);
        var written = new List<string>();
        var directories = new List<string>();
        var pending = new List<string>();
        try
        {
            foreach (var file in plan.StagedFiles)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!SHA256.HashData(File.ReadAllBytes(file.Value)).SequenceEqual(plan.StagedHashes[file.Value])) throw new IOException("Staged file changed after validation.");
                string directory = Path.GetDirectoryName(file.Key)!;
                if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); directories.Add(directory); }
                string temporary = file.Key + ".blrp-" + Guid.NewGuid().ToString("N") + ".tmp";
                pending.Add(temporary);
                File.Copy(file.Value, temporary, false);
            }
            ValidateUnchanged(plan);
            int index = 0;
            foreach (string target in plan.StagedFiles.Keys)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report($"Exporting {index + 1}/{pending.Count}: {Path.GetFileName(target)}");
                File.Move(pending[index++], target, overwrite: plan.Originals[target] != null);
                written.Add(target);
                if (!SHA256.HashData(File.ReadAllBytes(target)).SequenceEqual(plan.StagedHashes[plan.StagedFiles[target]])) throw new IOException("Installed bytes failed verification.");
            }
            cancellation.ThrowIfCancellationRequested();
            plan.Installed = true;
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<string>();
            foreach (string target in written.AsEnumerable().Reverse())
            {
                try
                {
                    if (plan.Originals[target] is byte[] original) File.WriteAllBytes(target, original);
                    else File.Delete(target);
                }
                catch (Exception ex) { rollbackErrors.Add(target + ": " + ex.Message); }
            }
            if (rollbackErrors.Count > 0) throw new IOException("Export failed and rollback could not restore: " + string.Join("; ", rollbackErrors), failure);
            throw;
        }
        finally
        {
            foreach (string path in pending) if (File.Exists(path)) File.Delete(path);
            foreach (string directory in directories.AsEnumerable().Reverse()) if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }

    internal static void ValidateUnchanged(LiveryPlan plan)
    {
        if (!StreamFiles(plan.Resources).SequenceEqual(plan.StreamBaseline, StringComparer.OrdinalIgnoreCase)) throw new IOException("Streamed files changed since review. Scan and review again.");
        foreach (var file in plan.Originals)
        {
            bool asset = plan.Items.Any(item => item.Destination.Equals(file.Key, StringComparison.OrdinalIgnoreCase));
            if (asset)
            {
                string folder = Path.GetDirectoryName(file.Key)!;
                if (!plan.ExportFolders.Contains(folder) || !Directory.Exists(folder)) throw new IOException("Selected export folder changed or disappeared.");
                EnsureNoLinks(folder);
            }
            else if (plan.SelectedMasterLists.Contains(file.Key))
            {
                if ((File.GetAttributes(file.Key) & FileAttributes.ReparsePoint) != 0) throw new IOException("The selected master list became a symbolic link.");
                EnsureNoLinks(Path.GetDirectoryName(file.Key)!);
            }
            else EnsureInside(Path.GetDirectoryName(plan.Resources)!, file.Key);
            if (file.Value == null ? File.Exists(file.Key) : !File.Exists(file.Key) || !File.ReadAllBytes(file.Key).SequenceEqual(file.Value))
                throw new IOException("Repository changed since review: " + file.Key);
        }
        foreach (var input in plan.Inputs)
            if (!File.Exists(input.Key) || !SHA256.HashData(File.ReadAllBytes(input.Key)).SequenceEqual(input.Value)) throw new IOException("Input changed since review: " + input.Key);
    }

    internal static string[] StreamFiles(string resources) => Directory.EnumerateFiles(resources, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
        .Where(file => file.Split(Path.DirectorySeparatorChar).Contains("stream", StringComparer.OrdinalIgnoreCase) && new[] { ".yft", ".ytd", ".ydr", ".ydd" }.Contains(Path.GetExtension(file).ToLowerInvariant()))
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static void EnsureInside(string root, string path)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        path = Path.GetFullPath(path);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Destination is outside the selected repository.");
        for (string? directory = Path.GetDirectoryName(path); directory != null && directory.Length >= root.Length; directory = Path.GetDirectoryName(directory))
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to write through a directory junction: " + directory);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to replace a symbolic link: " + path);
    }

    private static void EnsureNoLinks(string folder)
    {
        // The explicitly chosen folder is the output boundary. Do not require access
        // to unrelated parent directories (for example the user's AppData root).
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Choose an export folder without symbolic links or directory junctions: " + folder);
    }

    private static XElement FindKit(string text, string name) => XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo)
        .Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")?.Value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase) == true);

    private static string AppendMod(string text, string name, XElement mod)
    {
        XElement kit = FindKit(text, name);
        XElement visible = kit.Element("visibleMods") ?? throw new InvalidDataException("Modkit has no visibleMods. Additional setup required.");
        IXmlLineInfo info = (IXmlLineInfo)visible;
        int start = 0;
        for (int line = 1; line < info.LineNumber; line++) start = text.IndexOf('\n', start) + 1;
        start += info.LinePosition - 1;
        int close = text.IndexOf("</visibleMods>", start, StringComparison.Ordinal);
        if (close < 0 || text.IndexOf("/>", start, text.IndexOf('>', start) - start + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("An empty/self-closing visibleMods needs a compatible existing vehicle livery template first.");
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        int lineStart = text.LastIndexOf('\n', close) + 1;
        string indent = text[lineStart..close];
        if (!indent.All(char.IsWhiteSpace)) { lineStart = close; indent = "      "; }
        string formatted = new XElement(mod).ToString(SaveOptions.None).Replace("\r\n", "\n");
        // Drop source whitespace inside the cloned item; the surrounding document stays byte-for-byte intact.
        XElement clean = XElement.Parse(formatted);
        formatted = clean.ToString().Replace("\r\n", "\n");
        string insertion = string.Join(newline, formatted.Split('\n').Select(line => indent + "  " + line)) + newline;
        string result = text.Insert(lineStart, insertion);
        XDocument.Parse(result);
        return result;
    }

    private static string ReadText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static byte[] EncodeText(string text, byte[] original)
    {
        Encoding encoding = original.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }) ? new UnicodeEncoding(false, true) :
            original.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }) ? new UnicodeEncoding(true, true) :
            new UTF8Encoding(original.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
    }
}
