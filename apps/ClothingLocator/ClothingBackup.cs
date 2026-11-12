namespace BLRP.ClothingLocator;

internal sealed class ClothingBackup(string root, bool needsRollback = false) : IDisposable
{
    private readonly bool _keep = ImportDirectories.KeepLocalBackups;
    private readonly Dictionary<string, string?> _originals = new(StringComparer.OrdinalIgnoreCase);
    private string? _directory;
    private bool _recoveryNeeded;

    public string? RetainedDirectory => _keep ? _directory : null;

    public void Save(string path, string? relativePath = null)
    {
        if (!_keep && !needsRollback) return;
        if (!File.Exists(path))
        {
            _originals.Add(path, null);
            return;
        }

        _directory ??= _keep
            ? Path.Combine(root, ".clothing-locator-backups", $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}")
            : Directory.CreateTempSubdirectory("blrp-clothing-rollback-").FullName;
        string copy = Path.Combine(_directory, relativePath ?? Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(path, copy);
        _originals.Add(path, copy);
    }

    public void Restore(string path)
    {
        try
        {
            if (_originals[path] is { } copy) File.Copy(copy, path, true);
            else File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _recoveryNeeded = true;
            throw new IOException($"Could not restore '{path}'. Recovery copies are in '{_directory}'.", ex);
        }
    }

    public void Dispose()
    {
        if (_keep || _recoveryNeeded || _directory == null) return;
        try { Directory.Delete(_directory, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Could not remove clothing rollback copies: {ex.Message}");
        }
    }
}
