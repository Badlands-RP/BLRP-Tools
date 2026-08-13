using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BLRP.WeaponSkinTool;

internal sealed record WeaponSkinSettings(
    string RepositoryRoot,
    string DataDirectory,
    string StreamDirectory,
    string WeaponMeta,
    string ModelPrefix,
    string ComponentPrefix);

internal sealed record WeaponSkinPlan(
    int Index,
    string ModelName,
    string ComponentName,
    string ModelTarget,
    string TextureTarget,
    string ArchetypesMeta,
    string ComponentsMeta,
    string WeaponMeta,
    string? Warning);

internal static partial class WeaponSkinImporter
{
    public static (string Model, string Texture)? FindLatestAssetPair(string streamDirectory, string modelPrefix)
    {
        modelPrefix = RequirePrefix(modelPrefix, "model prefix");
        string? model = Directory.EnumerateFiles(streamDirectory, "*.ydr", SearchOption.TopDirectoryOnly)
            .Where(path => Suffix(Path.GetFileNameWithoutExtension(path), modelPrefix) > 0 && File.Exists(Path.ChangeExtension(path, ".ytd")))
            .OrderByDescending(path => Suffix(Path.GetFileNameWithoutExtension(path), modelPrefix))
            .FirstOrDefault();
        return model is null ? null : (model, Path.ChangeExtension(model, ".ytd"));
    }

    public static WeaponSkinPlan Analyze(WeaponSkinSettings settings)
    {
        string root = RequireDirectory(settings.RepositoryRoot, "BadlandsRP repository");
        string data = ResolveInside(root, settings.DataDirectory, "data directory");
        string stream = ResolveInside(root, settings.StreamDirectory, "stream directory");
        string archetypes = Path.Combine(data, "weaponarchetypes.meta");
        string components = Path.Combine(data, "weaponcomponents.meta");
        string weaponMeta = ResolveInside(root, settings.WeaponMeta, "weapon meta");
        RequireFile(archetypes, "weaponarchetypes.meta");
        RequireFile(components, "weaponcomponents.meta");
        RequireFile(weaponMeta, "weapon meta");
        RequireDirectory(stream, "stream directory");

        string modelPrefix = RequirePrefix(settings.ModelPrefix, "model prefix");
        string componentPrefix = RequirePrefix(settings.ComponentPrefix, "component prefix");
        string archetypeText = File.ReadAllText(archetypes);
        string componentText = File.ReadAllText(components);
        string weaponText = File.ReadAllText(weaponMeta);
        ValidateXml(archetypeText, archetypes);
        ValidateXml(componentText, components);
        ValidateXml(weaponText, weaponMeta);

        int archetypeMax = MaxSuffix(archetypeText, "modelName", modelPrefix);
        int componentMax = MaxSuffix(componentText, "Name", componentPrefix);
        int weaponMax = MaxSuffix(XmlComments().Replace(weaponText, match => new string(' ', match.Length)), "Name", componentPrefix);
        int fileMax = Directory.EnumerateFiles(stream)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Select(name => Suffix(name, modelPrefix))
            .DefaultIfEmpty(0)
            .Max();
        int index = new[] { archetypeMax, componentMax, weaponMax, fileMax }.Max() + 1;
        if (componentMax == 0 || archetypeMax == 0 || weaponMax == 0)
        {
            throw new InvalidDataException("The configured prefixes must already exist in all three metadata files; the tool needs one existing skin as its insertion template.");
        }

        int digits = Math.Max(2, new[] { archetypeMax, componentMax, weaponMax, fileMax }
            .Select(value => value.ToString().Length).Max());
        string suffix = index.ToString($"D{digits}");
        string modelName = $"{modelPrefix}_{suffix}";
        string componentName = $"{componentPrefix}_{suffix}";
        string warning = new[] { archetypeMax, componentMax, weaponMax, fileMax }.Distinct().Count() > 1
            ? $"Existing files/metas are out of step ({archetypeMax}/{componentMax}/{weaponMax}/{fileMax}); using the next safe number."
            : string.Empty;

        return new WeaponSkinPlan(
            index,
            modelName,
            componentName,
            Path.Combine(stream, modelName.ToLowerInvariant() + ".ydr"),
            Path.Combine(stream, modelName.ToLowerInvariant() + ".ytd"),
            archetypes,
            components,
            weaponMeta,
            string.IsNullOrEmpty(warning) ? null : warning);
    }

