using System.Globalization;
using System.Text.RegularExpressions;

namespace Badlands.LiveryTool;

internal static class LiveryNaming
{
    private sealed record Numbered(string Prefix, int Number, int Padding);
    private static Numbered? Parse(string name)
    {
        Match match = Regex.Match(name, @"^([a-zA-Z0-9_]*?[a-zA-Z_])(\d+)$");
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out int number)) return null;
        string digits = match.Groups[2].Value;
        return new Numbered(match.Groups[1].Value, number, digits.StartsWith('0') ? digits.Length : 0);
    }

    public static (string Name, string Label) Suggest(VehicleKit kit, VehicleTemplate template, string model,
        ISet<string> occupiedNames, ISet<string> occupiedLabels)
    {
        var entries = kit.Liveries.Select(mod => (Asset: Parse(mod.Element("modelName")?.Value ?? ""),
            Label: Parse(mod.Element("modShopLabel")?.Value ?? ""))).ToArray();
        string? selectedPrefix = Parse(Path.GetFileNameWithoutExtension(template.Path))?.Prefix;
        var family = entries.Where(entry => entry.Asset != null).GroupBy(entry => entry.Asset!.Prefix, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key.Equals(selectedPrefix, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        string prefix = family?.Key ?? model.ToLowerInvariant() + "_liv";
        int number = checked((family?.Max(entry => entry.Asset!.Number) ?? 0) + 1);
        int padding = family?.Max(entry => entry.Asset!.Padding) ?? 0;
        var labelFamily = family?.Where(entry => entry.Label != null).GroupBy(entry => entry.Label!.Prefix, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).FirstOrDefault();
        int[] offsets = labelFamily?.Select(entry => entry.Label!.Number - entry.Asset!.Number).Distinct().ToArray() ?? [];
        int labelNumber = checked((labelFamily?.Max(entry => entry.Label!.Number) ?? 0) + 1);
        string Format(int value, int width) => value.ToString(width > 0 ? "D" + width : "D", CultureInfo.InvariantCulture);
        while (true)
        {
            string name = prefix + Format(number, padding);
            string label = labelFamily == null ? name.ToUpperInvariant() : labelFamily.Key + Format(
                offsets.Length == 1 ? checked(number + offsets[0]) : labelNumber, labelFamily.Max(entry => entry.Label!.Padding));
            if (!occupiedNames.Contains(name) && !occupiedLabels.Contains(label)) return (name, label);
            number = checked(number + 1);
            labelNumber = checked(labelNumber + 1);
        }
    }
}
