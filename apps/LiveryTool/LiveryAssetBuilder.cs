using System.Xml.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace Badlands.LiveryTool;

internal sealed record LiveryTexture(string Name, int Width, int Height)
{
    public override string ToString() => $"{Name} ({Width} × {Height})";
}

internal static class LiveryAssetBuilder
{
    public static LiveryTexture[] Textures(string template)
    {
        var yft = Load(template);
        var xml = XDocument.Parse(YftXml.GetXml(yft));
        return xml.Descendants("TextureDictionary").Elements("Item").Select(item => new LiveryTexture(
            item.Element("Name")!.Value, (int?)item.Element("Width")?.Attribute("value") ?? 0,
            (int?)item.Element("Height")?.Attribute("value") ?? 0)).Distinct().ToArray();
    }

    public static string? SuggestTexture(string template)
    {
        var xml = XDocument.Parse(YftXml.GetXml(Load(template)));
        string[] names = xml.Descendants("TextureDictionary").Elements("Item").Select(item => item.Element("Name")!.Value).Distinct().ToArray();
        string[] referenced = xml.Descendants("Parameters").Elements("Item")
            .Where(item => (string?)item.Attribute("type") == "Texture" &&
                (string?)item.Attribute("name") is "DiffuseSampler2" or "LiverySampler")
            .Select(item => item.Element("Name")?.Value).Where(name => name != null && names.Contains(name)).Cast<string>().Distinct().ToArray();
        return referenced.Length == 1 ? referenced[0] : names.Length == 1 ? names[0] : null;
    }

    public static void Build(string template, string textureName, string image, string name, string output,
        bool fitImage, CancellationToken cancellation)
    {
        string work = Path.Combine(Path.GetTempPath(), "BLRP-Livery-Asset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var xml = XDocument.Parse(YftXml.GetXml(Load(template), work));
            XElement[] textures = xml.Descendants("TextureDictionary").Elements("Item").ToArray();
            XElement[] targets = textures.Where(item => item.Element("Name")?.Value == textureName).ToArray();
            if (targets.Length == 0) throw new InvalidDataException("Selected embedded texture is missing from the template.");
            int width = (int?)targets[0].Element("Width")?.Attribute("value") ?? 0;
            int height = (int?)targets[0].Element("Height")?.Attribute("value") ?? 0;
            if (targets.Any(target => (int?)target.Element("Width")?.Attribute("value") != width || (int?)target.Element("Height")?.Attribute("value") != height))
                throw new InvalidDataException("The selected texture name occurs at different dimensions. Choose a template with an unambiguous livery texture.");
            string dds = Path.Combine(work, name + ".dds");
            new LiveryImageConverter().ConvertForTemplate(image, dds, width, height, fitImage, cancellation);
            Texture converted = DDSIO.GetTexture(File.ReadAllBytes(dds));
            foreach (XElement texture in targets)
            {
                texture.Element("Name")!.Value = name;
                texture.Element("FileName")!.Value = name + ".dds";
                texture.Element("Width")!.SetAttributeValue("value", converted.Width);
                texture.Element("Height")!.SetAttributeValue("value", converted.Height);
                texture.Element("MipLevels")!.SetAttributeValue("value", converted.Levels);
                texture.Element("Format")!.Value = "D3DFMT_DXT5";
            }
            foreach (XElement parameter in xml.Descendants("Parameters").Elements("Item").Where(item => (string?)item.Attribute("type") == "Texture"))
                if (parameter.Element("Name")?.Value == textureName) parameter.Element("Name")!.Value = name;
            LiveryAssetIdentity.Rename(xml, Path.GetFileNameWithoutExtension(template), name);
            cancellation.ThrowIfCancellationRequested();
            YftFile built = XmlYft.GetYft(xml.ToString(), work);
            byte[] bytes = built.Save();
            var reloaded = new YftFile();
            reloaded.Load(bytes);
            var verified = XDocument.Parse(YftXml.GetXml(reloaded));
            XElement[] actual = verified.Descendants("TextureDictionary").Elements("Item").ToArray();
            if (actual.Length != textures.Length || actual.Count(item => item.Element("Name")?.Value == name) != targets.Length)
                throw new InvalidDataException("Compiled YFT texture dictionary failed verification.");
            foreach (XElement target in actual.Where(item => item.Element("Name")?.Value == name))
                if ((int?)target.Element("Width")?.Attribute("value") != width || (int?)target.Element("Height")?.Attribute("value") != height ||
                    target.Element("Format")?.Value != "D3DFMT_DXT5" || (int?)target.Element("MipLevels")?.Attribute("value") != converted.Levels)
                    throw new InvalidDataException("Compiled texture dimensions/compression/mipmaps failed verification.");
            if (verified.Descendants("Parameters").Elements("Item").Any(item => (string?)item.Attribute("type") == "Texture" && item.Element("Name")?.Value == textureName))
                throw new InvalidDataException("Compiled YFT contains stale texture references.");
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllBytes(output, bytes);
        }
        finally { Directory.Delete(work, true); }
    }

    private static YftFile Load(string path)
    {
        var yft = new YftFile();
        yft.Load(NativeAssetReader.Read(path));
        if (yft.Fragment?.Drawable == null) throw new InvalidDataException("Template has no vehicle mod drawable.");
        return yft;
    }
}
