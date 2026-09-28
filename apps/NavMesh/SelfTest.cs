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
            using var form = new MainForm(preferencesPath: Path.Combine(temporary, "preferences.json")) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
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
                    await form.ReadMapDetailsForTest();
                    form.SaveForTest(saved);
                    var details = MapSelection.Read(BakeSettings.Load(saved));
                    var fitted = form.ReadSettings();
                    Require(Enumerable.Range(0, 3).All(i => Math.Abs(fitted.Min[i] - (details.Min[i] - 0.5f)) < 0.001f &&
                        Math.Abs(fitted.Max[i] - (details.Max[i] + 0.5f)) < 0.001f), "bounds button fits the selected collision and game sources");
                    await form.GenerateForTest();
                    Require(form.LatestOutput != null && File.Exists(Path.Combine(form.LatestOutput, "report.json")), "desktop generates a retained report");
                    using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(form.LatestOutput!, "report.json")));
                    Require(report.RootElement.GetProperty("status").GetString() == "diagnostic-only", "desktop preview completes");
                    Require(form.PreviewPolygonCount > 0, "desktop displays generated polygons");
                    Require(form.IssueCount == report.RootElement.GetProperty("issues").GetArrayLength(), "desktop displays unresolved inputs");
                    Require(!form.AdvancedVisible && form.IssueGroupCount <= 7, "normal workflow hides advanced controls and groups warnings");
                    Require(form.IssueCount == 0 || !form.ExportEnabled, "incomplete preview cannot enable export");
                    Require(float.IsFinite(details.Min.X) && details.Min.X < details.Max.X && details.Min.Y < details.Max.Y,
                        "map picker reads finite world bounds");
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
                    form.CaptureTabsForTest(screenshot);
                    form.SetAdvancedForTest(true);
                    form.CaptureTabsForTest(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshot))!, Path.GetFileNameWithoutExtension(screenshot) + "-advanced.png"));
                    form.SetAdvancedForTest(false);
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

                    // Exercise the ordinary first-run flow: no project, no game-folder dialog, no coordinates.
                    form.ApplySettings(new BakeSettings { GameBuild = original.GameBuild, Min = [0, 0, -1], Max = [10, 10, 4],
                        OutputDirectory = Path.Combine(temporary, "first-run"), AutoFitArea = true });
                    Require(form.AutomaticArea && !form.PreviewEnabled && !form.ExportEnabled, "empty project guides the user to map selection first");
                    if (GameSource.FindLegacyDirectory() != null) Require(form.GameDetected, "installed GTA is selected automatically");
                    string firstMap = original.Ymaps[0];
                    string firstResource = original.ResourceRoots.First(r => !Path.GetRelativePath(r, firstMap).StartsWith(".."));
                    form.AddResourceForTest(firstResource);
                    form.SelectMapsForTest(original.Ymaps.Where(p => !Path.GetRelativePath(firstResource, p).StartsWith("..")).ToArray());
                    await form.GenerateForTest();
                    Require(form.PreviewPolygonCount > 0 && form.AutomaticArea, "one preview action fits and generates the selected map");
                    if (Path.GetFileName(firstMap) == "hns_josecafe_mrpark_milo_.ymap")
                    {
                        var setup = form.ReadSettings();
                        Require(setup.ResourceRoots.Any(r => Path.GetFileName(r) == "hns_josecafe_base"), "cafe base resource discovered from the MLO owner");
                        Require(setup.Max[0] - setup.Min[0] < 35 && setup.Max[1] - setup.Min[1] < 25, "automatic cafe area stays close to its actual collision");
                    }
                    Require(!form.ExportEnabled, "unreviewed first-run preview does not enable export");
                    form.CaptureTabsForTest(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshot))!, Path.GetFileNameWithoutExtension(screenshot) + "-first-run.png"));
                    form.SaveForTest(saved);
                    var manualProject = BakeSettings.Load(saved);
                    if (manualProject.GameSourceFile.Length > 0)
                    {
                        var source = System.Text.Json.JsonSerializer.Deserialize<GameSourceManifest>(File.ReadAllText(manualProject.GameSourceFile), BakeSettings.Json)!;
                        var manual = GameSource.BaseGameSelection(source.GameDirectory, source.GameBuild);
                        manual.AutoDiscoverDlc = false;
                        File.WriteAllText(manualProject.GameSourceFile, System.Text.Json.JsonSerializer.Serialize(manual, BakeSettings.Json));
                        form.LoadProject(saved); form.SaveForTest(saved);
                        var restored = System.Text.Json.JsonSerializer.Deserialize<GameSourceManifest>(File.ReadAllText(manualProject.GameSourceFile), BakeSettings.Json)!;
                        Require(!restored.AutoDiscoverDlc, "explicit automatic-discovery opt-out survives project reload");
                    }
                }
                catch (Exception e) { failure = e; }
                finally { form.Close(); }
            };
            Application.Run(form);
            if (failure != null) throw new InvalidOperationException("Desktop workflow check failed.", failure);
            Console.WriteLine("PASS: desktop projects, collision/game bounds fitting, background generation, preview, issues and embedded help.");
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
        CheckCollisionPrimitives();
        Require(GameArchiveDiscovery.UpdateHashes(3095)?["update/update.rpf"] == "fd46de4495d32f0533b8b3ae72507b829e8650f3" &&
            GameArchiveDiscovery.UpdateHashes(1) == null, "game discovery requires an exact supported build");
        var boxBounds = MapSelection.WorldBounds(new BoundBox { BoxMin = new(-1, -2, 0), BoxMax = new(1, 2, 3),
            Transform = Matrix.Translation(3, 0, 0) }, Matrix.Scaling(2) * Matrix.RotationZ(MathF.PI / 2) * Matrix.Translation(10, 20, 30));
        Require(Vector3.Distance(boxBounds.Minimum, new(6, 24, 30)) < 0.00001f && Vector3.Distance(boxBounds.Maximum, new(14, 28, 36)) < 0.00001f,
            "collision bounds apply the bound transform, scale and decoded placement exactly once");
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
            string install = Path.Combine(temporary, "synthetic-install"); Directory.CreateDirectory(install);
            foreach (string name in new[] { "GTA5.exe", "common.rpf", "x64b.rpf", "x64a.rpf" }) File.WriteAllBytes(Path.Combine(install, name), []);
            var sourceSet = GameSource.BaseGameSelection(install, 3095);
            Require(!sourceSet.ValidatedForBuild && sourceSet.GameBuild == 3095 &&
                sourceSet.Archives.Select(a => a.LogicalPath).SequenceEqual(new[] { "common.rpf", "x64a.rpf", "x64b.rpf" }) &&
                sourceSet.Archives.All(a => Path.IsPathFullyQualified(a.Path) && a.Sha256.Length == 0),
                "base game selection preserves target build, deterministic order and unverified provenance");
            string preferencesPath = Path.Combine(temporary, "preferences.json");
            new UserPreferences { GtaFolder = install, GameBuild = 3095, ResultsFolder = temporary }.Save(preferencesPath);
            var preferences = UserPreferences.Load(preferencesPath);
            Require(preferences.GtaFolder == install && preferences.ResultsFolder == temporary && GameSource.FindLegacyDirectory(preferences.GtaFolder) == install,
                "remembered game location is restored and preferred without changing build verification");
            var groups = IssueGroup.From(["Escrow-protected test.ydr", "Escrow-protected test.ydr", "Missing owning YTYP for test", "Archive not pinned by SHA256: test", "Another failure"]);
            Require(groups.Length == 4 && groups.Sum(g => g.Messages.Length) == 4 && groups.All(g => g.NextStep.Length > 30),
                "issues are grouped with next steps while retaining every distinct detail");
            string first = Path.Combine(temporary, "first"), second = Path.Combine(temporary, "second");
            Directory.CreateDirectory(first); Directory.CreateDirectory(second);
            byte[] floorBytes = new YbnFile { Bounds = new BoundBox { Type = BoundsType.Box, BoxMin = new(-2, -2, -1), BoxMax = new(12, 12, 0) } }.Save();
            File.WriteAllBytes(Path.Combine(first, "floor.ybn"), floorBytes);
            File.WriteAllBytes(Path.Combine(second, "floor.ybn"), [.. floorBytes, 0]);
            var conflicting = new BakeSettings { GameBuild = 3095, Min = [0, 0, -1], Max = [10, 10, 3], ResourceRoots = [first, second],
                Collision = [new CollisionInput { Path = Path.Combine(first, "floor.ybn") }], OutputDirectory = Path.Combine(temporary, "conflict-preview") };
            Require(Program.Bake(conflicting, true) == 0, "asset conflicts allow a diagnostic preview");
            conflicting.OutputDirectory = Path.Combine(temporary, "conflict-build");
            bool conflictBlocked = false;
            try { Program.Bake(conflicting, false); }
            catch (InvalidOperationException e) { conflictBlocked = e.InnerException?.Message.StartsWith("Collision inputs are incomplete") == true; }
            Require(conflictBlocked && !Directory.Exists(Path.Combine(conflicting.OutputDirectory, "resource")), "asset conflicts still block resource output");
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

    private static void CheckCollisionPrimitives()
    {
        Bounds[] shapes = [
            new BoundSphere { Type = BoundsType.Sphere, SphereRadius = 0.5f },
            new BoundCapsule { Type = BoundsType.Capsule, SphereRadius = 0.8f, Margin = 0.2f },
            new BoundCylinder { Type = BoundsType.Cylinder, BoxMin = new(-0.3f, -0.7f, -0.3f), BoxMax = new(0.3f, 0.7f, 0.3f) }];
        var transform = Matrix.Scaling(1.2f, 0.8f, 1.4f) * Matrix.RotationZ(0.4f) * Matrix.Translation(5, 6, 7);
        foreach (var shape in shapes)
        {
            var file = new YbnFile { Bounds = shape };
            var reloaded = new YbnFile(); reloaded.Load(file.Save());
            var scene = new CollisionScene(new BakeSettings()); scene.AddBounds(reloaded.Bounds, transform, "round-trip fixture");
            Require(scene.Issues.Count == 0 && scene.Triangles.Count > 0, "native rounded collision is extracted");
            foreach (Vector3 direction in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Normalize(new Vector3(1, 0.2f, 1)) })
            {
                var localRay = new Ray(direction * 5, -direction);
                var expected = reloaded.Bounds.RayIntersect(ref localRay);
                // CodeWalker's cylinder ray routine degenerates for an axis-parallel ray; use the exact cap height there.
                bool cylinderCap = reloaded.Bounds is BoundCylinder && direction == Vector3.UnitY;
                Require(expected.Hit || cylinderCap, $"analytic primitive ray hits {reloaded.Bounds.GetType().Name} along {direction}");
                Vector3 expectedPoint = cylinderCap ? new Vector3(0, shape.BoxMax.Y, 0) : expected.Position;
                Vector3 origin = Vector3.TransformCoordinate(localRay.Position, transform);
                var ray = new Ray(origin, Vector3.Normalize(new Vector3(5, 6, 7) - origin));
                float nearest = float.MaxValue;
                foreach (var triangle in scene.Triangles)
                    if (ray.Intersects(ref triangle[0], ref triangle[1], ref triangle[2], out float distance)) nearest = Math.Min(nearest, distance);
                Vector3 actual = origin + ray.Direction * nearest;
                Require(Vector3.Distance(actual, Vector3.TransformCoordinate(expectedPoint, transform)) < CollisionPrimitives.MaxWorldError,
                    "rounded collision agrees with CodeWalker's analytic ray intersections after scale/rotation/native reload");
            }
            Require(scene.Triangles.All(t => Vector3.Dot(Vector3.Cross(t[1] - t[0], t[2] - t[0]),
                (t[0] + t[1] + t[2]) / 3 - new Vector3(5, 6, 7)) > 0), "rounded collision has outward normals");
        }

        var bound = new BoundBox { Transform = Matrix.Translation(2, 3, 4) };
        var composite = new BoundComposite { Children = new ResourcePointerArray64<Bounds> { data_items = [bound] } };
        var fragment = new FragType
        {
            Drawable = new FragDrawable { Skeleton = new Skeleton { BonesMap = new Dictionary<ushort, Bone> { [7] = new Bone { AbsTransform = Matrix.Translation(2, 3, 4) } } } },
            PhysicsLODGroup = new FragPhysicsLODGroup
            {
                PhysicsLOD1 = new FragPhysicsLOD
                {
                    Archetype1 = new FragPhysArchetype { Bound = composite },
                    Children = new ResourcePointerArray64<FragPhysTypeChild> { data_items = [new FragPhysTypeChild { BoneTag = 7, Drawable1 = new FragDrawable { FragMatrix = Matrix4F_s.Identity } }] }
                }
            }
        };
        var fixture = new YftFile { Fragment = fragment };
        Require(ReferenceEquals(CollisionScene.FragmentBounds(fixture, "fixture"), composite), "fragment bind placement is verified without doubling transforms");
        bound.Transform = Matrix.Identity;
        bool rejected = false;
        try { CollisionScene.FragmentBounds(fixture, "fixture"); } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "ambiguous fragment placement remains blocked");
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
