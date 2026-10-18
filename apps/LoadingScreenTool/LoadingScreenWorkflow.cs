using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace BLRP.LoadingScreenTool;

internal static class LoadingScreenWorkflow
{
    private const string LedgerName = ".blrp-loading-screen.json";
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed class Ledger
    {
        public Dictionary<string, string> ProcessedSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed record ProcessResult(int Existing, int Added, int Total);
    internal sealed record ExportResult(int Copied, int Removed);

    internal static ProcessResult Process(string projectFolder)
    {
        string folder = ExistingFolder(projectFolder);
        var numbered = NumberedFiles(folder);
        Ledger ledger = ReadLedger(folder);
        var additions = new List<(string Path, string Hash)>();
        foreach (string path in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (TryNumber(Path.GetFileName(path), out _) || !ImageExtensions.Contains(Path.GetExtension(path))) continue;
            string hash = Hash(path);
            if (!ledger.ProcessedSources.TryGetValue(Path.GetFileName(path), out string? prior) || prior != hash)
                additions.Add((path, hash));
        }

        if (numbered.Count + additions.Count == 0) return new ProcessResult(0, 0, 0);
        string work = Path.Combine(folder, ".blrp-loading-work-" + Guid.NewGuid().ToString("N"));
        string staged = Path.Combine(work, "staged");
        string backup = Path.Combine(work, "backup");
        Directory.CreateDirectory(staged);
        Directory.CreateDirectory(backup);
        try
        {
            int index = 0;
            foreach (string path in numbered)
                File.Copy(path, Path.Combine(staged, (++index) + ".webp"));
            foreach (var (path, _) in additions)
            {
                using SKBitmap image = SKBitmap.Decode(path) ?? throw new InvalidDataException("Could not read image: " + path);
                using SKImage surface = SKImage.FromBitmap(image);
                using SKData encoded = surface.Encode(SKEncodedImageFormat.Webp, 90) ??
                    throw new InvalidDataException("Could not convert image: " + path);
                using FileStream output = File.Create(Path.Combine(staged, (++index) + ".webp"));
                encoded.SaveTo(output);
            }

            foreach (var (path, hash) in additions) ledger.ProcessedSources[Path.GetFileName(path)] = hash;
            ReplaceNumbered(folder, numbered, staged, backup, () => WriteLedger(folder, ledger));
            return new ProcessResult(numbered.Count, additions.Count, index);
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    internal static ExportResult Export(string projectFolder, string activeFolder)
    {
        string source = ExistingFolder(projectFolder);
        string target = ExistingFolder(activeFolder);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The project and active folders must be different.");

        var images = NumberedFiles(source);
        if (images.Count == 0) throw new InvalidOperationException("Process the project folder before exporting; no numbered WebPs were found.");
        if (images.Where((path, index) => !string.Equals(Path.GetFileName(path), (index + 1) + ".webp", StringComparison.OrdinalIgnoreCase)).Any())
            throw new InvalidOperationException("The project numbering has gaps. Process the project folder before exporting.");

        var existing = NumberedFiles(target);
        string work = Path.Combine(target, ".blrp-loading-export-" + Guid.NewGuid().ToString("N"));
        string staged = Path.Combine(work, "staged");
        string backup = Path.Combine(work, "backup");
        Directory.CreateDirectory(staged);
        Directory.CreateDirectory(backup);
        try
        {
            foreach (string path in images) File.Copy(path, Path.Combine(staged, Path.GetFileName(path)));
            ReplaceNumbered(target, existing, staged, backup, () => { });
            return new ExportResult(images.Count, existing.Count - existing.Count(path => images.Any(image =>
                string.Equals(Path.GetFileName(image), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))));
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    internal static IReadOnlyList<string> NumberedFiles(string folder) => Directory.EnumerateFiles(folder)
        .Where(path => TryNumber(Path.GetFileName(path), out _))
        .OrderBy(path => int.Parse(Path.GetFileNameWithoutExtension(path)))
        .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void ReplaceNumbered(string folder, IReadOnlyList<string> current, string staged, string backup, Action commit)
    {
        var installed = new List<string>();
        var moved = new List<(string Backup, string Original)>();
        try
        {
            foreach (string path in current)
            {
                string saved = Path.Combine(backup, Path.GetFileName(path));
                File.Move(path, saved);
                moved.Add((saved, path));
            }
            foreach (string path in Directory.EnumerateFiles(staged).OrderBy(path => int.Parse(Path.GetFileNameWithoutExtension(path))))
            {
                string destination = Path.Combine(folder, Path.GetFileName(path));
                File.Move(path, destination);
                installed.Add(destination);
            }
            commit();
        }
        catch
        {
            foreach (string path in installed) File.Delete(path);
            foreach (var (saved, original) in moved) File.Move(saved, original);
            throw;
        }
    }

    private static bool TryNumber(string name, out int number)
    {
        number = 0;
        return Path.GetExtension(name).Equals(".webp", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(Path.GetFileNameWithoutExtension(name), out number) && number > 0;
    }

    private static string ExistingFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new DirectoryNotFoundException("Choose an existing folder: " + path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Ledger ReadLedger(string folder)
    {
        string path = Path.Combine(folder, LedgerName);
        if (!File.Exists(path)) return new Ledger();
        Ledger ledger = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(path)) ?? new Ledger();
        ledger.ProcessedSources = new Dictionary<string, string>(ledger.ProcessedSources, StringComparer.OrdinalIgnoreCase);
        return ledger;
    }

    private static void WriteLedger(string folder, Ledger ledger)
    {
        string path = Path.Combine(folder, LedgerName);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(ledger, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
