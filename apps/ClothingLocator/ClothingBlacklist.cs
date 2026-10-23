using System.Text;
using System.Text.RegularExpressions;

namespace BLRP.ClothingLocator;

internal static class ClothingBlacklist
{
    public static IReadOnlyDictionary<string, string> ReadPreviewRestrictions(string directory)
    {
        var restrictions = new Dictionary<string, string>();
        const string literal = @"'(?<text>(?:\\.|[^'])*)'|""(?<double>(?:\\.|[^""])*)""|(?<boolean>true|false)";
        foreach (Gender gender in Enum.GetValues<Gender>())
        {
            string model = gender == Gender.Male ? "mp_m_freemode_01" : "mp_f_freemode_01";
            string path = Path.Combine(directory, model + ".lua");
            string text = File.ReadAllText(path);
            text = Regex.Replace(text, @"--\[(=*)\[.*?\]\1\]", string.Empty, RegexOptions.Singleline);
            string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            int start = FindTableStart(lines), end = FindBlockEnd(lines, start);
            if (lines.Skip(end + 1).Any(line => !string.IsNullOrWhiteSpace(line.Split("--", 2)[0])))
                throw new InvalidDataException("Blacklist code after the literal table is not supported: " + path);
            string? category = null;
            int? drawable = null;
            for (int index = start + 1; index < end; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("--", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(line, @"^\s*sex\s*=\s*'(male|female)'\s*,?\s*(--.*)?$")) continue;
                Match categoryMatch = Regex.Match(line, @"^\s{2}\['(?<key>\d+|p\d+)'\]\s*=\s*\{\s*(--.*)?$");
                if (categoryMatch.Success)
                {
                    category = categoryMatch.Groups["key"].Value;
                    continue;
                }
                if (Regex.IsMatch(line, @"^\s{2}\},?\s*(--.*)?$")) { category = null; continue; }
                if (Regex.IsMatch(line, @"^\s{4}\},?\s*(--.*)?$")) { drawable = null; continue; }
                Match table = Regex.Match(line, @"^\s{4}\[(?<id>\d+)\]\s*=\s*\{\s*(--.*)?$");
                if (category != null && table.Success)
                {
                    drawable = int.Parse(table.Groups["id"].Value);
                    continue;
                }
                Match rule = Regex.Match(line, @"^(?<space>\s{4}|\s{6})\[(?<id>\d+)\]\s*=\s*(?:" + literal + @")\s*,?\s*(--.*)?$");
                if (category == null || !rule.Success)
                    throw new InvalidDataException($"Unsupported blacklist entry at {Path.GetFileName(path)}:{index + 1}; export stopped.");
                bool textureRule = rule.Groups["space"].Value.Length == 6;
                if (textureRule != drawable.HasValue) throw new InvalidDataException("Unexpected blacklist nesting: " + path);
                if (rule.Groups["boolean"].Value == "false") continue;
                int id = int.Parse(rule.Groups["id"].Value);
                string key = $"{gender.ToString().ToLowerInvariant()}/{category}/{(textureRule ? drawable : id)}/{(textureRule ? id.ToString() : "*")}";
                string value = rule.Groups["text"].Success ? rule.Groups["text"].Value : rule.Groups["double"].Value;
                restrictions.Add(key, rule.Groups["boolean"].Value == "true" ? "!blocked" : UnescapeLua(value).Replace("\\\"", "\"", StringComparison.Ordinal));
            }
        }
        return restrictions;
    }

