using System.Text.Json;

namespace Badlands.LiveryTool;

internal sealed record ToolSettings(string RepoRoot, string ModkitMasterListPath, bool CreateBackups, string GtaFolder = "",
    string AccessGroup = "", string Permission = "", bool FitImage = false, Dictionary<string, VehicleChoice>? VehicleChoices = null)
{
    public static ToolSettings Default => new(
        Paths.DefaultRepoRoot,
        string.Empty,
        CreateBackups: false,
        GtaFolder: string.Empty);
}

internal sealed record VehicleChoice(string KitPath, string KitName, string TemplatePath, string Texture);

internal static class ToolSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BadlandsRP",
        "LiveryTool",
        "settings.json");

    public static ToolSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return ToolSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(SettingsPath)) ?? ToolSettings.Default;
            var repoRoot = string.IsNullOrWhiteSpace(settings.RepoRoot) ? ToolSettings.Default.RepoRoot : settings.RepoRoot;
            var modkitMasterListPath = settings.ModkitMasterListPath ?? string.Empty;

            return settings with
            {
                RepoRoot = repoRoot,
                ModkitMasterListPath = modkitMasterListPath,
                GtaFolder = settings.GtaFolder ?? string.Empty,
                AccessGroup = settings.AccessGroup ?? string.Empty,
                Permission = settings.Permission ?? string.Empty,
            };
        }
        catch
        {
            return ToolSettings.Default;
        }
    }

    public static void Save(ToolSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
