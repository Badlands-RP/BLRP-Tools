using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace Badlands.LiveryTool;

internal sealed record ModkitConversionRequest(CatalogVehicle Vehicle, VehicleKit Kit);
internal sealed record PlannedModkit(string Vehicle, string OriginalName, string Name, int Id, string MasterList);
internal sealed class ModkitConversionException(string vehicleKey, Exception cause) : IOException(vehicleKey + " modkit: " + cause.Message, cause)
{
    public string VehicleKey { get; } = vehicleKey;
}

internal static class ModkitConversion
{
    public static string[] FindMasterLists(VehicleCatalog catalog) => catalog.Files.Concat(Directory.EnumerateFiles(catalog.Root, "*.txt"))
        .Distinct(StringComparer.OrdinalIgnoreCase).Where(path =>
        Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"modkit[\s_-]*master[\s_-]*list", RegexOptions.IgnoreCase)).ToArray();

    public static string ResolveMasterList(VehicleCatalog catalog, string? selected)
    {
        if (string.IsNullOrWhiteSpace(selected))
        {
            string[] matches = FindMasterLists(catalog);
            if (matches.Length != 1) throw new InvalidDataException("Choose the modkit master list in Advanced; it could not be found uniquely in this repository.");
            selected = matches[0];
        }
        string path = Path.GetFullPath(Path.IsPathRooted(selected) ? selected : Path.Combine(catalog.Root, selected));
        if (!File.Exists(path)) throw new FileNotFoundException("Choose an existing modkit master list, or create an empty text file for a new list.", path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(Path.GetDirectoryName(path)!) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Select a modkit master list without symbolic links or directory junctions.");
        return path;
    }

    public static VehicleKit Apply(VehicleCatalog catalog, ModkitConversionRequest request, string masterList,
        Func<string, string> read, Action<string, string> write, ICollection<PlannedModkit> changes)
    {
        CatalogVehicle vehicle = request.Vehicle;
        VehicleKit kit = request.Kit;
        if (!catalog.Vehicles.Contains(vehicle) || !vehicle.Kits.Contains(kit)) throw new InvalidDataException("Select a current, metadata-linked vehicle modkit before converting it.");
        if (vehicle.Problems.Length > 0 || catalog.Vehicles.Count(item => item.Model.Equals(vehicle.Model, StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidDataException(vehicle.Model + ": resolve conflicting or missing vehicle metadata before converting its modkit.");
        foreach (string path in vehicle.MetadataPaths.Append(kit.Path).Distinct(StringComparer.OrdinalIgnoreCase))
            ResourceMetadata.ValidateRegistration(ResourceMetadata.Manifest(catalog.Root, path), path);
        string list = read(masterList);
        string[] entries = list.Split('\n').Select(line => line.Trim()).Where(line => Regex.IsMatch(line, @"^\d+_[a-zA-Z0-9_]+$")).ToArray();
        if (entries.GroupBy(entry => entry[..entry.IndexOf('_')]).Any(group => group.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            throw new InvalidDataException("The master list assigns one ID to different modkits. Resolve those conflicting entries before exporting.");
        bool shared = catalog.Vehicles.Any(other => other.Key != vehicle.Key && other.Kits.Any(otherKit => otherKit.Name == kit.Name && otherKit.Path == kit.Path));
        if (entries.Contains(kit.Name, StringComparer.OrdinalIgnoreCase) && !shared)
        {
            int currentId = (int?)kit.Xml.Element("id")?.Attribute("value") ?? throw new InvalidDataException("The selected modkit has no numeric ID.");
            int registeredId = int.Parse(kit.Name[..kit.Name.IndexOf('_')], CultureInfo.InvariantCulture);
            if (registeredId > ushort.MaxValue || catalog.Kits.Any(other => other != kit && (int?)other.Xml.Element("id")?.Attribute("value") == registeredId))
                throw new InvalidDataException("The registered ID is outside the supported range or already assigned to another carcols modkit.");
            string current = read(kit.Path);
            XElement source = XDocument.Parse(current, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo).Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")?.Value.Trim() == kit.Name);
            if (!XNode.DeepEquals(source, kit.Xml)) throw new InvalidDataException("Modkit metadata changed since scanning. Review again before converting.");
            if (currentId != registeredId)
            {
                XElement identity = source.Element("id")!;
                var span = ElementSpan(current, identity);
                identity.SetAttributeValue("value", registeredId);
                write(kit.Path, current[..span.Start] + identity.ToString(SaveOptions.DisableFormatting) + current[span.End..]);
            }
            changes.Add(new PlannedModkit(vehicle.Model, kit.Name, kit.Name, registeredId, masterList));
            XElement repaired = XDocument.Parse(read(kit.Path), LoadOptions.PreserveWhitespace).Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")?.Value.Trim() == kit.Name);
            return new VehicleKit(kit.Path, kit.Name, repaired);
        }
        var usedIds = catalog.Kits.Select(item => (int?)item.Xml.Element("id")?.Attribute("value")).Where(id => id != null).Select(id => id!.Value).ToHashSet();
        foreach (string entry in entries) usedIds.Add(int.Parse(entry[..entry.IndexOf('_')], CultureInfo.InvariantCulture));
        int largest = entries.Select(entry => int.Parse(entry[..entry.IndexOf('_')], CultureInfo.InvariantCulture)).DefaultIfEmpty(59990).Max();
        int id = Math.Max(60000, checked((largest / 10 + 1) * 10));
        while (usedIds.Contains(id)) id = checked(id + 10);
        if (id > ushort.MaxValue) throw new InvalidDataException("No free modkit ID remains in the master list's custom range. Resolve the ID allocation before exporting.");
        string name = $"{id}_bl_{vehicle.Model.ToLowerInvariant()}_modkit";
        if (!Regex.IsMatch(name, @"^\d+_[a-zA-Z0-9_]+$")) throw new InvalidDataException("The vehicle spawn name cannot be used in a modkit name.");
        if (catalog.Kits.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("A modkit with the allocated name already exists. Review the master list.");
        string cols = read(kit.Path);
        var colsXml = XDocument.Parse(cols, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        XElement original = colsXml.Descendants("Kits").Elements("Item").Single(item => item.Element("kitName")?.Value.Trim() == kit.Name);
        if (!XNode.DeepEquals(original, kit.Xml)) throw new InvalidDataException("Modkit metadata changed since scanning. Review again before converting.");
        var converted = XElement.Parse(original.ToString(SaveOptions.DisableFormatting));
        converted.SetElementValue("kitName", name);
        XElement idElement = converted.Element("id") ?? throw new InvalidDataException("The selected modkit has no ID element.");
        idElement.SetAttributeValue("value", id);
        var variations = vehicle.MetadataPaths.SelectMany(path => XDocument.Parse(read(path), LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo)
            .Descendants("variationData").Elements("Item").Where(item => item.Element("modelName")?.Value.Equals(vehicle.Model, StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => (Path: path, Item: item))).ToArray();
        if (variations.Length != 1) throw new InvalidDataException("The selected vehicle needs one unambiguous carvariations entry before conversion.");
        var variation = variations[0];
        XElement[] references = variation.Item.Element("kits")?.Elements("Item").Where(item => Hash(item.Value.Trim()) == Hash(kit.Name)).ToArray() ?? [];
        if (references.Length != 1) throw new InvalidDataException("The selected vehicle's modkit reference could not be identified uniquely.");
        string vars = read(variation.Path);
        (int start, int end) = ElementSpan(vars, references[0]);
        string raw = vars[start..end];
        int openingEnd = raw.IndexOf('>') + 1, closingStart = raw.LastIndexOf("</", StringComparison.Ordinal);
        if (closingStart < openingEnd) throw new InvalidDataException("The vehicle modkit reference is empty.");
        write(variation.Path, vars[..start] + raw[..openingEnd] + name + raw[closingStart..] + vars[end..]);
        cols = read(kit.Path);
        colsXml = XDocument.Parse(cols, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        XElement kitsParent = colsXml.Descendants("Kits").Single(parent => parent.Elements("Item").Any(item => item.Element("kitName")?.Value.Trim() == kit.Name));
        write(kit.Path, AppendKit(cols, kitsParent, converted));
        string newline = list.Contains("\r\n") ? "\r\n" : "\n";
        write(masterList, list + (list.Length > 0 && !list.EndsWith('\n') ? newline : "") + name + newline);
        changes.Add(new PlannedModkit(vehicle.Model, kit.Name, name, id, masterList));
        // Keep the same source metadata path; the original kit and all of its users remain intact.
        string updated = read(kit.Path);
        XElement staged = XDocument.Parse(updated, LoadOptions.PreserveWhitespace).Descendants("Kits").Elements("Item")
            .Single(item => item.Element("kitName")?.Value == name);
        return new VehicleKit(kit.Path, name, staged);
    }

    private static uint Hash(string value) => uint.TryParse(value, out uint numeric) ? numeric :
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(value[2..], NumberStyles.HexNumber, null, out numeric) ? numeric : JenkHash.GenHash(value.ToLowerInvariant());

    private static int Offset(string text, int line, int column)
    {
        int offset = 0;
        for (int i = 1; i < line; i++) offset = text.IndexOf('\n', offset) + 1;
        return offset + column - 2; // XML line information points at the name, immediately after '<'.
    }

    internal static (int Start, int End) ElementSpan(string text, XElement element)
    {
        var target = (IXmlLineInfo)element;
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.Read())
        {
            var info = (IXmlLineInfo)reader;
            if (reader.NodeType != XmlNodeType.Element || info.LineNumber != target.LineNumber || info.LinePosition != target.LinePosition) continue;
            int start = Offset(text, info.LineNumber, info.LinePosition), depth = reader.Depth;
            if (reader.IsEmptyElement) return (start, text.IndexOf('>', start) + 1);
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                    return (start, text.IndexOf('>', Offset(text, info.LineNumber, info.LinePosition)) + 1);
            break;
        }
        throw new InvalidDataException("Could not locate the original XML element without reformatting unrelated metadata.");
    }

    internal static string AppendKit(string text, XElement parent, XElement converted)
    {
        var span = ElementSpan(text, parent);
        int close = text.LastIndexOf("</Kits", span.End - 1, span.End - span.Start, StringComparison.Ordinal);
        if (close < 0) throw new InvalidDataException("The carcols file needs a Kits container.");
        int line = text.LastIndexOf('\n', close) + 1;
        string indent = text[line..close];
        if (!indent.All(char.IsWhiteSpace)) { line = close; indent = "  "; }
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        string fragment = string.Join(newline, converted.ToString().Replace("\r\n", "\n").Split('\n').Select(value => indent + "  " + value));
        string result = text.Insert(line, (line == close && indent == "  " ? newline : "") + fragment + newline);
        XDocument.Parse(result);
        return result;
    }
}
