namespace Badlands.LiveryTool;

internal static class LiveryRouting
{
    // Use the existing modkit's streamed assets, including when they live in a shared livery resource.
    public static string? ExportFolder(VehicleCatalog catalog, VehicleKit kit)
    {
        string[] folders = catalog.Templates(kit).Select(template => Path.GetDirectoryName(template.Path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (folders.Length != 1) return null;
        try
        {
            string manifest = ResourceMetadata.Manifest(catalog.Root, folders[0] + Path.DirectorySeparatorChar + "asset.yft");
            string relative = Path.GetRelativePath(Path.GetDirectoryName(manifest)!, folders[0]);
            return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0].Equals("stream", StringComparison.OrdinalIgnoreCase)
                ? folders[0] : null;
        }
        catch (InvalidDataException) { return null; }
    }

    public static string[] AccessFiles(VehicleCatalog catalog, CancellationToken cancellation)
    {
        var matches = new List<string>();
        foreach (string file in catalog.Files.Where(file => file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
        {
            cancellation.ThrowIfCancellationRequested();
            string text = File.ReadAllText(file);
            if (!text.Contains("custom_liveries", StringComparison.Ordinal)) continue;
            // Ignore references, comments and strings. Multiple real assignments need an explicit choice.
            try
            {
                var tokens = LuaLiveryEditor.Tokens(text);
                if (Enumerable.Range(0, Math.Max(0, tokens.Count - 2)).Any(i =>
                    tokens[i].Value == "custom_liveries" && tokens[i + 1].Value == "=" && tokens[i + 2].Value == "{")) matches.Add(file);
            }
            catch (InvalidDataException)
            {
                // Keep a likely broken configuration visible so it cannot be silently bypassed.
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bcustom_liveries\s*=\s*\{")) matches.Add(file);
            }
        }
        return matches.ToArray();
    }

    public static string AccessFile(VehicleCatalog catalog, string? selected, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(selected))
        {
            string[] matches = AccessFiles(catalog, cancellation);
            if (matches.Length != 1) throw new InvalidDataException("The livery access/display-name Lua file could not be identified uniquely. Select it in Advanced.");
            selected = matches[0];
        }
        string path = Path.GetFullPath(selected);
        string resources = Path.GetFullPath(Path.Combine(catalog.Root, "resources")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(resources, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || !path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose an existing livery Lua file inside this repository's resources folder.");
        LuaLiveryEditor.Slots(File.ReadAllText(path), "__blrp_validation__");
        return path;
    }
}
