using System.Xml.Linq;

namespace Badlands.LiveryTool;

internal sealed record VehicleSetupTargets(string[] Carcols, string[] Variations);
internal sealed record VehicleSetupResult(LiveryPlan Plan, string Review) : IDisposable
{
    public void Dispose() => Plan.Dispose();
}

internal static class VehicleSetupWorkflow
{
    internal static VehicleSetupTargets Targets(VehicleCatalog catalog, CatalogVehicle vehicle)
    {
        string[] Paths(string section) => catalog.Files.Where(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
            path.StartsWith(vehicle.ResourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).Where(path =>
        {
            try { return XDocument.Load(path).Descendants(section).Any(); }
            catch (System.Xml.XmlException) { return false; }
        }).Where(path =>
        {
            try { ResourceMetadata.ValidateRegistration(ResourceMetadata.Manifest(catalog.Root, path), path); return true; }
            catch (InvalidDataException) { return false; }
        }).ToArray();
        string[] cols = Paths("Kits"), vars = Paths("variationData");
        string[] linkedCols = vehicle.Kits.Select(kit => kit.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] linkedVars = vars.Where(path => XDocument.Load(path).Descendants("variationData").Elements("Item").Any(item => item.Element("modelName")?.Value.Equals(vehicle.Model, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
        return new(linkedCols.Length == 1 ? linkedCols.Concat(cols.Except(linkedCols, StringComparer.OrdinalIgnoreCase)).ToArray() : cols,
            linkedVars.Concat(vars.Except(linkedVars, StringComparer.OrdinalIgnoreCase)).ToArray());
    }

    internal static string? Suggest(string[] candidates, string? linked = null, string? sibling = null)
    {
        if (linked != null && candidates.Contains(linked, StringComparer.OrdinalIgnoreCase)) return linked;
        if (sibling != null)
        {
            var paired = candidates.Where(path => Path.GetDirectoryName(path) == Path.GetDirectoryName(sibling)).ToArray();
            if (paired.Length == 1) return paired[0];
        }
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal static string? SuggestRegisteredCarcols(VehicleCatalog catalog, string[] candidates, string? masterList)
    {
        try
        {
            string registry = ModkitConversion.ResolveMasterList(catalog, masterList);
            var registered = File.ReadLines(registry).Select(line => line.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var scores = candidates.Select(path => (Path: path, Score: catalog.Kits.Count(kit => kit.Path == path && registered.Contains(kit.Name))))
                .OrderByDescending(item => item.Score).ToArray();
            // A shared destination is supported by the repository's own registry, not a filename or machine-specific path.
            return scores.Length > 0 && scores[0].Score > 1 && (scores.Length == 1 || scores[0].Score > scores[1].Score) ? scores[0].Path : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return null; }
    }

    internal static VehicleSetupResult Prepare(VehicleCatalog catalog, CatalogVehicle selected, string colsPath, string varsPath,
        string? masterList, VehicleMetadataSourceResult? source, bool dedicated, CancellationToken cancellation)
    {
        var plan = new LiveryPlan { Resources = Path.Combine(catalog.Root, "resources"), StreamBaseline = LiveryBatchWorkflow.StreamFiles(Path.Combine(catalog.Root, "resources")) };
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string> { "Vehicle setup: " + selected.Model, "Stock livery YFTs stay in GTA. Only new custom artwork will be exported later." };
        string Read(string path) => LiveryBatchWorkflow.PlanText(plan, catalog, texts, path);
        try
        {
            if (!catalog.Vehicles.Contains(selected)) throw new InvalidDataException("The selected vehicle changed. Scan again before setup.");
            foreach (string path in new[] { colsPath, varsPath }.Concat(selected.MetadataPaths).Concat(selected.Kits.Select(kit => kit.Path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                LiveryBatchWorkflow.EnsureInside(catalog.Root, path);
                string manifest = ResourceMetadata.Manifest(catalog.Root, path);
                ResourceMetadata.ValidateRegistration(manifest, path);
                plan.Inputs[manifest] = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(manifest));
                plan.Inputs[path] = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            }
            foreach (var group in selected.Kits.GroupBy(kit => kit.Name, StringComparer.OrdinalIgnoreCase))
            {
                string[] paths = group.Select(kit => kit.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (paths.Length > 1) throw new InvalidDataException("The modkit is defined in different files. Choose which resource owns it before setup: " + string.Join(", ", paths));
                string path = paths.Single();
                var matches = XDocument.Parse(Read(path), LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo).Descendants("Kits").Elements("Item").Where(item => item.Element("kitName")?.Value.Equals(group.Key, StringComparison.OrdinalIgnoreCase) == true).ToArray();
                if (matches.Length < 2) continue;
                XElement Clean(XElement item) => XElement.Parse(item.ToString(SaveOptions.DisableFormatting));
                if (matches.Skip(1).Any(item => !XNode.DeepEquals(Clean(matches[0]), Clean(item))))
                    throw new InvalidDataException("The duplicate modkit entries contain different settings. Setup will not choose one automatically: " + path);
                string text = Read(path);
                foreach (var span in matches.Skip(1).Select(item => ModkitConversion.ElementSpan(text, item)).OrderByDescending(span => span.Start))
                    text = text.Remove(span.Start, span.End - span.Start);
                texts[path] = text;
                notes.Add($"Remove {matches.Length - 1} identical duplicate(s) of {group.Key} from {Path.GetRelativePath(catalog.Root, path)}. Keep the first entry and all of its slots.");
            }
            string vars = Read(varsPath);
            XElement[] existingVars = XDocument.Parse(vars).Descendants("variationData").Elements("Item").Where(item => item.Element("modelName")?.Value.Equals(selected.Model, StringComparison.OrdinalIgnoreCase) == true).ToArray();
            if (existingVars.Length > 1) throw new InvalidDataException("Multiple carvariations entries exist for this vehicle. Their settings need to be resolved before setup.");
            if (existingVars.Length == 0)
            {
                if (source?.Variations.Count != 1) throw new InvalidDataException("The active GTA build has no unambiguous carvariations entry for " + selected.Model);
                texts[varsPath] = Append(vars, "variationData", XElement.Parse(source.Variations.Single().Xml));
                notes.Add("Add carvariations to " + Path.GetRelativePath(catalog.Root, varsPath));
            }
            var staged = VehicleCatalog.Scan(catalog.Root, cancellation, overrides: texts);
            CatalogVehicle vehicle = staged.Vehicles.Single(item => item.Key == selected.Key);
            string? importedKit = null;
            if (vehicle.Kits.Length == 0)
            {
                if (source?.Kits.Count != 1) throw new InvalidDataException("The active GTA build does not provide one matching modkit. A compatible modkit must be supplied for this vehicle.");
                XElement kit = XElement.Parse(source.Kits.Single().Xml);
                string name = kit.Element("kitName")!.Value;
                XElement variation = XDocument.Parse(Read(varsPath), LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo).Descendants("variationData").Elements("Item").Single(item => item.Element("modelName")?.Value.Equals(vehicle.Model, StringComparison.OrdinalIgnoreCase) == true);
                XElement[] references = variation.Element("kits")?.Elements("Item").ToArray() ?? [];
                if (references.Length != 1) throw new InvalidDataException("The missing modkit reference is ambiguous. Setup needs one selected vehicle kit reference.");
                if (!references[0].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    var referenceSpan = ModkitConversion.ElementSpan(Read(varsPath), references[0]);
                    var replacement = new XElement(references[0]); replacement.Value = name;
                    string current = Read(varsPath);
                    texts[varsPath] = current[..referenceSpan.Start] + replacement.ToString(SaveOptions.DisableFormatting) + current[referenceSpan.End..];
                    notes.Add("Repair the missing vehicle kit reference using active GTA metadata before conversion.");
                }
                if (!staged.Kits.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    texts[colsPath] = Append(Read(colsPath), "Kits", kit);
                    importedKit = name;
                    notes.Add("Create carcols metadata from the active stock modkit in " + Path.GetRelativePath(catalog.Root, colsPath));
                }
                staged = VehicleCatalog.Scan(catalog.Root, cancellation, overrides: texts);
                vehicle = staged.Vehicles.Single(item => item.Key == selected.Key);
            }
            if (vehicle.Problems.Length > 0) throw new InvalidDataException(vehicle.Status);
            if (vehicle.Kits.Length != 1) throw new InvalidDataException("This vehicle has several modkits. Select the intended kit before setup.");
            if (dedicated)
            {
                string master = ModkitConversion.ResolveMasterList(staged, masterList);
                plan.SelectedMasterLists.Add(master);
                VehicleKit converted = ModkitConversion.Apply(staged, new(vehicle, vehicle.Kits.Single()), master, Read, (path, text) => texts[path] = text, plan.Modkits);
                if (importedKit != null && !converted.Name.Equals(importedKit, StringComparison.OrdinalIgnoreCase))
                {
                    string current = Read(colsPath);
                    XElement temporarySource = XDocument.Parse(current, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo).Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")?.Value == importedKit);
                    var sourceSpan = ModkitConversion.ElementSpan(current, temporarySource);
                    texts[colsPath] = current.Remove(sourceSpan.Start, sourceSpan.End - sourceSpan.Start);
                    notes.Add("Store only the new dedicated modkit; the original stock kit remains supplied by GTA.");
                }
                notes.Add($"Use modkit {converted.Name}, carcols ID {converted.Xml.Element("id")?.Attribute("value")?.Value}. Update carcols, carvariations and the master list together when needed.");
            }
            LiveryBatchWorkflow.StageTexts(plan, texts);
            notes.Add(plan.StagedFiles.Count == 0 ? "Metadata is already ready. No files need changing." : "Files to update:\r\n" + string.Join("\r\n", plan.StagedFiles.Keys.Select(path => "  " + Path.GetRelativePath(catalog.Root, path))));
            return new(plan, string.Join("\r\n\r\n", notes));
        }
        catch { plan.Dispose(); throw; }
    }

    private static string Append(string text, string section, XElement item)
    {
        XElement parent = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo).Descendants(section).Single();
        var span = ModkitConversion.ElementSpan(text, parent);
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        int startLine = text.LastIndexOf('\n', span.Start) + 1;
        string indent = text[startLine..span.Start];
        if (!indent.All(char.IsWhiteSpace)) indent = "  ";
        string fragment = string.Join(newline, XElement.Parse(item.ToString()).ToString().Replace("\r\n", "\n").Split('\n').Select(line => indent + "  " + line));
        string raw = text[span.Start..span.End];
        if (raw.TrimEnd().EndsWith("/>", StringComparison.Ordinal))
            return text[..span.Start] + "<" + section + ">" + newline + fragment + newline + indent + "</" + section + ">" + text[span.End..];
        int close = text.LastIndexOf("</" + section, span.End - 1, span.End - span.Start, StringComparison.Ordinal);
        int lineStart = text.LastIndexOf('\n', close) + 1;
        if (!text[lineStart..close].All(char.IsWhiteSpace)) lineStart = close;
        return text.Insert(lineStart, (lineStart == close ? newline : "") + fragment + newline);
    }
}
