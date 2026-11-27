namespace Badlands.LiveryTool;

internal static class NativeAssetReader
{
    public static byte[] Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith("FXAP"u8))
            throw new InvalidDataException($"{Path.GetFileName(path)} is escrow encrypted (FXAP). Offline preview and YFT building require a readable model and textures. Ask the creator for an unencrypted preview asset, or preview in your entitled FiveM resource. See Help for details.");
        return bytes;
    }
}
