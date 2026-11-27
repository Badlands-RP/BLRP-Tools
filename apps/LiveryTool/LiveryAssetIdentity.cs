using System.Xml.Linq;

namespace Badlands.LiveryTool;

internal static class LiveryAssetIdentity
{
    public static void Rename(XDocument xml, string sourceAsset, string target)
    {
        if (xml.Root?.Element("Name") is XElement fragment) fragment.Value = "pack:/" + target;
        foreach (XElement drawable in xml.Descendants("Drawable"))
            if (drawable.Element("Name") is XElement name) name.Value = target;
        // These names describe the fragment itself. References to attachment/deformation bones,
        // group indices, bone tags and transforms retain their original values.
        foreach (XElement name in xml.Descendants("Archetype").Elements("Name")
            .Concat(xml.Descendants("Groups").Elements("Item").Elements("Name")))
            if (name.Value == sourceAsset) name.Value = target;
        foreach (XElement bone in xml.Descendants("Bones").Elements("Item").Where(bone =>
            (string?)bone.Element("Tag")?.Attribute("value") == "0" &&
            (string?)bone.Element("Index")?.Attribute("value") == "0" &&
            (string?)bone.Element("ParentIndex")?.Attribute("value") == "-1"))
            if (bone.Element("Name")?.Value == sourceAsset + "_ng") bone.Element("Name")!.Value = target + "_ng";
    }
}
