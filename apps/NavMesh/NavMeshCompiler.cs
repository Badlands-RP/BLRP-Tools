using CodeWalker.GameFiles;
using SharpDX;

namespace BLRP.NavMesh;

public sealed class QuantizationMetrics
{
    public int CollapsedPolygons { get; set; }
    public int RemovedVertices { get; set; }
    public double MaxXYShiftMetres { get; set; }
}

public static class NavMeshCompiler
{
    public const uint NoLink = 0x3FFF;
    public const float TileSize = 150;
    // Smaller than a native XY quantization step; never merge distinct adjacent grid vertices.
    public const float EdgeTolerance = 0.0005f;

    public static List<YnvPoly> TilePolygons(IEnumerable<Vector3[]> polygons, BakeSettings settings, QuantizationMetrics? metrics = null)
    {
        var result = new List<YnvPoly>();
        foreach (var vertices in polygons)
        {
            if (vertices.Length < 3 || Geometry.SignedArea(vertices) <= 1e-8) continue;
            if (!Geometry.IsConvex(vertices)) throw new InvalidDataException("Triangulate concave navigation input before clipping.");
            int minX = Cell(vertices.Min(v => v.X)), minY = Cell(vertices.Min(v => v.Y));
            int maxX = Cell(vertices.Max(v => v.X)), maxY = Cell(vertices.Max(v => v.Y));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                if (x is < 0 or >= 100 || y is < 0 or >= 100) continue;
                var min = new Vector3(-6000 + x * TileSize, -6000 + y * TileSize, -float.MaxValue);
                var clipped = Geometry.Clip(Geometry.Clip(Geometry.Clip(Geometry.Clip(vertices, 0, min.X, true),
                    0, min.X + TileSize, false), 1, min.Y, true), 1, min.Y + TileSize, false);
                if (clipped.Length < 3 || Geometry.SignedArea(clipped) <= 1e-8) continue;
                var original = clipped;
                clipped = QuantizeXY(clipped, x + 100 * y);
                if (metrics != null)
                {
                    metrics.RemovedVertices += original.Length - clipped.Length;
                    foreach (var v in original)
                    {
                        var q = QuantizeVertex(v, x + 100 * y);
                        metrics.MaxXYShiftMetres = Math.Max(metrics.MaxXYShiftMetres, Vector3.Distance(v, q));
                    }
                }
                if (clipped.Length < 3 || Geometry.SignedArea(clipped) <= 1e-8)
                { if (metrics != null) metrics.CollapsedPolygons++; continue; }
                var poly = new YnvPoly { Vertices = clipped, AreaID = (ushort)(x + 100 * y) };
                poly.Flags1 = settings.PolygonFlags[0]; poly.Flags2 = settings.PolygonFlags[1];
                poly.Flags3 = settings.PolygonFlags[2]; poly.Flags4 = settings.PolygonFlags[3]; poly.Flags5 = settings.PolygonFlags[4];
                poly.B14_IsInterior = settings.Interior;
                poly.B17_IsFlatGround = clipped.Max(v => v.Z) - clipped.Min(v => v.Z) < 0.01f;
                result.Add(poly);
            }
        }
        return result;
    }

    public static Vector3[] QuantizeXY(Vector3[] vertices, int area)
    {
        var output = new List<Vector3>();
        foreach (var v in vertices)
        {
            var q = QuantizeVertex(v, area);
            if (output.Count == 0 || Vector3.DistanceSquared(output[^1], q) > 1e-12f) output.Add(q);
        }
        if (output.Count > 1 && Vector3.DistanceSquared(output[0], output[^1]) < 1e-12f) output.RemoveAt(output.Count - 1);
        return output.Count >= 3 ? output.ToArray() : [];
    }

    private static Vector3 QuantizeVertex(Vector3 v, int area)
    {
        double minX = -6000 + area % 100 * TileSize, minY = -6000 + area / 100 * TileSize;
        return new Vector3((float)(minX + Math.Round((v.X - minX) / TileSize * 65535) / 65535 * TileSize),
            (float)(minY + Math.Round((v.Y - minY) / TileSize * 65535) / 65535 * TileSize), v.Z);
    }

    public static int Cell(float coordinate) => (int)Math.Floor(((double)coordinate + 6000) / TileSize);
    public static string TileName(int area) => $"navmesh[{area % 100 * 3}][{area / 100 * 3}].ynv";

    public static YnvEdge Boundary() => new() { AreaID1 = NoLink, AreaID2 = NoLink, PolyID1 = NoLink, PolyID2 = NoLink };

    public static YnvEdge CopyEdge(YnvEdge e) => new()
    {
        RawData = e.RawData, AreaID1 = e.AreaID1, AreaID2 = e.AreaID2, Poly1 = e.Poly1, Poly2 = e.Poly2
    };

    public static Dictionary<int, YnvFile> CreateTiles(IEnumerable<YnvPoly> polygons)
    {
        var tiles = new Dictionary<int, YnvFile>();
        foreach (var group in polygons.GroupBy(p => (int)p.AreaID).OrderBy(g => g.Key))
        {
            var nav = new CodeWalker.GameFiles.NavMesh(); nav.SetDefaults(false);
            var file = new YnvFile { Name = TileName(group.Key), Nav = nav, AreaID = group.Key, Polys = group.ToList() };
            tiles[group.Key] = file;
        }
        return tiles;
    }

    public static void Connect(Dictionary<int, YnvFile> tiles, Baseline? baseline = null)
    {
        var all = tiles.Values.OrderBy(t => t.AreaID).SelectMany(t => t.Polys).ToList();
        var original = all.ToDictionary(p => p, p => (Vertices: p.Vertices, Edges: p.Edges));
        var pool = new VertexPool();
        foreach (var p in all) foreach (var v in p.Vertices) pool.Add(v);
        // Split T-junctions before matching. The spatial buckets avoid comparing every vertex with every edge.
        foreach (var poly in all)
        {
            var vertices = new List<Vector3>(); var edges = new List<YnvEdge>();
            var source = original[poly];
            for (int i = 0; i < source.Vertices.Length; i++)
            {
                var a = source.Vertices[i]; var b = source.Vertices[(i + 1) % source.Vertices.Length];
                var points = new List<(float T, Vector3 Position)> { (0, a) };
                var ab = b - a; float lengthSquared = ab.LengthSquared();
                if (lengthSquared <= 1e-12f) throw new InvalidDataException("Degenerate navigation edge.");
                foreach (var v in pool.NearSegment(a, b))
                {
                    float t = Vector3.Dot(v - a, ab) / lengthSquared;
                    if (t <= 0 || t >= 1) continue;
                    var projected = a + ab * t;
                    if (Vector3.DistanceSquared(projected, v) <= EdgeTolerance * EdgeTolerance &&
                        Vector3.DistanceSquared(projected, a) > EdgeTolerance * EdgeTolerance &&
                        Vector3.DistanceSquared(projected, b) > EdgeTolerance * EdgeTolerance)
                        points.Add((t, projected));
                }
                foreach (var point in points.OrderBy(v => v.T))
                {
                    if (vertices.Count > 0 && Vector3.DistanceSquared(vertices[^1], point.Position) < EdgeTolerance * EdgeTolerance) continue;
                    vertices.Add(point.Position);
                    edges.Add(source.Edges != null && i < source.Edges.Length ? CopyEdge(source.Edges[i]) : Boundary());
                }
            }
            poly.Vertices = vertices.ToArray(); poly.Edges = edges.ToArray();
        }
        var edgeOwners = new Dictionary<(int, int), List<(YnvPoly Poly, int Edge, int Start)>>();
        foreach (var poly in all)
        for (int i = 0; i < poly.Vertices.Length; i++)
        {
            int a = pool.Add(poly.Vertices[i]), b = pool.Add(poly.Vertices[(i + 1) % poly.Vertices.Length]);
            if (a == b) throw new InvalidDataException($"Navigation edge collapses within native quantization tolerance: {poly.Vertices[i]} -> {poly.Vertices[(i + 1) % poly.Vertices.Length]}");
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!edgeOwners.TryGetValue(key, out var owners)) edgeOwners[key] = owners = [];
            owners.Add((poly, i, a));
        }
        foreach (var owners in edgeOwners.Values)
        {
            if (owners.Count > 2) throw new InvalidDataException("Non-manifold or overlapping navigation polygons share an edge.");
            if (owners.Count != 2) continue;
            var a = owners[0]; var b = owners[1];
            if (a.Start == b.Start) throw new InvalidDataException("Adjacent polygons have inconsistent winding.");
            // Existing coincident edges are not sufficient evidence of a vanilla connection.
            if (baseline != null && baseline.Preserved.Contains(a.Poly) && baseline.Preserved.Contains(b.Poly)) continue;
            if (baseline != null && !baseline.MayConnect(a.Poly, a.Edge, b.Poly, b.Edge)) continue;
            bool retainFlags = baseline != null && baseline.Origins.ContainsKey(a.Poly) && baseline.Origins.ContainsKey(b.Poly) &&
                baseline.Origins[a.Poly] != baseline.Origins[b.Poly];
            Link(a.Poly, a.Edge, b.Poly, retainFlags); Link(b.Poly, b.Edge, a.Poly, retainFlags);
        }
        foreach (var tile in tiles.Values)
        {
            for (int i = 0; i < tile.Polys.Count; i++)
            {
                var poly = tile.Polys[i]; poly.Index = i; poly.Ynv = tile; poly.AreaID = (ushort)tile.AreaID;
                poly.CalculatePosition();
                if (baseline?.Preserved.Contains(poly) != true)
                    poly.B19_IsCellEdge = poly.Edges.Any(e => e.Poly1 != null ? e.Poly1.AreaID != poly.AreaID : e.AreaID1 != NoLink && e.AreaID1 != poly.AreaID);
            }
        }
    }

    private static void Link(YnvPoly owner, int index, YnvPoly neighbor, bool retainFlags = false)
    {
        var edge = owner.Edges[index];
        edge.Poly1 = edge.Poly2 = neighbor;
        if (retainFlags) return;
        edge.Poly1Unk2 = edge.Poly2Unk2 = owner.AreaID == neighbor.AreaID ? 1u : 0u;
        edge.Poly1Unk3 = 0; edge.Poly2Unk3 = owner.AreaID == neighbor.AreaID ? 0u : 4u;
    }

    public static Dictionary<int, byte[]> SaveAndVerify(Dictionary<int, YnvFile> tiles, Baseline? baseline = null)
    {
        var bytes = new Dictionary<int, byte[]>();
        var loaded = new Dictionary<int, YnvFile>();
        foreach (var tile in tiles.Values.OrderBy(t => t.AreaID))
        {
            if (tile.Polys.Count == 0) throw new InvalidDataException($"Refusing empty tile {tile.AreaID}.");
            if (tile.Polys.Count >= NoLink || tile.Polys.Sum(p => p.Vertices.Length) > ushort.MaxValue)
                throw new InvalidDataException($"YNV index capacity exceeded: {tile.AreaID}.");
            var vertices = tile.Polys.SelectMany(p => p.Vertices).ToList();
            float minZ = vertices.Min(v => v.Z), maxZ = vertices.Max(v => v.Z);
            if (tile.Points != null) foreach (var point in tile.Points) { minZ = Math.Min(minZ, point.Position.Z); maxZ = Math.Max(maxZ, point.Position.Z); }
            if (tile.Portals != null) foreach (var portal in tile.Portals)
            {
                minZ = Math.Min(minZ, Math.Min(portal.PositionFrom.Z, portal.PositionTo.Z));
                maxZ = Math.Max(maxZ, Math.Max(portal.PositionFrom.Z, portal.PositionTo.Z));
            }
            // Preserve existing extents when possible to avoid unnecessarily requantizing vanilla data.
            if (tile.Nav.SectorTree != null)
            { minZ = Math.Min(minZ, tile.Nav.AABBMin.Z); maxZ = Math.Max(maxZ, tile.Nav.AABBMax.Z); }
            var min = new Vector3(-6000 + (tile.AreaID % 100) * TileSize, -6000 + (tile.AreaID / 100) * TileSize, minZ);
            tile.Nav.SectorTree ??= new NavMeshSector();
            tile.Nav.SectorTree.AABBMin = new Vector4(min, 0);
            tile.Nav.SectorTree.AABBMax = new Vector4(min.X + TileSize, min.Y + TileSize, maxZ, 0);
            tile.Nav.AABBSize = new Vector3(TileSize, TileSize, maxZ - minZ);
            foreach (var v in vertices)
                if (!float.IsFinite(v.X + v.Y + v.Z) || v.X < min.X - 0.0001 || v.X > min.X + TileSize + 0.0001 ||
                    v.Y < min.Y - 0.0001 || v.Y > min.Y + TileSize + 0.0001)
                    throw new InvalidDataException($"Out-of-tile vertex in {tile.Name}: {v}");
            tile.UpdateContentFlags(false);
            var adjacentAreas = tile.Polys.SelectMany(p => p.Edges).SelectMany(e => new[] {
                e.Poly1 == null ? e.AreaID1 : e.Poly1.AreaID, e.Poly2 == null ? e.AreaID2 : e.Poly2.AreaID }).Distinct().Count();
            if (adjacentAreas + 6 > 32) throw new InvalidDataException("YNV adjacency table capacity exceeded.");
            var data = tile.Save();
            if (System.Text.Encoding.ASCII.GetString(data, 0, 4) != "RSC7") throw new InvalidDataException("Non-native YNV output.");
            var reload = new YnvFile(); reload.Load(data); reload.Name = tile.Name;
            if (reload.Polys.Count != tile.Polys.Count) throw new InvalidDataException("YNV polygon count changed after save.");
            double maxError = 0;
            double tolerance = tile.Nav.AABBSize.Length() / ushort.MaxValue / 2 + 0.0003;
            for (int i = 0; i < tile.Polys.Count; i++)
            {
                var before = tile.Polys[i]; var after = reload.Polys[i];
                if (before.Flags1 != after.Flags1 || before.Flags2 != after.Flags2 || before.Flags3 != after.Flags3 ||
                    before.Flags4 != after.Flags4 || before.Flags5 != after.Flags5 ||
                    !(before.PortalLinks ?? []).SequenceEqual(after.PortalLinks ?? []))
                    throw new InvalidDataException("Polygon flags or portal links changed after save.");
                if (before.Vertices.Length != after.Vertices.Length) throw new InvalidDataException("YNV vertex count changed.");
                for (int j = 0; j < before.Vertices.Length; j++)
                {
                    double error = Vector3.Distance(before.Vertices[j], after.Vertices[j]);
                    if (!double.IsFinite(error) || error > tolerance) throw new InvalidDataException($"YNV geometry changed by {error}m, allowed {tolerance}m.");
                    maxError = Math.Max(maxError, error);
                    CheckEdge(before.Edges[j], after.Edges[j]);
                }
                if (Math.Abs(Geometry.SignedArea(after.Vertices)) < 1e-8) throw new InvalidDataException("Polygon collapses after quantization.");
            }
            if ((tile.Portals?.Count ?? 0) != (reload.Portals?.Count ?? 0) || (tile.Points?.Count ?? 0) != (reload.Points?.Count ?? 0))
                throw new InvalidDataException("Special navigation data count changed after save.");
            for (int i = 0; i < (tile.Portals?.Count ?? 0); i++)
            {
                var a = tile.Portals![i]; var b = reload.Portals![i];
                // The writer quantizes a temporary portal struct rather than mutating RawData.
                var metadata = b.RawData;
                metadata.PositionFrom = a.RawData.PositionFrom; metadata.PositionTo = a.RawData.PositionTo;
                if (!a.RawData.Equals(metadata) || Vector3.Distance(a.PositionFrom, b.PositionFrom) > tolerance || Vector3.Distance(a.PositionTo, b.PositionTo) > tolerance)
                    throw new InvalidDataException("Navigation portal changed after save.");
            }
            // Sector rebuilding can reorder points; match their type, angle and quantized position.
            var remainingPoints = (reload.Points ?? []).ToList();
            foreach (var point in tile.Points ?? [])
            {
                var match = remainingPoints.FindIndex(p => p.Type == point.Type && p.Angle == point.Angle && Vector3.Distance(p.Position, point.Position) <= tolerance);
                if (match < 0) throw new InvalidDataException("Navigation point changed after save.");
                remainingPoints.RemoveAt(match);
            }
            Console.WriteLine($"Verified {tile.Name}: {reload.Polys.Count} polygons, max error {maxError:F6}m");
            bytes[tile.AreaID] = data; loaded[tile.AreaID] = reload;
        }
        foreach (var tile in loaded.Values)
        foreach (var poly in tile.Polys)
        foreach (var edge in poly.Edges)
        {
            foreach (var target in new[] { (edge.AreaID1, edge.PolyID1), (edge.AreaID2, edge.PolyID2) })
                if (target.Item2 != NoLink && loaded.TryGetValue((int)target.Item1, out var found) && target.Item2 >= found.Polys.Count)
                    throw new InvalidDataException("Dangling native link after reload.");
            if (edge.PolyID1 == NoLink) continue;
            if (!loaded.TryGetValue((int)edge.AreaID1, out var neighbor)) continue; // Unmodified external baseline reference.
            if (edge.PolyID1 >= neighbor.Polys.Count) throw new InvalidDataException("Dangling native link after reload.");
            // Runtime navigation remains a separate check; special vanilla edge slots can be asymmetric.
            bool original = baseline?.Origins.ContainsKey(tiles[tile.AreaID].Polys[poly.Index]) == true;
            if (!original && edge.AreaID1 == edge.AreaID2 && edge.PolyID1 == edge.PolyID2 &&
                !neighbor.Polys[(int)edge.PolyID1].Edges.Any(e => e.AreaID1 == tile.AreaID && e.PolyID1 == poly.Index))
                throw new InvalidDataException($"Non-reciprocal link {tile.AreaID}:{poly.Index} -> {edge.AreaID1}:{edge.PolyID1}");
        }
        return bytes;
    }

    private static void CheckEdge(YnvEdge before, YnvEdge after)
    {
        uint area1 = before.Poly1 == null ? before.AreaID1 : before.Poly1.AreaID;
        uint area2 = before.Poly2 == null ? before.AreaID2 : before.Poly2.AreaID;
        uint id1 = before.Poly1 == null ? before.PolyID1 : (uint)before.Poly1.Index;
        uint id2 = before.Poly2 == null ? before.PolyID2 : (uint)before.Poly2.Index;
        if (after.AreaID1 != area1 || after.AreaID2 != area2 || after.PolyID1 != id1 || after.PolyID2 != id2)
            throw new InvalidDataException("YNV edge reference changed after save.");
        if (before.Poly1Unk2 != after.Poly1Unk2 || before.Poly2Unk2 != after.Poly2Unk2 ||
            before.Poly1Unk3 != after.Poly1Unk3 || before.Poly2Unk3 != after.Poly2Unk3)
            throw new InvalidDataException("YNV edge flags changed after save.");
    }

    private sealed class VertexPool
    {
        private readonly List<Vector3> vertices = [];
        private readonly Dictionary<(int, int, int), List<int>> small = [];
        private readonly Dictionary<(int, int), List<int>> large = [];
        public int Add(Vector3 v)
        {
            var key = ((int)Math.Floor(v.X / EdgeTolerance), (int)Math.Floor(v.Y / EdgeTolerance), (int)Math.Floor(v.Z / EdgeTolerance));
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (small.TryGetValue((key.Item1 + x, key.Item2 + y, key.Item3 + z), out var candidates))
                    foreach (int id in candidates) if (Vector3.DistanceSquared(vertices[id], v) <= EdgeTolerance * EdgeTolerance) return id;
            int index = vertices.Count; vertices.Add(v);
            if (!small.TryGetValue(key, out var bucket)) small[key] = bucket = [];
            bucket.Add(index);
            var wideKey = ((int)Math.Floor(v.X), (int)Math.Floor(v.Y));
            if (!large.TryGetValue(wideKey, out var wideBucket)) large[wideKey] = wideBucket = [];
            wideBucket.Add(index);
            return index;
        }
        public IEnumerable<Vector3> NearSegment(Vector3 a, Vector3 b)
        {
            for (int x = (int)Math.Floor(Math.Min(a.X, b.X) - EdgeTolerance); x <= Math.Floor(Math.Max(a.X, b.X) + EdgeTolerance); x++)
            for (int y = (int)Math.Floor(Math.Min(a.Y, b.Y) - EdgeTolerance); y <= Math.Floor(Math.Max(a.Y, b.Y) + EdgeTolerance); y++)
                if (large.TryGetValue((x, y), out var ids)) foreach (int id in ids) yield return vertices[id];
        }
    }
}
