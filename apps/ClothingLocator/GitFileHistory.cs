namespace BLRP.ClothingLocator;

internal sealed record GitFileChange(string Hash, string Author, string Date, string Subject)
{
    public string Tooltip => $"{Author} / {Date} / {Hash}\n{Subject}";
}

internal static class GitFileHistory
{
    public static string? Describe(string rootPath, string filePath)
    {
        GitFileChange? change = RunSingle(rootPath, filePath);
        return change == null ? null : $"LAST CHANGE: {change.Author} / {change.Date} / {change.Hash}";
    }

    public static IReadOnlyDictionary<string, GitFileChange> BuildIndex(string rootPath)
    {
        try
        {
            string output = Run(rootPath, ["log", "--format=%x1e%h%x1f%an%x1f%ad%x1f%s", "--date=short", "--name-only", "--", ":(glob)**/*.ydd"], 15_000);
            var result = new Dictionary<string, GitFileChange>(StringComparer.OrdinalIgnoreCase);
            GitFileChange? current = null;
            foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '\x1e')
                {
                    string[] fields = line[1..].Split('\x1f');
                    current = fields.Length >= 4 ? new GitFileChange(fields[0], fields[1], fields[2], fields[3]) : null;
                    continue;
                }
                if (current == null) continue;
                string path = Path.GetFullPath(Path.Combine(rootPath, line.Replace('/', Path.DirectorySeparatorChar)));
                result.TryAdd(path, current);
            }
            return result;
        }
        catch { return new Dictionary<string, GitFileChange>(StringComparer.OrdinalIgnoreCase); }
    }

    internal static bool SelfTest(string rootPath, string filePath)
    {
        IReadOnlyDictionary<string, GitFileChange> index = BuildIndex(rootPath);
        return index.TryGetValue(Path.GetFullPath(filePath), out GitFileChange? change) &&
            !string.IsNullOrWhiteSpace(change.Author) && Describe(rootPath, filePath) != null;
    }

    private static GitFileChange? RunSingle(string rootPath, string filePath)
    {
        try
        {
            string relative = Path.GetRelativePath(rootPath, filePath);
            if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
            string value = Run(rootPath, ["log", "-1", "--format=%h%x1f%an%x1f%ad%x1f%s", "--date=short", "--", relative], 2_000).Trim();
            string[] fields = value.Split('\x1f');
            return fields.Length >= 4 ? new GitFileChange(fields[0], fields[1], fields[2], fields[3]) : null;
        }
        catch { return null; }
    }

    private static string Run(string rootPath, IReadOnlyList<string> arguments, int timeout)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(rootPath);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Unable to start Git.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(timeout)) { process.Kill(true); throw new TimeoutException("Git history lookup timed out."); }
        return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : string.Empty;
    }
}
