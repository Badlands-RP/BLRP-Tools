using System.Text.Json;

namespace BLRP.ClothingLocator;

internal sealed class ImportDirectories
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BLRP-Tools", "clothing-import-folders.json");
    public string? Model { get; set; }
    public string? Texture { get; set; }
    public bool KeepBackups { get; set; }
    private static ImportDirectories? _current;
    private static ImportDirectories Current => _current ??= Load(SettingsPath);

    public static bool KeepLocalBackups
    {
        get => Current.KeepBackups;
        set
        {
            Current.KeepBackups = value;
            Save();
        }
    }

    public static DialogResult Show(OpenFileDialog dialog, IWin32Window owner, bool texture = false)
    {
        dialog.InitialDirectory = Current.Resolve(texture, dialog.InitialDirectory);
        dialog.RestoreDirectory = true;
        var result = dialog.ShowDialog(owner);
        if (result != DialogResult.OK) return result;
        if (texture) Current.Texture = Path.GetDirectoryName(dialog.FileName);
        else Current.Model = Path.GetDirectoryName(dialog.FileName);
        Save();
        return result;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the selection for this session if settings cannot be written.
            System.Diagnostics.Trace.WriteLine($"Could not save clothing preferences: {ex.Message}");
        }
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
