using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace BLRP.PropertyMapper;

internal enum ReviewLevel { Good, Warning, Error }

internal sealed record MapItem(
    int Number,
    string Guid,
    string Model,
    int Tint,
    float X,
    float Y,
    float Z,
    float RotationX,
    float RotationY,
    float RotationZ,
    float RotationW,
    ReviewLevel Level,
    string Review)
{
    public float Heading => MathF.Atan2(
        2f * ((RotationW * RotationZ) + (RotationX * RotationY)),
        1f - (2f * ((RotationY * RotationY) + (RotationZ * RotationZ))));
}

internal sealed class PropertyMapDocument
{
    private PropertyMapDocument(string sourcePath, string xml, string name, IReadOnlyList<MapItem> items, IReadOnlyList<string> errors)
    {
        SourcePath = sourcePath;
        Xml = xml;
        Name = name;
        Items = items;
        Errors = errors;
    }

    public string SourcePath { get; }
    public string Xml { get; }
    public string Name { get; }
    public IReadOnlyList<MapItem> Items { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool CanExport => Errors.Count == 0 && Items.All(item => item.Level != ReviewLevel.Error);
    public int WarningCount => Items.Count(item => item.Level == ReviewLevel.Warning);

    public static PropertyMapDocument Load(string path) => Parse(File.ReadAllText(path), path);

    internal static PropertyMapDocument Parse(string xml, string sourcePath = "sample.ymap.xml")
    {
        XDocument document;
        var errors = new List<string>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            throw new InvalidDataException("The selected file is not valid XML. " + exception.Message, exception);
        }

        XElement root = document.Root ?? throw new InvalidDataException("The XML document is empty.");
        if (root.Name.LocalName != "CMapData")
        {
            throw new InvalidDataException($"Expected a CMapData document, but found {root.Name.LocalName}.");
        }

        string name = Child(root, "name")?.Value.Trim() ?? "";
        if (name.Length == 0) errors.Add("The map name is missing.");

        XElement? entities = Child(root, "entities");
        var items = new List<MapItem>();
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int number = 0;
        foreach (XElement entity in entities?.Elements().Where(element => element.Name.LocalName == "Item") ?? [])
        {
            number++;
            var issues = new List<string>();
            ReviewLevel level = ReviewLevel.Good;
            string type = entity.Attribute("type")?.Value ?? "CEntityDef";
            if (!type.Equals("CEntityDef", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("unsupported entity type");
                level = ReviewLevel.Error;
            }

            string model = Child(entity, "archetypeName")?.Value.Trim() ?? "";
            if (model.Length == 0)
            {
                issues.Add("missing model");
                level = ReviewLevel.Error;
            }

            XElement? guidElement = Child(entity, "guid");
            string guid = Value(guidElement, "?");
            if (guidElement is null || guid == "?")
            {
                issues.Add("missing GUID");
                level = ReviewLevel.Error;
            }
            else if (!guids.Add(guid))
            {
                issues.Add("duplicate GUID");
                level = Max(level, ReviewLevel.Warning);
            }

            XElement? position = Child(entity, "position");
            XElement? rotation = Child(entity, "rotation");
            bool transformOk = TryFloat(position, "x", out float x) &
                               TryFloat(position, "y", out float y) &
                               TryFloat(position, "z", out float z) &
                               TryFloat(rotation, "x", out float rx) &
                               TryFloat(rotation, "y", out float ry) &
                               TryFloat(rotation, "z", out float rz) &
                               TryFloat(rotation, "w", out float rw);
            if (!transformOk)
            {
                issues.Add("invalid position or rotation");
                level = ReviewLevel.Error;
            }
            else
            {
                float quaternionLength = MathF.Sqrt((rx * rx) + (ry * ry) + (rz * rz) + (rw * rw));
                if (MathF.Abs(quaternionLength - 1f) > 0.02f)
                {
                    issues.Add($"rotation length {quaternionLength:0.###}");
                    level = Max(level, ReviewLevel.Warning);
                }
            }

            int tint = IntValue(Child(entity, "tintValue"));
            items.Add(new MapItem(number, guid, model, tint, x, y, z, rx, ry, rz, rw, level,
                issues.Count == 0 ? "Good" : string.Join(", ", issues)));
        }

        if (entities is null) errors.Add("The entities section is missing.");
        if (items.Count == 0) errors.Add("The map contains no entities.");
        return new PropertyMapDocument(sourcePath, xml, name, items, errors);
    }

    public byte[] BuildYmap()
    {
        if (!CanExport) throw new InvalidOperationException("Fix the errors in the XML before exporting.");
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(Xml);
        byte[] bytes = XmlMeta.GetRSCData(document) ?? throw new InvalidDataException("CodeWalker could not convert this CMapData XML.");
        if (bytes.Length < 4 || Encoding.ASCII.GetString(bytes, 0, 4) != "RSC7")
            throw new InvalidDataException("CodeWalker did not produce a valid RSC7 YMAP.");
        return bytes;
    }

    public byte[] BuildManifest()
    {
        if (!CanExport) throw new InvalidOperationException("Fix the errors in the XML before exporting.");
        string escapedName = System.Security.SecurityElement.Escape(Name) ?? Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="no"?>
            <CPackFileMetaData>
              <MapDataGroups/>
              <HDTxdBindingArray/>
              <imapDependencies/>
              <imapDependencies_2>
                <Item>
                  <imapName>{escapedName}</imapName>
                  <manifestFlags/>
                  <itypDepArray/>
                </Item>
              </imapDependencies_2>
              <itypDependencies_2/>
              <Interiors/>
            </CPackFileMetaData>
            """;
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        byte[] bytes = XmlMeta.GetPSOData(document) ?? throw new InvalidDataException("CodeWalker could not build the manifest.");
        if (bytes.Length == 0) throw new InvalidDataException("CodeWalker produced an empty manifest.");
        return bytes;
    }

    private static XElement? Child(XElement parent, string name) => parent.Elements().FirstOrDefault(element => element.Name.LocalName == name);
    private static string Value(XElement? element, string fallback) => element?.Attribute("value")?.Value ?? element?.Value.Trim() ?? fallback;
    private static int IntValue(XElement? element) => int.TryParse(Value(element, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    private static ReviewLevel Max(ReviewLevel first, ReviewLevel second) => first > second ? first : second;

    private static bool TryFloat(XElement? element, string attribute, out float value)
    {
        bool parsed = float.TryParse(element?.Attribute(attribute)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return parsed && float.IsFinite(value);
    }

    public static bool SelfTest()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <CMapData>
              <name>property_48_test</name><parent/><flags value="0"/><contentFlags value="65"/>
              <streamingExtentsMin x="0" y="0" z="0"/><streamingExtentsMax x="20" y="20" z="20"/>
              <entitiesExtentsMin x="0" y="0" z="0"/><entitiesExtentsMax x="20" y="20" z="20"/>
              <entities><Item type="CEntityDef"><archetypeName>prop_helipad_01</archetypeName><flags value="1835040"/><guid value="1"/><position x="10" y="10" z="10"/><rotation x="0" y="0" z="0" w="1"/><scaleXY value="1"/><scaleZ value="1"/><parentIndex value="-1"/><lodDist value="100"/><childLodDist value="0"/><lodLevel>LODTYPES_DEPTH_ORPHANHD</lodLevel><numChildren value="0"/><priorityLevel>PRI_REQUIRED</priorityLevel><extensions/><ambientOcclusionMultiplier value="255"/><artificialAmbientOcclusion value="255"/><tintValue value="0"/></Item></entities>
              <containerLods itemType="rage__fwContainerLodDef"/><boxOccluders itemType="BoxOccluder"/><occludeModels itemType="OccludeModel"/><physicsDictionaries/><instancedData><ImapLink/><PropInstanceList itemType="rage__fwPropInstanceListDef"/><GrassInstanceList itemType="rage__fwGrassInstanceListDef"/></instancedData><timeCycleModifiers itemType="CTimeCycleModifier"/><carGenerators itemType="CCarGen"/><LODLightsSOA><direction itemType="FloatXYZ"/><falloff/><falloffExponent/><timeAndStateFlags/><hash/><coneInnerAngle/><coneOuterAngleOrCapExt/><coronaIntensity/></LODLightsSOA><DistantLODLightsSOA><position itemType="FloatXYZ"/><RGBI/><numStreetLights value="0"/><category value="0"/></DistantLODLightsSOA><block><version value="0"/><flags value="0"/><name>property_48_test</name><exportedBy>blrp_mapping</exportedBy><time>test</time></block>
            </CMapData>
            """;
        PropertyMapDocument map = Parse(xml);
        PropertyMapDocument invalid = Parse(xml.Replace("position x=\"10\"", "position x=\"not-a-number\"", StringComparison.Ordinal));
        byte[] ymap = map.BuildYmap();
        byte[] manifest = map.BuildManifest();
        var decoded = new YmapFile();
        decoded.Load(ymap);
        var manifestEntry = new RpfBinaryFileEntry
        {
            Name = "property_48_test_manifest.ymf",
            NameLower = "property_48_test_manifest.ymf",
            Path = "property_48_test_manifest.ymf",
            FileSize = (uint)manifest.Length,
            FileUncompressedSize = (uint)manifest.Length
        };
        var decodedManifest = new YmfFile();
        decodedManifest.Load(manifest, manifestEntry);
        return map.CanExport && !invalid.CanExport && map.Items.Count == 1 && decoded.AllEntities?.Length == 1 &&
               decodedManifest.imapDependencies2?.Length == 1;
    }
}
