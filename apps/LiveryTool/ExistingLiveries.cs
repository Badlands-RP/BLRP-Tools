using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Badlands.LiveryTool;

internal sealed record ExistingLivery(string VehicleKey, VehicleKit Kit, int Slot, XElement Mod, string Model, string Label, string DisplayName, string[] Paths)
{
    public string Key => Kit.Path + "|" + Kit.Name + "|" + Slot + "|" + Model;
    public bool CanReplace => Paths.Length == 1;
    public string ReplacementUnavailableReason => Paths.Length == 0
        ? "This slot has no YFT in the selected repository. It can be previewed from game files, but YFT-only replacement needs a repository file."
        : Paths.Length > 1 ? "Several repository files use this YFT filename. Resolve the duplicate streamed files and scan again before replacing it." : "";
    public override string ToString() => $"Slot {Slot} · {DisplayName} · {Model}.yft" +
        (Paths.Length == 0 ? " (game asset · preview only)" : Paths.Length > 1 ? " (ambiguous files)" : "");
}

internal static class ExistingLiveries
{
    public static ExistingLivery[] Load(VehicleCatalog catalog, CatalogVehicle vehicle, CancellationToken cancellation = default)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in catalog.Files.Where(path => path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
        {
            cancellation.ThrowIfCancellationRequested();
            string text = File.ReadAllText(path);
            if (!text.Contains("AddTextEntry", StringComparison.Ordinal)) continue;
            foreach (var entry in DisplayNames(text))
            {
                if (names.TryGetValue(entry.Key, out string? prior) && prior != entry.Value) conflicts.Add(entry.Key);
                else names[entry.Key] = entry.Value;
            }
        }
        return vehicle.Kits.SelectMany(kit => kit.Liveries.Select((mod, slot) =>
        {
            string model = mod.Element("modelName")?.Value.Trim() ?? "";
            string label = mod.Element("modShopLabel")?.Value.Trim() ?? "";
            string display = conflicts.Contains(label) ? label + " (conflicting Lua names)" : names.GetValueOrDefault(label, label.Length > 0 ? label : model);
            string[] paths = catalog.Files.Where(path => Path.GetExtension(path).Equals(".yft", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileNameWithoutExtension(path).Equals(model, StringComparison.OrdinalIgnoreCase)).ToArray();
            return new ExistingLivery(vehicle.Key, kit, slot, mod, model, label, display, paths);
        })).ToArray();
    }

    public static void Validate(VehicleCatalog catalog, CatalogVehicle vehicle, ExistingLivery selected)
    {
        VehicleKit? kit = vehicle.Kits.SingleOrDefault(kit => kit.Path == selected.Kit.Path && kit.Name == selected.Kit.Name);
        if (vehicle.Key != selected.VehicleKey || kit == null || selected.Slot < 0 || selected.Slot >= kit.Liveries.Length ||
            !XNode.DeepEquals(kit.Liveries[selected.Slot], selected.Mod)) throw new InvalidDataException("The existing livery selection changed. Select it again after scanning.");
        string[] paths = catalog.Files.Where(path => Path.GetExtension(path).Equals(".yft", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileNameWithoutExtension(path).Equals(selected.Model, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!paths.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(selected.Paths.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected livery's files changed. Scan and select it again.");
    }

    public static string[] OtherUses(VehicleCatalog catalog, ExistingLivery selected) => catalog.Vehicles.SelectMany(vehicle =>
        vehicle.Kits.SelectMany(kit => kit.Liveries.Select((mod, slot) => (mod, slot, kit))).Where(item =>
            item.mod.Element("modelName")?.Value.Equals(selected.Model, StringComparison.OrdinalIgnoreCase) == true &&
            !(vehicle.Key == selected.VehicleKey && item.kit.Path == selected.Kit.Path && item.kit.Name == selected.Kit.Name && item.slot == selected.Slot))
            .Select(item => $"{vehicle.Model} slot {item.slot}")).Distinct().ToArray();

    internal static IReadOnlyList<KeyValuePair<string, string>> DisplayNames(string text)
    {
        List<LuaLiveryEditor.Token> tokens;
        try { tokens = LuaLiveryEditor.Tokens(text); }
        catch (InvalidDataException) { return []; }
        var result = new List<KeyValuePair<string, string>>();
        for (int i = 0; i + 5 < tokens.Count; i++)
            if (tokens[i].Value == "AddTextEntry" && tokens[i + 1].Value == "(" && tokens[i + 3].Value == "," && tokens[i + 5].Value == ")" &&
                Decode(tokens[i + 2].Value) is string label && Decode(tokens[i + 4].Value) is string value) result.Add(new(label, value));
        return result;
    }

    private static string? Decode(string value)
    {
        if (value.StartsWith('['))
        {
            int opening = value.IndexOf('[', 1);
            if (opening < 1 || !value[1..opening].All(c => c == '=')) return null;
            string body = value[(opening + 1)..^(opening + 1)];
            return body.StartsWith("\r\n") ? body[2..] : body.StartsWith('\n') ? body[1..] : body;
        }
        if (value.Length < 2 || value[0] is not ('\'' or '"') || value[^1] != value[0]) return null;
        var text = new StringBuilder();
        for (int i = 1; i < value.Length - 1; i++)
        {
            char c = value[i];
            if (c != '\\') { text.Append(c); continue; }
            if (++i >= value.Length - 1) return null;
            c = value[i];
            if (c is >= '0' and <= '9')
            {
                int start = i;
                while (i + 1 < value.Length - 1 && i - start < 2 && char.IsAsciiDigit(value[i + 1])) i++;
                if (!int.TryParse(value[start..(i + 1)], out int number) || number > 255) return null;
                text.Append((char)number); continue;
            }
            if (c == 'z') { while (i + 1 < value.Length - 1 && char.IsWhiteSpace(value[i + 1])) i++; continue; }
            if (c == 'x')
            {
                if (i + 2 >= value.Length - 1 || !int.TryParse(value[(i + 1)..(i + 3)], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int number)) return null;
                text.Append((char)number); i += 2; continue;
            }
            char decoded = c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'a' => '\a', 'b' => '\b', 'f' => '\f', 'v' => '\v', '\\' => '\\', '\'' => '\'', '"' => '"', _ => '\0' };
            if (decoded == '\0') return null;
            text.Append(decoded);
        }
        return text.ToString();
    }
}
