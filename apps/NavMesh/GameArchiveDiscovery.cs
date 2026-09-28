using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using CodeWalker.GameFiles;

namespace BLRP.NavMesh;

internal static class GameArchiveDiscovery
{
    // Update identities from citizenfx/fivem code/client/launcher/GameCache.cpp, retrieved 2026-09-29.
    // Only the requested build is considered. This identifies updates; it does not certify base/DLC files.
    internal static Dictionary<string, string>? UpdateHashes(int build)
    {
        using var stream = typeof(GameArchiveDiscovery).Assembly.GetManifestResourceStream("BLRP.NavMesh.GameBuildUpdates.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!.GetValueOrDefault(build.ToString());
    }

    internal static GameSourceManifest Discover(string folder, int build, CancellationToken cancellation = default)
    {
        var result = GameSource.BaseGameSelection(folder, build);
        var problems = new List<string>();
        var hashes = UpdateHashes(build);
        var updates = new List<ArchiveInput>();
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app", "data", "game-storage");
        if (hashes != null)
        foreach (var (logical, expected) in hashes)
        {
            cancellation.ThrowIfCancellationRequested();
            string cacheName = logical.Replace('/', '+') + "_" + expected;
            var paths = new[] { Path.Combine(cache, "override+" + cacheName), Path.Combine(cache, cacheName), Path.Combine(folder, logical) };
            string? selected = null;
            foreach (string path in paths.Where(File.Exists))
            {
                Console.WriteLine($"Checking build {build}: {Path.GetFileName(path)}");
                using var input = File.OpenRead(path);
                string actual = Convert.ToHexString(SHA1.HashData(input));
                if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) { selected = path; break; }
            }
            if (selected != null) updates.Add(new ArchiveInput { Path = selected, LogicalPath = logical, Sha256 = CollisionScene.Hash(selected) });
            else if (logical == "update/update.rpf")
                problems.Add($"No local {logical} matches build {build}. Connect FiveM to a server using that build to populate its cache, or import a matching source set.");
            // FiveM can use a newer executable's update2.rpf with the requested build's update.rpf.
            // Do not require an old update2, or guess which newer one is active. Mount verification stays explicit.
        }
        else problems.Add($"Build {build} is not in this tool's update catalog. Import a matching source set; a newer installed update was not substituted.");

        var update = updates.FirstOrDefault(a => a.LogicalPath == "update/update.rpf");
        if (update == null)
        {
            result.DiscoveryIssues = problems.ToArray();
            return result;
        }
        GTA5Keys.LoadFromPath(folder, false, null);
        var patch = Open(update.Path, update.LogicalPath);
        XmlDocument? list = ReadXml(patch, "common/data/dlclist.xml");
        if (list == null) throw new InvalidDataException("The matching update has no readable DLC list.");

        // CodeWalker's GameFileCache orders packs using setup2.xml. Updates supply patched setup files.
        var packs = new List<(int Order, ArchiveInput[] Archives)>();
        foreach (XmlNode item in list.SelectNodes("//Paths/Item")!)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = item.InnerText.Trim().Replace('\\', '/').ToLowerInvariant();
            if (!path.StartsWith("dlcpacks:/")) continue; // platform:/ packs are already nested in the base archives.
            string name = path["dlcpacks:/".Length..].Trim('/');
            if (name.Length == 0 || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Contains(".."))
                throw new InvalidDataException("Invalid DLC path: " + path);
            string relative = "update/x64/dlcpacks/" + name + "/dlc.rpf";
            string physical = Path.Combine(folder, relative);
            if (!File.Exists(physical))
            {
                problems.Add($"DLC listed for build {build} is not installed: {name}.");
                continue;
            }
            Console.WriteLine("Discovering DLC: " + name);
            var archive = Open(physical, relative);
            var setupXml = ReadXml(patch, "dlc_patch/" + name + "/setup2.xml") ?? ReadXml(archive, "setup2.xml");
            if (setupXml == null) { problems.Add("DLC has no readable setup2.xml: " + name); continue; }
            var setup = new DlcSetupFile(); setup.Load(setupXml);
            var files = new List<ArchiveInput> { new() { Path = physical, LogicalPath = relative } };
            for (int sub = 1; sub <= setup.subPackCount; sub++)
            {
                string subRelative = "update/x64/dlcpacks/" + name + $"/dlc{sub}.rpf";
                string subPhysical = Path.Combine(folder, subRelative);
                if (File.Exists(subPhysical)) files.Add(new ArchiveInput { Path = subPhysical, LogicalPath = subRelative });
                else problems.Add("DLC subpack is missing: " + subRelative);
            }
            packs.Add((setup.order, files.ToArray()));
        }
        result.Archives = result.Archives.Concat(packs.OrderBy(p => p.Order).SelectMany(p => p.Archives)).Concat(updates).ToArray();
        result.Source = $"Automatically discovered base archives, DLC from build {build}'s dlclist/setup order, and SHA1-matched update archives. Base/DLC identities and mount overrides remain unverified.";
        result.DiscoveryIssues = problems.ToArray();
        return result;
    }

    private static RpfFile Open(string physical, string logical)
    {
        var archive = new RpfFile(physical, logical) { Name = Path.GetFileName(logical), NameLower = Path.GetFileName(logical).ToLowerInvariant() };
        archive.ScanStructure(_ => { }, message => throw new InvalidDataException(message));
        return archive;
    }

    private static XmlDocument? ReadXml(RpfFile archive, string relative)
    {
        string path = (archive.Path + "/" + relative).Replace('\\', '/');
        var entry = archive.AllEntries.OfType<RpfFileEntry>().FirstOrDefault(e => e.Path.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(Encoding.UTF8.GetString(archive.ExtractFile(entry)).TrimStart('\uFEFF'));
        return document;
    }
}
