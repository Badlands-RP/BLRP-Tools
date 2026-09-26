using CodeWalker.GameFiles;
using SharpDX;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace BLRP.NavMesh;

public static class SelfTest
{
    public static void RunDesktop(string project, string screenshot)
    {
        ApplicationConfiguration.Initialize();
        string temporary = Path.Combine(Path.GetTempPath(), "blrp-navmesh-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        Exception? failure = null;
        try
        {
            var original = BakeSettings.Load(project);
            original.OutputDirectory = Path.Combine(temporary, "results");
            using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
            form.ApplySettings(original);
            form.Shown += async (_, _) =>
            {
                try
                {
                    string saved = Path.Combine(temporary, "project.json");
                    form.SaveForTest(saved);
                    var reloaded = BakeSettings.Load(saved);
                    Require(original.ResourceRoots.SequenceEqual(reloaded.ResourceRoots) && original.Ymaps.SequenceEqual(reloaded.Ymaps) &&
                        original.Min.SequenceEqual(reloaded.Min) && original.Max.SequenceEqual(reloaded.Max) && original.GameBuild == reloaded.GameBuild,
                        "desktop project round-trip preserves inputs and bounds");
                    Require(original.EntitySets.Count == reloaded.EntitySets.Count && original.PolygonFlags.SequenceEqual(reloaded.PolygonFlags) &&
                        original.Collision.Length == reloaded.Collision.Length, "desktop project round-trip preserves advanced settings");
                    form.LoadProject(saved);
                    await form.GenerateForTest();
                    Require(form.LatestOutput != null && File.Exists(Path.Combine(form.LatestOutput, "report.json")), "desktop generates a retained report");
                    using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(form.LatestOutput!, "report.json")));
                    Require(report.RootElement.GetProperty("status").GetString() == "diagnostic-only", "desktop preview completes");
                    Require(form.PreviewPolygonCount > 0, "desktop displays generated polygons");
                    Require(form.IssueCount == report.RootElement.GetProperty("issues").GetArrayLength(), "desktop displays unresolved inputs");
                    var details = MapSelection.Read(original.ResourceRoots, original.Ymaps);
                    Require(float.IsFinite(details.Min.X) && details.Min.X < details.Max.X && details.Min.Y < details.Max.Y,
                        "map picker reads world bounds from selected custom archetypes");
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
                    form.CaptureTabsForTest(screenshot);
                    using var guide = new HelpForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
                    guide.Show(form);
                    Require(guide.TopicCount == 8 && guide.CurrentArticle.Contains("GENERATE PREVIEW"), "embedded help opens at the quick start");
                    for (int i = 0; i < guide.TopicCount; i++)
                    {
                        guide.SelectTopic(i);
                        Require(guide.CurrentArticle.Length > 200, "help topic has instructions");
                    }
                    Require(guide.CurrentArticle.Contains("Unreadable or escrowed asset"), "help includes bake troubleshooting");
                    guide.SelectTopic(0);
                    using var helpImage = new Bitmap(guide.Width, guide.Height);
                    guide.DrawToBitmap(helpImage, new Rectangle(Point.Empty, helpImage.Size));
                    helpImage.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshot))!, Path.GetFileNameWithoutExtension(screenshot) + "-help.png"));
                    guide.Close();
                }
                catch (Exception e) { failure = e; }
                finally { form.Close(); }
            };
            Application.Run(form);
            if (failure != null) throw new InvalidOperationException("Desktop workflow check failed.", failure);
            Console.WriteLine("PASS: desktop project loading/saving, background generation, preview and issue report.");
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException("Self-test: " + message); }
    private static Vector3[] Quad(float x, float y, float width, float depth, float z) =>
        [new(x, y, z), new(x + width, y, z), new(x + width, y + depth, z), new(x, y + depth, z)];
    private static void Triangulate(List<Vector3[]> triangles, Vector3[] quad)
    { triangles.Add([quad[0], quad[1], quad[2]]); triangles.Add([quad[0], quad[2], quad[3]]); }

    public static void Run()
    {
        var settings = new BakeSettings { GameBuild = 3095, Min = [0, 0, -1], Max = [500, 30, 8] };
        var quantization = new QuantizationMetrics();
        var wide = NavMeshCompiler.TilePolygons([Quad(10, 10, 450, 2, 5)], settings, quantization);
        Require(quantization.MaxXYShiftMetres < 0.0017 && quantization.CollapsedPolygons == 0, "quantization displacement reported");
        var tiles = NavMeshCompiler.CreateTiles(wide);
        NavMeshCompiler.Connect(tiles);
        var bytes = NavMeshCompiler.SaveAndVerify(tiles);
        Require(bytes.Count == 4, "450m polygon must cover all four cells.");
        Require(wide.Sum(p => p.Edges.Count(e => e.Poly1 != null)) == 6, "multi-cell reciprocal links");

        var junction = NavMeshCompiler.TilePolygons([Quad(10, 10, 4, 4, 0), Quad(14, 10, 4, 2, 0), Quad(14, 12, 4, 2, 0)], settings);
        tiles = NavMeshCompiler.CreateTiles(junction); NavMeshCompiler.Connect(tiles); NavMeshCompiler.SaveAndVerify(tiles);
        Require(junction[0].Vertices.Length == 5, "T-junction must split the long edge.");
        Require(junction.Sum(p => p.Edges.Count(e => e.Poly1 != null)) == 6, "T-junction connectivity");

        settings.Min = [0, 0, -1]; settings.Max = [20, 12, 8];
        var collision = new List<Vector3[]>();
        Triangulate(collision, Quad(-2, -2, 24, 16, 0));
        Triangulate(collision, Quad(2, 2, 5, 5, 4)); // stacked, disconnected upper floor
        Triangulate(collision, [new(10, -2, 0), new(10, 14, 0), new(10, 14, 3), new(10, -2, 3)]);
        var baked = RecastBake.Build(collision, settings);
        Require(baked.Any(p => p.Average(v => v.Z) > 3.9), "stacked upper floor retained");
        Require(baked.Any(p => p.Average(v => v.Z) < 0.2), "lower floor retained");
        Require(!baked.Any(p => p.Min(v => v.X) < 9.9 && p.Max(v => v.X) > 10.1), "wall blocks polygons");
        tiles = NavMeshCompiler.CreateTiles(NavMeshCompiler.TilePolygons(baked, settings));
        NavMeshCompiler.Connect(tiles); NavMeshCompiler.SaveAndVerify(tiles);
        var left = tiles.Values.SelectMany(t => t.Polys).First(p => p.Position.X < 5 && p.Position.Z < 1);
        var reachable = Reachable(left);
        Require(!reachable.Any(p => p.Position.X > 11), "wall blocks graph traversal");
        Require(!reachable.Any(p => p.Position.Z > 3), "stacked floors must not connect");

        collision.Clear(); Triangulate(collision, Quad(-2, -2, 24, 16, 0));
        var ceiling = Quad(2, 2, 5, 5, 1); Array.Reverse(ceiling); Triangulate(collision, ceiling);
        baked = RecastBake.Build(collision, settings);
        Require(!baked.Any(p => p.Average(v => v.X) is > 3 and < 6 && p.Average(v => v.Y) is > 3 and < 6), "low headroom filtered");
        settings.Min = [140, 0, -2]; settings.Max = [160, 12, 8];
        collision.Clear();
        Triangulate(collision, Quad(138, -2, 24, 16, 0).Select(v => new Vector3(v.X, v.Y, (v.X - 138) * 0.1f)).ToArray());
        baked = RecastBake.Build(collision, settings);
        Require(baked.SelectMany(p => p).All(v => Math.Abs(v.Z - (v.X - 138) * 0.1f) < 0.12f), "ramp heights retained");
        tiles = NavMeshCompiler.CreateTiles(NavMeshCompiler.TilePolygons(baked, settings));
        NavMeshCompiler.Connect(tiles); NavMeshCompiler.SaveAndVerify(tiles);
        Require(tiles.Count == 2 && Reachable(tiles.Values.First().Polys[0]).Any(p => p.Position.X > 150), "sloped tile seam connects");
        Require(Math.Abs(tiles.Values.SelectMany(t => t.Polys).Sum(p => Geometry.SignedArea(p.Vertices)) - 240) < 0.1,
            "ramp detail mesh preserves coverage");
        Require(Geometry.SubtractBox(Quad(0, 0, 10, 10, 0), new(2, 2, 0), new(8, 8, 1)).Sum(p => Geometry.SignedArea(p)) == 64,
            "edit exactly on Z boundary does not duplicate the surface");
        bool rejectedConcave = false;
        try { NavMeshCompiler.TilePolygons([[new(0, 0, 0), new(4, 0, 0), new(2, 1, 0), new(4, 4, 0), new(0, 4, 0)]], settings); }
        catch (InvalidDataException) { rejectedConcave = true; }
        Require(rejectedConcave, "concave navigation input rejected");
        // A transformed box must have outward normals, especially its walkable top.
        var scene = new CollisionScene(settings);
        scene.AddBounds(new BoundBox { BoxMin = new(-1, -1, -1), BoxMax = new(1, 1, 1), Transform = Matrix.Identity },
            Matrix.RotationZ(0.4f) * Matrix.Translation(5, 6, 7), "synthetic box");
        Require(scene.Triangles.Count == 12 && scene.Triangles.All(t => Vector3.Dot(Vector3.Cross(t[1] - t[0], t[2] - t[0]),
            (t[0] + t[1] + t[2]) / 3 - new Vector3(5, 6, 7)) > 0), "transformed box winding");

        // Retain the full surrounding neighborhood while replacing a small central patch.
        string temporary = Path.Combine(Path.GetTempPath(), "blrp-navmesh-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var originals = new List<Vector3[]>();
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) originals.Add(Quad(x * 150, y * 150, 150, 150, 0));
            var originalTiles = NavMeshCompiler.CreateTiles(NavMeshCompiler.TilePolygons(originals, settings));
            NavMeshCompiler.Connect(originalTiles);
            originalTiles[3939].Points = [new YnvPoint { Position = new(-100, -100, 0), Type = 1, Angle = 12 }];
            originalTiles[3939].Portals = [new YnvPortal { AreaIDFrom = 3939, AreaIDTo = 3939,
                PolyIDFrom1 = 0, PolyIDFrom2 = 0, PolyIDTo1 = 0, PolyIDTo2 = 0, Type = 1,
                PositionFrom = new(-110, -110, 0), PositionTo = new(-108, -110, 0) }];
            originalTiles[3939].Polys[0].PortalLinks = [0];
            var originalsBytes = NavMeshCompiler.SaveAndVerify(originalTiles);
            var manifest = new BaselineManifest { GameBuild = 3095, Source = "Synthetic test", ValidatedForBuild = true };
            foreach (var pair in originalsBytes)
            {
                string name = NavMeshCompiler.TileName(pair.Key); string file = Path.Combine(temporary, name);
                File.WriteAllBytes(file, pair.Value); manifest.Files[name] = CollisionScene.Hash(file);
            }
            File.WriteAllText(Path.Combine(temporary, "baseline.json"), System.Text.Json.JsonSerializer.Serialize(manifest, BakeSettings.Json));
            settings.BaselineDirectory = temporary; settings.Min = [10, 10, -1]; settings.Max = [20, 20, 1];
            var baseline = Baseline.Load(settings, scene);
            var replacement = NavMeshCompiler.TilePolygons([Quad(10, 10, 10, 10, 0)], settings);
            tiles = baseline.Compose(replacement, settings);
            NavMeshCompiler.Connect(tiles, baseline); NavMeshCompiler.SaveAndVerify(tiles, baseline);
            Require(Math.Abs(tiles.Values.SelectMany(t => t.Polys).Sum(p => Geometry.SignedArea(p.Vertices)) - 9 * 150 * 150) < 0.01,
                "composition preserves neighborhood coverage without overlap");
            Require(Reachable(replacement[0]).Count == tiles.Values.Sum(t => t.Polys.Count), "replacement connects to preserved neighborhood");
            Require(tiles[3939].Points.Count == 1, "unrelated navigation point retained");
            Require(tiles[3939].Portals.Count == 1 && tiles[3939].Polys[0].PortalLinks.Length == 1, "unrelated portal retained");

            baseline = Baseline.Load(settings, scene);
            foreach (var poly in baseline.Tiles.Values.SelectMany(t => t.Polys))
                foreach (var edge in poly.Edges)
                    if (poly.AreaID == 4040 || edge.AreaID1 == 4040)
                    {
                        edge.Poly1 = edge.Poly2 = null;
                        edge.AreaID1 = edge.AreaID2 = edge.PolyID1 = edge.PolyID2 = NavMeshCompiler.NoLink;
                    }
            // Refresh the captured edge metadata for this deliberately disconnected fixture.
            foreach (var poly in baseline.Tiles.Values.SelectMany(t => t.Polys))
                baseline.OriginalEdges[poly] = poly.Edges.Select(NavMeshCompiler.CopyEdge).ToArray();
            replacement = NavMeshCompiler.TilePolygons([Quad(10, 10, 10, 10, 0)], settings);
            tiles = baseline.Compose(replacement, settings); NavMeshCompiler.Connect(tiles, baseline);
            Require(Reachable(replacement[0]).All(p => p.AreaID == 4040), "blocked vanilla boundary stays blocked");
        }
        finally { Directory.Delete(temporary, true); }
        Console.WriteLine("PASS: clipping, links, native round-trip, T-junctions, walls, stacked floors, headroom, ramps, transforms and baseline preservation.");
    }

    public static HashSet<YnvPoly> Reachable(YnvPoly start)
    {
        var visited = new HashSet<YnvPoly> { start }; var queue = new Queue<YnvPoly>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var poly))
            foreach (var edge in poly.Edges)
                if (edge.Poly1 != null && visited.Add(edge.Poly1)) queue.Enqueue(edge.Poly1);
        return visited;
    }
}
