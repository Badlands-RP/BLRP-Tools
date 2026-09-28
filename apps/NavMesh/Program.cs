using System.Globalization;
using System.Text.Json;
using CodeWalker.GameFiles;

namespace BLRP.NavMesh;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string shared = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "shared", name.Name + ".dll"));
            return File.Exists(shared) ? System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(shared) : null;
        };
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0 || (args.Length == 1 && File.Exists(args[0])))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args.FirstOrDefault()));
            return 0;
        }
        try
        {
            if (args.Length == 3 && args[0] == "--ui-self-test")
            { SelfTest.RunDesktop(args[1], args[2]); return 0; }
            if (args.Length == 1 && args[0] == "self-test") { SelfTest.Run(); return 0; }
            if (args.Length == 3 && args[0] == "capture-baseline")
            {
                var settings = BakeSettings.Load(args[1]);
                var source = new GameSource(settings, new CollisionScene(settings));
                source.CaptureBaseline(settings, Path.GetFullPath(args[2])); return 0;
            }
            if (args.Length is 2 or 3 && args[0] == "bake" && (args.Length == 2 || args[2] == "--diagnostic"))
                return Bake(args[1], args.Length == 3);
            Console.WriteLine("BLRP NavMesh\n  bake <settings.json> [--diagnostic]\n  capture-baseline <settings.json> <empty-directory>\n  self-test\n\nNormal bakes require a matching baseline and complete collision inputs.\nDiagnostic bakes write inspection assets only. No files are installed automatically.");
            return args.Length == 0 ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.ToString());
            return 1;
        }
    }

    public static int Bake(string settingsPath, bool diagnostic)
    {
        var settings = BakeSettings.Load(settingsPath);
        return Bake(settings, diagnostic);
    }

    public static int Bake(BakeSettings settings, bool diagnostic, CancellationToken cancellation = default)
    {
        settings.Validate();
        string output = settings.OutputDirectory;
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Output directory is not empty. Choose a new directory to retain the previous baseline and results.");
        Directory.CreateDirectory(output);
        var scene = new CollisionScene(settings);
        Dictionary<int, byte[]> bytes = [];
        string status = "failed";
        Exception? failure = null;
        var quantization = new QuantizationMetrics();
        string? bakeId = null;
        object? connectivity = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            Console.WriteLine("Loading explicit scene inputs...");
            scene.Load();
            cancellation.ThrowIfCancellationRequested();
            Console.WriteLine($"Collision: {scene.Triangles.Count} triangles; {scene.Issues.Distinct().Count()} unresolved items.");
            Geometry.WriteObj(Path.Combine(output, "collision.obj"), scene.Triangles);
            if (!diagnostic && !settings.AllowWarnings && scene.Issues.Count != 0)
                throw new InvalidDataException("Collision inputs are incomplete. Resolve the warnings or enable Allow warnings under Export to build a test resource.");
            Console.WriteLine("Generating walkable surfaces with Recast...");
            var polygons = RecastBake.Build(scene.Triangles, settings);
            cancellation.ThrowIfCancellationRequested();
            var generated = NavMeshCompiler.TilePolygons(polygons, settings, quantization);
            Geometry.WriteObj(Path.Combine(output, "generated-navmesh.obj"), generated.Select(p => p.Vertices));
            Dictionary<int, YnvFile> tiles;
            Baseline? baseline = null;
            if (diagnostic && settings.BaselineDirectory.Length == 0) tiles = NavMeshCompiler.CreateTiles(generated);
            else
            {
                if (settings.BaselineDirectory.Length == 0) throw new InvalidDataException("A build-matched baselineDirectory is required for a streamable bake.");
                baseline = Baseline.Load(settings, scene, diagnostic || settings.AllowWarnings);
                tiles = baseline.Compose(generated, settings);
            }
            Console.WriteLine("Connecting polygons and checking native output...");
            NavMeshCompiler.Connect(tiles, baseline);
            cancellation.ThrowIfCancellationRequested();
            var unvisited = tiles.Values.SelectMany(t => t.Polys).ToHashSet();
            var generatedSet = generated.ToHashSet();
            int components = 0, isolatedGeneratedComponents = 0;
            while (unvisited.Count != 0)
            {
                var queue = new Queue<YnvPoly>(); var start = unvisited.First(); queue.Enqueue(start); unvisited.Remove(start);
                bool hasGenerated = false, hasOriginal = false;
                while (queue.TryDequeue(out var poly))
                {
                    hasGenerated |= generatedSet.Contains(poly);
                    hasOriginal |= baseline?.Origins.ContainsKey(poly) == true;
                    foreach (var edge in poly.Edges)
                        foreach (var next in new[] { edge.Poly1, edge.Poly2 })
                            if (next != null && unvisited.Remove(next)) queue.Enqueue(next);
                }
                components++;
                if (hasGenerated && !hasOriginal) isolatedGeneratedComponents++;
            }
            connectivity = new { components, isolatedGeneratedComponents,
                polygons = tiles.Values.Sum(t => t.Polys.Count),
                directedLinks = tiles.Values.Sum(t => t.Polys.Sum(p => p.Edges.Count(e => e.Poly1 != null))) };
            if (baseline != null && isolatedGeneratedComponents != 0 && !settings.AllowIsolatedComponents)
            {
                scene.Issues.Add($"{isolatedGeneratedComponents} generated components do not connect to preserved baseline navigation. Inspect 3D seam alignment or explicitly allow intended islands.");
                if (!diagnostic && !settings.AllowWarnings) throw new InvalidDataException("Generated navigation does not connect to the preserved neighborhood. See report.json.");
            }
            bytes = NavMeshCompiler.SaveAndVerify(tiles, baseline);
            cancellation.ThrowIfCancellationRequested();
            bakeId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.OrderBy(p => p.Key)
                .SelectMany(p => System.Security.Cryptography.SHA256.HashData(p.Value)).ToArray()));
            var names = tiles.Keys.Select(NavMeshCompiler.TileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var conflicts = settings.ConflictScanRoots.Concat(settings.ResourceRoots).Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.ynv", SearchOption.AllDirectories))
                .Where(file => names.Contains(Path.GetFileName(file))).ToArray();
            if (conflicts.Length > 0)
            {
                scene.Issues.AddRange(conflicts.Select(p => "Existing streamed tile requires composition/deconfliction: " + p));
                if (!diagnostic) throw new InvalidDataException("Conflicting streamed YNV filenames. See report.json.");
            }
            var nativeDir = Path.Combine(output, diagnostic ? "inspection" : "resource.pending/stream");
            var xmlDir = Path.Combine(output, "source");
            Directory.CreateDirectory(nativeDir); Directory.CreateDirectory(xmlDir);
            foreach (var item in bytes)
            {
                cancellation.ThrowIfCancellationRequested();
                string filename = NavMeshCompiler.TileName(item.Key);
                File.WriteAllBytes(Path.Combine(nativeDir, filename), item.Value);
                var ynv = new YnvFile(); ynv.Load(item.Value);
                File.WriteAllText(Path.Combine(xmlDir, filename + ".xml"), YnvXml.GetXml(ynv));
            }
            if (!diagnostic)
            {
                cancellation.ThrowIfCancellationRequested();
                File.WriteAllText(Path.Combine(output, "resource.pending", "fxmanifest.lua"),
                    "fx_version 'cerulean'\ngame 'gta5'\nthis_is_a_map 'yes'\n" +
                    $"navmesh_bake '{bakeId}'\nnavmesh_target_build '{settings.GameBuild}'\n" +
                    (settings.AllowWarnings ? "navmesh_test_resource 'yes'\n" : "") +
                    $"dependency '/gameBuild:{settings.GameBuild}'\n" +
                    string.Concat(settings.Dependencies.Select(s => $"dependency '{s}'\n")));
                if (settings.AllowWarnings)
                    File.WriteAllText(Path.Combine(output, "resource.pending", "BUILD-WARNINGS.txt"),
                        "TEST RESOURCE — input warnings were bypassed. Missing/unreadable collision is omitted; conflicting assets use the first selected copy.\n" +
                        "Build/source verification may be incomplete. Test NPC routes and doors in FiveM.\n\n" + string.Join("\n", scene.Issues.Distinct().Order()));
                Directory.Move(Path.Combine(output, "resource.pending"), Path.Combine(output, "resource"));
            }
            status = diagnostic ? "diagnostic-only" : settings.AllowWarnings ? "test-resource-with-warnings" : "awaiting-fivem-validation";
        }
        catch (Exception e) { failure = e; if (e is OperationCanceledException) status = "cancelled"; }
        finally
        {
            var report = new
            {
                status, diagnostic, warningsBypassed = !diagnostic && settings.AllowWarnings, bakeId, settings.GameBuild, generatedAtUtc = DateTime.UtcNow,
                codeWalkerSha256 = CollisionScene.Hash(typeof(YnvFile).Assembly.Location),
                recastVersion = typeof(DotRecast.Recast.RcBuilder).Assembly.GetName().Version?.ToString(),
                settings, quantization, connectivity, collisionTriangleCount = scene.Triangles.Count,
                curvedCollisionMaxErrorMetres = CollisionPrimitives.MaxWorldError, inputs = scene.Inputs,
                placements = scene.Placements, masks = scene.Masks, exclusions = scene.Exclusions.Distinct().ToArray(),
                issues = scene.Issues.Distinct().Order().ToArray(), error = failure?.Message,
                outputHashes = bytes.ToDictionary(p => NavMeshCompiler.TileName(p.Key), p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Value))),
                runtime = "Not tested. NPC traversal, tile transitions, doors and streaming must be verified in FiveM."
            };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, BakeSettings.Json));
            File.WriteAllText(Path.Combine(output, "bake.json"), JsonSerializer.Serialize(settings, BakeSettings.Json));
        }
        if (failure != null) throw new InvalidOperationException($"Bake failed; retained diagnostics in {output}", failure);
        Console.WriteLine($"{status}: {output}");
        return 0;
    }
}