    public static IReadOnlyList<string> GetRestrictionNames(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        string directory = Path.Combine(Path.GetFullPath(rootPath), "blrp_clothingstore", "blacklists");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var pattern = new Regex(
            @"^\s*\[\d+\]\s*=\s*'(?<value>(?:\\.|[^'])*)'",
            RegexOptions.CultureInvariant);
        return Directory.EnumerateFiles(directory, "*.lua", SearchOption.TopDirectoryOnly)
            .SelectMany(File.ReadLines)
            .Select(line => pattern.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["value"].Value
                .Replace("\\'", "'", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? GetDrawableRestriction(
        string rootPath,
        Gender gender,
        ComponentDefinition component,
        int globalIndex)
    {
        string path = GetPath(rootPath, gender);
        var lines = File.ReadAllText(path)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        string categoryKey = component.IsProp ? $"p{component.Slot}" : component.Slot.ToString();
        int tableStart = FindTableStart(lines);
        int tableEnd = FindBlockEnd(lines, tableStart);
        int categoryStart = FindLine(
            lines,
            $@"^\s{{2}}\['{Regex.Escape(categoryKey)}'\]\s*=\s*\{{\s*$",
            tableStart + 1,
            tableEnd);
        if (categoryStart < 0)
        {
            throw new InvalidDataException($"Blacklist category {categoryKey} was not found in {Path.GetFileName(path)}.");
        }

        int categoryEnd = FindBlockEnd(lines, categoryStart);
        int entryStart = FindLine(lines, $@"^\s{{4}}\[{globalIndex}\]\s*=", categoryStart + 1, categoryEnd);
        if (entryStart < 0)
        {
            return null;
        }

        Match match = Regex.Match(
            lines[entryStart],
            $@"^\s{{4}}\[{globalIndex}\]\s*=\s*'(?<value>(?:\\.|[^'])*)'",
            RegexOptions.CultureInvariant);
        return match.Success
            ? match.Groups["value"].Value
                .Replace("\\'", "'", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal)
            : null;
    }

    public static void AddDrawable(
        string rootPath,
        Gender gender,
        ComponentDefinition component,
        int globalIndex,
        string business) =>
        Update(rootPath, gender, component, globalIndex, null, business);

    public static void AddTexture(
        string rootPath,
        Gender gender,
        ComponentDefinition component,
        int globalIndex,
        int textureIndex,
        string business) =>
        Update(rootPath, gender, component, globalIndex, textureIndex, business);

    private static void Update(
        string rootPath,
        Gender gender,
        ComponentDefinition component,
        int globalIndex,
        int? textureIndex,
        string business)
    {
        business = business.Trim();
        if (business.Length == 0)
        {
            throw new ArgumentException("Choose a business before blacklisting clothing.", nameof(business));
        }

        string fullRoot = Path.GetFullPath(rootPath);
        string path = GetPath(rootPath, gender);
        string fileName = Path.GetFileName(path);

        string text = File.ReadAllText(path);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        string categoryKey = component.IsProp ? $"p{component.Slot}" : component.Slot.ToString();
        int tableStart = FindTableStart(lines);
        int tableEnd = FindBlockEnd(lines, tableStart);
        int categoryStart = FindLine(
            lines,
            $@"^\s{{2}}\['{Regex.Escape(categoryKey)}'\]\s*=\s*\{{\s*$",
            tableStart + 1,
            tableEnd);
        if (categoryStart < 0)
        {
            throw new InvalidDataException($"Blacklist category {categoryKey} was not found in {fileName}.");
        }

        int categoryEnd = FindBlockEnd(lines, categoryStart);
        int entryStart = FindLine(
            lines,
            $@"^\s{{4}}\[{globalIndex}\]\s*=",
            categoryStart + 1,
            categoryEnd);
        string escapedBusiness = EscapeLua(business);

        if (textureIndex == null)
        {
            if (entryStart >= 0)
            {
                if (HasBusiness(lines[entryStart], escapedBusiness))
                {
                    return;
                }
                throw new InvalidOperationException(
                    $"Clothing #{globalIndex} already has a blacklist entry. It was not overwritten.");
            }

            lines.Insert(categoryEnd, $"    [{globalIndex}] = '{escapedBusiness}',");
        }
        else if (entryStart < 0)
        {
            lines.InsertRange(categoryEnd,
            [
                $"    [{globalIndex}] = {{",
                $"      [{textureIndex.Value}] = '{escapedBusiness}',",
                "    },"
            ]);
        }
        else if (Regex.IsMatch(lines[entryStart], @"=\s*\{\s*$"))
        {
            int entryEnd = FindBlockEnd(lines, entryStart);
            int textureLine = FindLine(
                lines,
                $@"^\s{{6}}\[{textureIndex.Value}\]\s*=",
                entryStart + 1,
                entryEnd);
            if (textureLine >= 0)
            {
                if (HasBusiness(lines[textureLine], escapedBusiness))
                {
                    return;
                }
                throw new InvalidOperationException(
                    $"Clothing #{globalIndex} texture #{textureIndex.Value} is already blacklisted. It was not overwritten.");
            }

            lines.Insert(entryEnd, $"      [{textureIndex.Value}] = '{escapedBusiness}',");
        }
        else
        {
            if (HasBusiness(lines[entryStart], escapedBusiness))
            {
                return;
            }
            throw new InvalidOperationException(
                $"Clothing #{globalIndex} is already blacklisted as a whole drawable. It was not overwritten.");
        }

        string backupRoot = Path.Combine(
            fullRoot,
            ".clothing-locator-backups",
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(backupRoot);
        File.Copy(path, Path.Combine(backupRoot, fileName), false);
        File.WriteAllText(path, string.Join(newline, lines), new UTF8Encoding(false));
    }

    private static int FindLine(
        IReadOnlyList<string> lines,
        string pattern,
        int start = 0,
        int? end = null)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        int limit = Math.Min(end ?? lines.Count, lines.Count);
        for (int index = start; index < limit; index++)
        {
            if (regex.IsMatch(lines[index]))
            {
                return index;
            }
        }
        return -1;
    }

    internal static int FindTableStart(IReadOnlyList<string> lines)
    {
        for (int index = lines.Count - 1; index >= 0; index--)
        {
            if (Regex.IsMatch(lines[index], @"^blacklists\[.+\]\s*=\s*\{\s*$", RegexOptions.CultureInvariant))
            {
                return index;
            }
        }
        throw new InvalidDataException("The live blacklist table was not found.");
    }

    internal static int FindBlockEnd(IReadOnlyList<string> lines, int start)
    {
        int depth = 0;
        for (int index = start; index < lines.Count; index++)
        {
            string code = lines[index].Split("--", 2, StringSplitOptions.None)[0];
            depth += code.Count(character => character == '{');
            depth -= code.Count(character => character == '}');
            if (index > start && depth == 0)
            {
                return index;
            }
        }
        throw new InvalidDataException("The blacklist contains an unterminated Lua table.");
    }

    private static string EscapeLua(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal);

    internal static string UnescapeLua(string value) => value
        .Replace("\\'", "'", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static bool HasBusiness(string line, string escapedBusiness) => Regex.IsMatch(
        line,
        $@"=\s*'{Regex.Escape(escapedBusiness)}'\s*,?\s*(?:--.*)?$",
        RegexOptions.CultureInvariant);

    internal static string GetPath(string rootPath, Gender gender)
    {
        string fileName = gender == Gender.Male ? "mp_m_freemode_01.lua" : "mp_f_freemode_01.lua";
        string path = Path.Combine(
            Path.GetFullPath(rootPath),
            "blrp_clothingstore",
            "blacklists",
            fileName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("Could not find the clothing blacklist.", path);
    }

    internal static bool SelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "BLRP-Clothing-Blacklist-Test-" + Guid.NewGuid());
        string directory = Path.Combine(root, "blrp_clothingstore", "blacklists");
        Directory.CreateDirectory(directory);
        const string fixture = "--[[\r\nblacklists[`mp_m_freemode_01`] = {\r\n  ['4'] = {\r\n  },\r\n}\r\n]]\r\nblacklists[`mp_m_freemode_01`] = {\r\n  sex = 'male',\r\n  ['4'] = {\r\n  },\r\n}\r\n";
        File.WriteAllText(Path.Combine(directory, "mp_m_freemode_01.lua"), fixture);
        File.WriteAllText(
            Path.Combine(directory, "existing.lua"),
            "sex = 'female'\r\n  [1] = 'LEO',\r\n  [2] = 'Bob\\'s Burgers',\r\n");

        AddDrawable(root, Gender.Male, ClothingComponents.ByCode["lowr"], 400, "Bob's Burgers");
        AddTexture(root, Gender.Male, ClothingComponents.ByCode["lowr"], 401, 2, "Aces and Eights");
        AddDrawable(root, Gender.Male, ClothingComponents.ByCode["lowr"], 400, "Bob's Burgers");
        AddTexture(root, Gender.Male, ClothingComponents.ByCode["lowr"], 401, 2, "Aces and Eights");
        string result = File.ReadAllText(Path.Combine(directory, "mp_m_freemode_01.lua"));
        IReadOnlyList<string> names = GetRestrictionNames(root);
        return names.Contains("LEO") &&
               names.Contains("Bob's Burgers") &&
               !names.Contains("female") &&
               GetDrawableRestriction(root, Gender.Male, ClothingComponents.ByCode["lowr"], 400) == "Bob's Burgers" &&
               GetDrawableRestriction(root, Gender.Male, ClothingComponents.ByCode["lowr"], 401) == null &&
               result.Contains("[400] = 'Bob\\'s Burgers',", StringComparison.Ordinal) &&
               result.Contains("[401] = {", StringComparison.Ordinal) &&
               result.Contains("[2] = 'Aces and Eights',", StringComparison.Ordinal) &&
               Directory.EnumerateFiles(
                   Path.Combine(root, ".clothing-locator-backups"),
                   "*.lua",
                   SearchOption.AllDirectories).Count() == 2;
    }
}
