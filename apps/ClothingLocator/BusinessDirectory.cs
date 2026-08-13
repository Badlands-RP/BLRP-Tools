using System.Net.Http.Headers;
using System.Text.Json;

namespace BLRP.ClothingLocator;

internal static class BusinessDirectory
{
    private const string DefaultUrl = "https://panel.badlandsrp.com/api/businesses-list";
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BLRP Clothing Utility",
        "businesses.json");

    public static async Task<IReadOnlyList<string>> RefreshAsync()
    {
        string url = Environment.GetEnvironmentVariable("BLRP_PANEL_BUSINESSES_URL")?.Trim() ?? DefaultUrl;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Panel business refresh failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        IReadOnlyList<string> names = Normalize(
            JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync()) ?? []);
        if (names.Count == 0)
        {
            throw new InvalidOperationException("The Panel returned no business names.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(names));
        return names;
    }

    public static IReadOnlyList<string> LoadCached()
    {
        if (!File.Exists(CachePath))
        {
            return [];
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<string[]>(File.ReadAllText(CachePath)) ?? []);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<string> Normalize(IEnumerable<string> names) => names
        .Select(name => name.Trim())
        .Where(name => name.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