    public static WeaponSkinPlan Import(WeaponSkinSettings settings, string sourceModel, string sourceTexture, string? replacementImage = null)
    {
        WeaponSkinPlan plan = Analyze(settings);
        sourceModel = RequireAsset(sourceModel, ".ydr");
        sourceTexture = RequireAsset(sourceTexture, ".ytd");
        replacementImage = string.IsNullOrWhiteSpace(replacementImage) ? null : RequireReplacementImage(replacementImage);
        if (File.Exists(plan.ModelTarget) || File.Exists(plan.TextureTarget))
        {
            throw new IOException("The target skin files already exist. Scan again and resolve the numbering conflict before importing.");
        }

        byte[] archetypeOriginal = File.ReadAllBytes(plan.ArchetypesMeta);
        byte[] componentOriginal = File.ReadAllBytes(plan.ComponentsMeta);
        byte[] weaponOriginal = File.ReadAllBytes(plan.WeaponMeta);
        byte[] textureOutput = replacementImage is null
            ? File.ReadAllBytes(sourceTexture)
            : WeaponTextureBuilder.Build(sourceModel, sourceTexture, replacementImage);
        WeaponBonePlan bonePlan = WeaponBoneExpander.Plan(
            Path.GetDirectoryName(plan.ModelTarget)!,
            settings.ModelPrefix.Trim().TrimEnd('_'),
            plan.Index);
        string archetypeUpdated = AddArchetype(Decode(archetypeOriginal), plan.ModelName);
        string componentUpdated = AddComponent(Decode(componentOriginal), plan.ComponentName, plan.ModelName);
        string weaponUpdated = AddWeaponReference(Decode(weaponOriginal), settings.ComponentPrefix, plan.ComponentName, plan.Index);
        ValidateXml(archetypeUpdated, plan.ArchetypesMeta);
        ValidateXml(componentUpdated, plan.ComponentsMeta);
        ValidateXml(weaponUpdated, plan.WeaponMeta);

        string backupRoot = Path.Combine(
            Path.GetFullPath(settings.RepositoryRoot),
            ".weapon-skin-tool-backups",
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Backup(plan.ArchetypesMeta, settings.RepositoryRoot, backupRoot);
        Backup(plan.ComponentsMeta, settings.RepositoryRoot, backupRoot);
        Backup(plan.WeaponMeta, settings.RepositoryRoot, backupRoot);
        if (bonePlan.Changed) Backup(bonePlan.ModelPath!, settings.RepositoryRoot, backupRoot);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plan.ModelTarget)!);
            File.Copy(sourceModel, plan.ModelTarget, false);
            File.WriteAllBytes(plan.TextureTarget, textureOutput);
            if (bonePlan.Changed) WriteAtomically(bonePlan.ModelPath!, bonePlan.UpdatedBytes!);
            WriteAtomically(plan.ArchetypesMeta, EncodeLike(archetypeOriginal, archetypeUpdated));
            WriteAtomically(plan.ComponentsMeta, EncodeLike(componentOriginal, componentUpdated));
            WriteAtomically(plan.WeaponMeta, EncodeLike(weaponOriginal, weaponUpdated));
            return plan;
        }
        catch
        {
            File.Delete(plan.ModelTarget);
            File.Delete(plan.TextureTarget);
            File.WriteAllBytes(plan.ArchetypesMeta, archetypeOriginal);
            File.WriteAllBytes(plan.ComponentsMeta, componentOriginal);
            File.WriteAllBytes(plan.WeaponMeta, weaponOriginal);
            if (bonePlan.Changed) File.WriteAllBytes(bonePlan.ModelPath!, bonePlan.OriginalBytes!);
            throw;
        }
    }

    private static string AddArchetype(string xml, string modelName)
    {
        string newline = Newline(xml);
        string item = $"    <Item>{newline}      <modelName>{modelName}</modelName>{newline}      <txdName>{modelName}</txdName>{newline}      <ptfxAssetName>null</ptfxAssetName>{newline}      <lodDist value=\"50\"/>{newline}    </Item>{newline}";
        return InsertBeforeClosing(xml, "InitDatas", item);
    }

    private static string AddComponent(string xml, string componentName, string modelName)
    {
        string n = Newline(xml);
        string item = $"    <Item type=\"CWeaponComponentVariantModelInfo\">{n}" +
            $"      <Name>{componentName}</Name>{n}      <Model>{modelName}</Model>{n}" +
            $"      <LocName>WCT_INVALID</LocName>{n}      <LocDesc>WCD_INVALID</LocDesc>{n}" +
            $"      <AttachBone />{n}      <AccuracyModifier type=\"NULL\" />{n}      <DamageModifier type=\"NULL\" />{n}" +
            $"      <bShownOnWheel value=\"false\" />{n}      <CreateObject value=\"false\" />{n}" +
            $"      <HudDamage value=\"0\" />{n}      <HudSpeed value=\"0\" />{n}      <HudCapacity value=\"0\" />{n}" +
            $"      <HudAccuracy value=\"0\" />{n}      <HudRange value=\"0\" />{n}      <TintIndexOverride value=\"2\"/>{n}    </Item>{n}";
        return InsertBeforeClosing(xml, "Infos", item);
    }

    private static string AddWeaponReference(string xml, string prefix, string componentName, int skinIndex)
    {
        string masked = XmlComments().Replace(xml, match => new string(' ', match.Length));
        Match attachPoints = AttachPointsBlocks().Matches(masked)
            .Cast<Match>()
            .Where(match => Regex.IsMatch(match.Value, $"<Name>\\s*{Regex.Escape(prefix)}_\\d+\\s*</Name>", RegexOptions.IgnoreCase))
            .OrderByDescending(match => MaxSuffix(match.Value, "Name", prefix))
            .FirstOrDefault()
            ?? throw new InvalidDataException("Could not find the configured component family inside an active <AttachPoints> list in the weapon meta.");
        string boneName = WeaponBoneExpander.BoneForSkin(skinIndex);
        string attachValue = xml.Substring(attachPoints.Index, attachPoints.Length);
        string attachMasked = masked.Substring(attachPoints.Index, attachPoints.Length);
        Match group = Regex.Match(
            attachMasked,
            $@"<Item\b[^>]*>\s*<AttachBone>\s*{Regex.Escape(boneName)}\s*</AttachBone>\s*<Components\b[^>]*>.*?</Components>\s*</Item>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        string n = Newline(xml);
        if (!group.Success)
        {
            Match firstGroup = Regex.Match(attachMasked, @"(?m)^(?<indent>[ \t]*)<Item\b", RegexOptions.IgnoreCase);
            string indent = firstGroup.Success ? firstGroup.Groups["indent"].Value : "              ";
            string item = $"{indent}<Item>{n}" +
                $"{indent}  <AttachBone>{boneName}</AttachBone>{n}" +
                $"{indent}  <Components>{n}" +
                $"{indent}    <Item>{n}" +
                $"{indent}      <Name>{componentName}</Name>{n}" +
                $"{indent}      <Default value=\"false\" />{n}" +
                $"{indent}    </Item>{n}" +
                $"{indent}  </Components>{n}" +
                $"{indent}</Item>{n}";
            int close = attachValue.LastIndexOf("</AttachPoints>", StringComparison.OrdinalIgnoreCase);
            int absoluteClose = attachPoints.Index + close;
            int lineStart = xml.LastIndexOf('\n', Math.Max(0, absoluteClose - 1));
            return xml.Insert(lineStart < attachPoints.Index ? absoluteClose : lineStart + 1, item);
        }

        int familyItems = Regex.Matches(group.Value, $"<Name>\\s*{Regex.Escape(prefix)}_\\d+\\s*</Name>", RegexOptions.IgnoreCase).Count;
        if (familyItems >= 12)
            throw new InvalidDataException($"The {boneName} attachment group already contains 12 skins; the metadata numbering is inconsistent.");
        int groupAbsolute = attachPoints.Index + group.Index;
        int closeComponents = group.Value.LastIndexOf("</Components>", StringComparison.OrdinalIgnoreCase);
        int absoluteComponents = groupAbsolute + closeComponents;
        int closeLine = xml.LastIndexOf('\n', Math.Max(0, absoluteComponents - 1));
        int closeStart = closeLine < groupAbsolute ? absoluteComponents : closeLine + 1;
        string closeIndent = xml.Substring(closeStart, absoluteComponents - closeStart);
        string componentItem = $"{closeIndent}  <Item>{n}" +
            $"{closeIndent}    <Name>{componentName}</Name>{n}" +
            $"{closeIndent}    <Default value=\"false\" />{n}" +
            $"{closeIndent}  </Item>{n}";
        return xml.Insert(closeStart, componentItem);
    }

    private static string InsertBeforeClosing(string xml, string element, string value)
    {
        int index = xml.LastIndexOf($"</{element}>", StringComparison.OrdinalIgnoreCase);
        if (index < 0) throw new InvalidDataException($"Missing <{element}> in metadata.");
        int lineStart = xml.LastIndexOf('\n', Math.Max(0, index - 1));
        return xml.Insert(lineStart < 0 ? index : lineStart + 1, value);
    }

    private static int MaxSuffix(string xml, string element, string prefix) =>
        Regex.Matches(xml, $"<{element}>\\s*{Regex.Escape(prefix)}_(?<number>\\d+)\\s*</{element}>", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(match => int.Parse(match.Groups["number"].Value))
            .DefaultIfEmpty(0)
            .Max();

    private static int Suffix(string value, string prefix)
    {
        Match match = Regex.Match(value, $"^{Regex.Escape(prefix)}_(?<number>\\d+)$", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups["number"].Value) : 0;
    }

    private static string RequirePrefix(string value, string label)
    {
        value = value.Trim().TrimEnd('_');
        if (!Regex.IsMatch(value, "^[A-Za-z0-9_]+$") || value.Length < 2)
            throw new InvalidDataException($"The {label} may contain only letters, numbers, and underscores.");
        return value;
    }

    private static string RequireDirectory(string path, string label)
    {
        path = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"The {label} does not exist: {path}");
        return path;
    }

    private static string ResolveInside(string root, string relative, string label)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException($"The {label} must be relative to the repository.");
        string resolved = Path.GetFullPath(Path.Combine(root, relative.Trim()));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The {label} must stay inside the repository.");
        return resolved;
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing {label}: {path}");
    }

    private static string RequireAsset(string path, string extension)
    {
        path = Path.GetFullPath(path.Trim());
        RequireFile(path, extension + " source");
        if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected a {extension} file: {path}");
        byte[] header = new byte[4];
        using FileStream stream = File.OpenRead(path);
        if (stream.Read(header) != 4 || BitConverter.ToUInt32(header) != 0x37435352)
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a normal RSC7 resource file.");
        return path;
    }

    private static string RequireReplacementImage(string path)
    {
        path = Path.GetFullPath(path.Trim());
        RequireFile(path, "replacement texture");
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected a .png or .dds file: {path}");
        return path;
    }

    private static void Backup(string path, string root, string backupRoot)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), path);
        string target = Path.Combine(backupRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(path, target, false);
    }

    private static void WriteAtomically(string path, byte[] data)
    {
        string temp = path + ".weapon-skin-tool.tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, true);
    }

    private static string Decode(byte[] data) =>
        data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF
            ? Encoding.UTF8.GetString(data, 3, data.Length - 3)
            : Encoding.UTF8.GetString(data);

    private static byte[] EncodeLike(byte[] original, string value)
    {
        bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
        byte[] body = Encoding.UTF8.GetBytes(value);
        return bom ? [.. Encoding.UTF8.GetPreamble(), .. body] : body;
    }

    private static string Newline(string value) => value.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static void ValidateXml(string xml, string path)
    {
        try { XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
        catch (Exception exception) { throw new InvalidDataException($"Invalid XML in {path}: {exception.Message}", exception); }
    }

    [GeneratedRegex(@"<AttachPoints\b[^>]*>.*?</AttachPoints>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AttachPointsBlocks();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComments();

    public static bool SelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "BLRP-Weapon-Skin-Test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string data = Path.Combine(root, "resources", "blrp_weapons", "data", "bats");
            string stream = Path.Combine(root, "resources", "blrp_weapons", "stream", "bats");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(stream);
            File.WriteAllText(Path.Combine(data, "weaponarchetypes.meta"), "<Root><InitDatas><Item><modelName>W_ME_Bat_BL_01</modelName></Item></InitDatas></Root>");
            File.WriteAllText(Path.Combine(data, "weaponcomponents.meta"), "<Root><Infos><Item><Name>COMPONENT_BAT_VARMOD_BL_01</Name></Item></Infos></Root>");
            string weaponMeta = Path.Combine(root, "resources", "blrp_weapons", "weapons.meta");
            Directory.CreateDirectory(Path.GetDirectoryName(weaponMeta)!);
            File.WriteAllText(weaponMeta, "<Root><AttachPoints><Item><AttachBone>Gun_Root</AttachBone><Components><Item><Name>COMPONENT_BAT_VARMOD_BL_01</Name></Item></Components></Item></AttachPoints></Root>");
            string sourceModel = Path.Combine(root, "source.ydr");
            string sourceTexture = Path.Combine(root, "source.ytd");
            File.WriteAllBytes(sourceModel, [0x52, 0x53, 0x43, 0x37, 1]);
            File.WriteAllBytes(sourceTexture, [0x52, 0x53, 0x43, 0x37, 2]);
            var settings = new WeaponSkinSettings(root, @"resources\blrp_weapons\data\bats", @"resources\blrp_weapons\stream\bats", @"resources\blrp_weapons\weapons.meta", "W_ME_Bat_BL", "COMPONENT_BAT_VARMOD_BL");
            WeaponSkinPlan plan = Import(settings, sourceModel, sourceTexture);
            (string Model, string Texture)? latest = FindLatestAssetPair(stream, "W_ME_Bat_BL");
            string weaponUpdated = File.ReadAllText(plan.WeaponMeta);
            string boundary = "<Root><AttachPoints><Item><AttachBone>Gun_Root</AttachBone><Components>" +
                string.Concat(Enumerable.Range(1, 12).Select(index => $"<Item><Name>COMPONENT_BAT_VARMOD_BL_{index:D2}</Name></Item>")) +
                "</Components></Item><!--<Item><AttachBone>WAPSkinA</AttachBone><Components /></Item>--></AttachPoints></Root>";
            string expanded = AddWeaponReference(boundary, "COMPONENT_BAT_VARMOD_BL", "COMPONENT_BAT_VARMOD_BL_13", 13);
            XDocument expandedDocument = XDocument.Parse(expanded);
            XElement[] liveGroups = expandedDocument.Descendants("Item")
                .Where(item => item.Element("AttachBone")?.Value == "WAPSkinA")
                .ToArray();
            return plan.Index == 2 && File.Exists(plan.ModelTarget) && File.Exists(plan.TextureTarget) &&
                File.ReadAllText(plan.ArchetypesMeta).Contains("W_ME_Bat_BL_02", StringComparison.Ordinal) &&
                File.ReadAllText(plan.ComponentsMeta).Contains("COMPONENT_BAT_VARMOD_BL_02", StringComparison.Ordinal) &&
                weaponUpdated.Contains("COMPONENT_BAT_VARMOD_BL_02", StringComparison.Ordinal) &&
                latest?.Model == plan.ModelTarget && latest?.Texture == plan.TextureTarget &&
                liveGroups.Length == 1 && liveGroups[0].Descendants("Name").Single().Value == "COMPONENT_BAT_VARMOD_BL_13" &&
                Directory.Exists(Path.Combine(root, ".weapon-skin-tool-backups"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
