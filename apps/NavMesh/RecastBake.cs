using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using SharpDX;

namespace BLRP.NavMesh;

public static class RecastBake
{
    // Proper rotation (determinant +1): GTA Z-up to Recast Y-up.
    public static RcVec3f ToRecast(Vector3 v) => new(v.X, v.Z, -v.Y);
    public static Vector3 FromRecast(float[] verts, int index) => new(verts[index * 3], -verts[index * 3 + 2], verts[index * 3 + 1]);

    public static List<Vector3[]> Build(List<Vector3[]> triangles, BakeSettings settings)
    {
        var agent = settings.Agent;
        var padding = agent.Radius + agent.CellSize * 4;
        var min = settings.BoundsMin - new Vector3(padding, padding, agent.Height);
        var max = settings.BoundsMax + new Vector3(padding, padding, agent.Height);
        var width = (int)Math.Ceiling((max.X - min.X) / agent.CellSize);
        var depth = (int)Math.Ceiling((max.Y - min.Y) / agent.CellSize);
        if ((long)width * depth > agent.MaxRasterCells)
            throw new InvalidDataException("Bake exceeds maxRasterCells. Split the requested region or increase the explicit memory budget.");
        var verts = new List<float>();
        var indices = new List<int>();
        foreach (var triangle in triangles)
        {
            if (triangle.Any(v => !float.IsFinite(v.X + v.Y + v.Z))) throw new InvalidDataException("Non-finite collision vertex.");
            if (!Geometry.Overlaps(triangle, min, max)) continue;
            foreach (var v in triangle)
            {
                var r = ToRecast(v);
                indices.Add(verts.Count / 3);
                verts.Add(r.X); verts.Add(r.Y); verts.Add(r.Z);
            }
        }
        if (indices.Count == 0) throw new InvalidDataException("No collision triangles overlap the bake bounds.");
        var input = new RcSampleInputGeomProvider(verts.ToArray(), indices.ToArray());
        var config = new RcConfig(RcPartition.WATERSHED, agent.CellSize, agent.CellHeight,
            agent.Slope, agent.Height, agent.Radius, agent.Climb, 0, 0,
            agent.MaxEdgeLength, agent.SimplificationError, 6, 2, 0.5f,
            true, true, true, new RcAreaModification(1), true);
        // One padded raster avoids independent tile erosion seams. GTA tiling happens after generation.
        var boundsMin = new RcVec3f(min.X, min.Z, -max.Y);
        var boundsMax = new RcVec3f(max.X, max.Z, -min.Y);
        var result = new RcBuilder().Build(input, new RcBuilderConfig(config, boundsMin, boundsMax), false);
        var detail = result.MeshDetail;
        var output = new List<Vector3[]>();
        for (int mesh = 0; mesh < detail.nmeshes; mesh++)
        {
            int vertexStart = detail.meshes[mesh * 4];
            int triangleStart = detail.meshes[mesh * 4 + 2];
            int count = detail.meshes[mesh * 4 + 3];
            for (int i = 0; i < count; i++)
            {
                var polygon = Enumerable.Range(0, 3).Select(j =>
                    FromRecast(detail.verts, vertexStart + detail.tris[(triangleStart + i) * 4 + j])).ToArray();
                if (Geometry.SignedArea(polygon) < 0) Array.Reverse(polygon);
                var clipped = Geometry.ClipBox(polygon, settings.BoundsMin, settings.BoundsMax);
                if (clipped.Length >= 3 && Geometry.SignedArea(clipped) > 1e-8) output.Add(clipped);
            }
        }
        if (output.Count == 0) throw new InvalidDataException("Recast found no walkable surfaces. Inspect collision winding and agent settings.");
        return output;
    }
}
