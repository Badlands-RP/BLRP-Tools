using System.Text.RegularExpressions;

namespace Badlands.LiveryTool;

internal static class ResourceMetadata
{
    public static string Manifest(string root, string path)
    {
        for (DirectoryInfo? directory = new(Path.GetDirectoryName(path)!); directory != null && directory.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase); directory = directory.Parent)
            foreach (string filename in new[] { "fxmanifest.lua", "__resource.lua" })
            {
                string manifest = Path.Combine(directory.FullName, filename);
                if (File.Exists(manifest)) return manifest;
            }
        throw new InvalidDataException("Missing resource manifest for " + path);
    }

    public static void ValidateRegistration(string manifest, string path)
    {
        var metadata = System.Xml.Linq.XDocument.Load(path);
        string kind = metadata.Descendants("Kits").Any() ? "CARCOLS_FILE" :
            metadata.Descendants("variationData").Any() ? "VEHICLE_VARIATION_FILE" :
            metadata.Descendants("InitDatas").Any() ? "VEHICLE_METADATA_FILE" : throw new InvalidDataException("Unknown vehicle metadata role: " + path);
        string relative = Path.GetRelativePath(Path.GetDirectoryName(manifest)!, path).Replace('\\', '/');
        var tokens = LuaLiveryEditor.Tokens(File.ReadAllText(manifest));
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Value != "data_file") continue;
            int value = tokens[i + 1].Value == "(" ? i + 2 : i + 1;
            if (value + 1 >= tokens.Count || tokens[value].Value.Trim('\'', '"') != kind) continue;
            int patternIndex = tokens[value + 1].Value == "," ? value + 2 : value + 1;
            if (patternIndex >= tokens.Count) continue;
            string pattern = tokens[patternIndex].Value.Trim('\'', '"').Replace('\\', '/');
            string expression = "^" + Regex.Escape(pattern).Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$";
            if (Regex.IsMatch(relative, expression, RegexOptions.IgnoreCase)) return;
        }
        throw new InvalidDataException($"{relative} is not registered as {kind} in {manifest}. Add explicit registration before installing.");
    }
}
