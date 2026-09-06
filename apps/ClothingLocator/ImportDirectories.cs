using System.Text.Json;

namespace BLRP.ClothingLocator;

internal sealed class ImportDirectories
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BLRP-Tools", "clothing-import-folders.json");
    public string? Model { get; set; }
    public string? Texture { get; set; }
    private static ImportDirectories? _current;

    public static DialogResult Show(OpenFileDialog dialog, IWin32Window owner, bool texture = false)
    {
        _current ??= Load(SettingsPath);
        dialog.InitialDirectory = _current.Resolve(texture, dialog.InitialDirectory);
        dialog.RestoreDirectory = true;
        var result = dialog.ShowDialog(owner);
        if (result != DialogResult.OK) return result;
        if (texture) _current.Texture = Path.GetDirectoryName(dialog.FileName);
        else _current.Model = Path.GetDirectoryName(dialog.FileName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_current));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the selection for this session if settings cannot be written.
            System.Diagnostics.Trace.WriteLine($"Could not save import folder: {ex.Message}");
        }
        return result;
    }

    private string Resolve(bool texture, string fallback) =>
        Directory.Exists(texture ? Texture : Model) ? (texture ? Texture : Model)! : fallback;

    private static ImportDirectories Load(string path)
    {
        try { return JsonSerializer.Deserialize<ImportDirectories>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal static bool SelfTest()
    {
        var folders = new ImportDirectories { Model = Path.GetTempPath(), Texture = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
        var restored = JsonSerializer.Deserialize<ImportDirectories>(JsonSerializer.Serialize(folders))!;
        return restored.Resolve(false, "fallback") == folders.Model && restored.Resolve(true, "fallback") == "fallback";
    }
}
