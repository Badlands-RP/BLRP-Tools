using System.Text.Json;

namespace BLRP.NavMesh;

internal sealed class UserPreferences
{
    public string GtaFolder { get; set; } = "";
    public int GameBuild { get; set; } = 3095;
    public string ResultsFolder { get; set; } = "";

    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BadlandsRP", "NavMesh", "settings.json");

    internal static UserPreferences Load(string path)
    {
        try { return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static string ReadLiveryGameFolder()
    {
        // LiveryTool already remembers this choice; read it without changing that tool's settings.
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BadlandsRP", "LiveryTool", "settings.json");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("GtaFolder", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return ""; }
    }
}
