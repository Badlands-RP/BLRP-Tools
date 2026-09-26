using System.Text.Json;
using CodeWalker.GameFiles;
using SharpDX;

namespace BLRP.NavMesh;

public sealed class BaselineManifest
{
    public int GameBuild { get; set; }
    public string Source { get; set; } = "";
    public bool ValidatedForBuild { get; set; }
    public Dictionary<string, string> Files { get; set; } = [];
}

public sealed class Baseline
{
    public Dictionary<int, YnvFile> Tiles { get; } = [];
    public HashSet<YnvPoly> Preserved { get; } = [];
    public Dictionary<YnvPoly, YnvPoly> Origins { get; } = [];
    public Dictionary<YnvPoly, YnvEdge[]> OriginalEdges { get; } = [];
    public Dictionary<YnvPoly, Vector3[]> OriginalVertices { get; } = [];
    private readonly Dictionary<(int Area, int Poly), YnvPoly> originals = [];
    private readonly Dictionary<YnvPoly, List<YnvPoly>> replacements = [];

    public static Baseline Load(BakeSettings settings, CollisionScene scene, bool diagnostic = false)
    {
        var path = Path.Combine(settings.BaselineDirectory, "baseline.json");
        var manifest = JsonSerializer.Deserialize<BaselineManifest>(File.ReadAllText(path), BakeSettings.Json)
            ?? throw new InvalidDataException("Missing baseline manifest.");
        if (manifest.GameBuild != settings.GameBuild || string.IsNullOrWhiteSpace(manifest.Source) || (!diagnostic && !manifest.ValidatedForBuild))
            throw new InvalidDataException("Baseline gameBuild/source does not match the requested build.");
        scene.Inputs[path] = CollisionScene.Hash(path);
        var baseline = new Baseline();
        foreach (var item in manifest.Files.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(item.Key) != item.Key || Path.GetExtension(item.Key) != ".ynv")
                throw new InvalidDataException("Baseline entries must be plain .ynv filenames.");
            var file = Path.Combine(settings.BaselineDirectory, item.Key);
            if (!CollisionScene.Hash(file).Equals(item.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Baseline hash changed: {file}");
            var ynv = new YnvFile(); ynv.Load(scene.Read(file)); ynv.Name = item.Key;
            if (ynv.AreaID is < 0 or >= 10000 || NavMeshCompiler.TileName(ynv.AreaID) != item.Key)
                throw new InvalidDataException($"Baseline filename/AreaID mismatch: {item.Key}");
            if (!baseline.Tiles.TryAdd(ynv.AreaID, ynv)) throw new InvalidDataException("Duplicate baseline area.");
            foreach (var poly in ynv.Polys) baseline.originals[(ynv.AreaID, poly.Index)] = poly;
        }
        foreach (var tile in baseline.Tiles.Values)
        foreach (var poly in tile.Polys)
        foreach (var edge in poly.Edges)
        {
            baseline.originals.TryGetValue(((int)edge.AreaID1, (int)edge.PolyID1), out var a);
            baseline.originals.TryGetValue(((int)edge.AreaID2, (int)edge.PolyID2), out var b);
            edge.Poly1 = a; edge.Poly2 = b;
        }
        foreach (var poly in baseline.originals.Values)
        {
            baseline.OriginalEdges[poly] = poly.Edges.Select(NavMeshCompiler.CopyEdge).ToArray();
            baseline.OriginalVertices[poly] = poly.Vertices.ToArray();
        }
        return baseline;
    }

    public Dictionary<int, YnvFile> Compose(List<YnvPoly> generated, BakeSettings settings)
    {
        var editedAreas = new HashSet<int>(generated.Select(p => (int)p.AreaID));
        foreach (var tile in Tiles.Values)
            if (tile.Polys.Any(p => Math.Abs(Geometry.SignedArea(Geometry.ClipBox(p.Vertices, settings.BoundsMin, settings.BoundsMax))) > 1e-8))
                editedAreas.Add(tile.AreaID);
        foreach (int area in editedAreas)
        {
            if (!Tiles.ContainsKey(area)) throw new InvalidDataException($"Missing original tile {NavMeshCompiler.TileName(area)}.");
            foreach (var edge in Tiles[area].Polys.SelectMany(p => p.Edges))
                foreach (uint targetArea in new[] { edge.AreaID1, edge.AreaID2 })
                    if (targetArea != NavMeshCompiler.NoLink && !Tiles.ContainsKey((int)targetArea))
                        throw new InvalidDataException($"Edited tile references uncaptured area {targetArea}; extend the baseline before remapping indices.");
            // All possible immediate incoming seams must be available before changing indices.
            int x = area % 100, y = area / 100;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                if (x + dx is >= 0 and < 100 && y + dy is >= 0 and < 100 && !Tiles.ContainsKey(x + dx + (y + dy) * 100))
                    throw new InvalidDataException($"Baseline needs neighbor {NavMeshCompiler.TileName(x + dx + (y + dy) * 100)} to preserve incoming references.");
        }
        foreach (var tile in Tiles.Values)
        {
            foreach (var point in tile.Points ?? [])
                if (Geometry.Overlaps([point.Position], settings.BoundsMin, settings.BoundsMax))
                    throw new InvalidDataException($"Edit intersects navigation point {tile.AreaID}:{point.Index}, type {point.Type}, at {point.Position}; explicit point authoring is required.");
            var newPolys = new List<YnvPoly>();
            foreach (var poly in tile.Polys)
            {
                var removedPart = Geometry.ClipBox(poly.Vertices, settings.BoundsMin, settings.BoundsMax);
                if (removedPart.Length < 3 || Math.Abs(Geometry.SignedArea(removedPart)) < 1e-8)
                {
                    Preserved.Add(poly); newPolys.Add(poly); replacements[poly] = [poly];
                    Origins[poly] = poly;
                    continue;
                }
                if (!Geometry.IsConvex(poly.Vertices)) throw new InvalidDataException("Baseline edit requires convex polygons with positive winding.");
                editedAreas.Add(tile.AreaID);
                if (poly.PortalLinks?.Length > 0)
                    throw new InvalidDataException($"Edit intersects special navigation portal at {tile.AreaID}:{poly.Index}; adjust bounds or author its replacement explicitly.");
                var pieces = Geometry.SubtractBox(poly.Vertices, settings.BoundsMin, settings.BoundsMax)
                    .Select(v => new YnvPoly { RawData = poly.RawData, Vertices = NavMeshCompiler.QuantizeXY(v, poly.AreaID), AreaID = poly.AreaID })
                    .Where(p => p.Vertices.Length >= 3 && Geometry.SignedArea(p.Vertices) > 1e-8).ToList();
                foreach (var piece in pieces)
                {
                    Origins[piece] = poly;
                    piece.Edges = Enumerable.Range(0, piece.Vertices.Length).Select(i =>
                    {
                        int edge = FindOriginalEdge(OriginalVertices[poly], piece.Vertices[i], piece.Vertices[(i + 1) % piece.Vertices.Length]);
                        return edge < 0 ? NavMeshCompiler.Boundary() : NavMeshCompiler.CopyEdge(OriginalEdges[poly][edge]);
                    }).ToArray();
                }
                replacements[poly] = pieces; newPolys.AddRange(pieces);
            }
            tile.Polys = newPolys;
        }
        foreach (var tile in Tiles.Values)
        foreach (var poly in tile.Polys.Where(p => p.Edges != null))
        foreach (var edge in poly.Edges)
        {
            // Links to changed polygons are rebuilt geometrically after clipping.
            if (edge.Poly1 != null && !Preserved.Contains(edge.Poly1))
            { edge.Poly1 = null; edge.AreaID1 = edge.PolyID1 = NavMeshCompiler.NoLink; }
            if (edge.Poly2 != null && !Preserved.Contains(edge.Poly2))
            { edge.Poly2 = null; edge.AreaID2 = edge.PolyID2 = NavMeshCompiler.NoLink; }
        }
        foreach (var poly in generated) Tiles[poly.AreaID].Polys.Add(poly);
        foreach (var tile in Tiles.Values)
        {
            for (int i = 0; i < tile.Polys.Count; i++) tile.Polys[i].Index = i;
            foreach (var portal in tile.Portals ?? [])
            {
                ushort Remap(ushort area, ushort id)
                {
                    if (id == NavMeshCompiler.NoLink) return id;
                    if (!originals.TryGetValue((area, id), out var old))
                    {
                        if (!editedAreas.Contains(area)) return id;
                        throw new InvalidDataException("Missing portal target.");
                    }
                    if (!replacements.TryGetValue(old, out var found) ||
                        found.Count != 1 || !Preserved.Contains(found[0]))
                        throw new InvalidDataException("Edit changes a polygon referenced by a special portal. Explicit portal authoring is required.");
                    return checked((ushort)found[0].Index);
                }
                portal.PolyIDFrom1 = Remap(portal.AreaIDFrom, portal.PolyIDFrom1);
                portal.PolyIDFrom2 = Remap(portal.AreaIDFrom, portal.PolyIDFrom2);
                portal.PolyIDTo1 = Remap(portal.AreaIDTo, portal.PolyIDTo1);
                portal.PolyIDTo2 = Remap(portal.AreaIDTo, portal.PolyIDTo2);
            }
        }
        // Retain loaded neighbor tiles: their old polygon order remains stable while inbound IDs are updated.
        return Tiles;
    }

    public static int FindOriginalEdge(Vector3[] vertices, Vector3 a, Vector3 b)
    {
        // Clipped endpoints can move by half a native XY step on each axis.
        const float tolerance = 0.002f;
        for (int i = 0; i < vertices.Length; i++)
        {
            var start = vertices[i]; var d = vertices[(i + 1) % vertices.Length] - start;
            bool On(Vector3 v)
            {
                float t = Vector3.Dot(v - start, d) / d.LengthSquared();
                return t >= -0.00001f && t <= 1.00001f && Vector3.Distance(v, start + d * t) <= tolerance;
            }
            if (On(a) && On(b)) return i;
        }
        return -1;
    }

    public bool MayConnect(YnvPoly a, int edgeA, YnvPoly b, int edgeB)
    {
        if (!Origins.TryGetValue(a, out var oldA) || !Origins.TryGetValue(b, out var oldB)) return true;
        if (oldA == oldB) return true;
        bool Allowed(YnvPoly p, int edge, YnvPoly original, YnvPoly target)
        {
            int index = FindOriginalEdge(OriginalVertices[original], p.Vertices[edge], p.Vertices[(edge + 1) % p.Vertices.Length]);
            if (index < 0) return false;
            var e = OriginalEdges[original][index];
            if (e.AreaID1 != e.AreaID2 || e.PolyID1 != e.PolyID2)
                throw new InvalidDataException("Edit touches asymmetric vanilla links; explicit link authoring is required.");
            return e.Poly1 == target;
        }
        return Allowed(a, edgeA, oldA, oldB) && Allowed(b, edgeB, oldB, oldA);
    }
}
