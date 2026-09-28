using SharpDX;

namespace BLRP.NavMesh;

internal static class CollisionPrimitives
{
    internal const float MaxWorldError = 0.002f;

    // Curved collision is sampled to a bounded chord error, below the bake's raster resolution.
    internal static List<Vector3[]> Round(Vector3 a, Vector3 b, float radius, bool capsule, float tolerance)
    {
        if (!float.IsFinite(radius) || radius <= 0 || !float.IsFinite(tolerance) || tolerance <= 0)
            throw new InvalidDataException("Invalid rounded collision dimensions.");
        double required = Math.Ceiling(Math.PI / Math.Acos(1 - Math.Min(tolerance / (2.0 * radius), 1)));
        if (!double.IsFinite(required) || required > 512)
            throw new NotSupportedException("Rounded collision exceeds the tessellation budget at 2mm precision.");
        int segments = Math.Max(16, (int)required);
        segments = (segments + 3) / 4 * 4;
        Vector3 axis = b - a;
        axis = axis.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(axis);
        Vector3 u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
        Vector3 v = Vector3.Cross(axis, u);
        var rings = new List<Vector3[]>();
        void Ring(Vector3 center, float r)
        {
            rings.Add(Enumerable.Range(0, segments).Select(i => center + r *
                (u * MathF.Cos(i * MathF.Tau / segments) + v * MathF.Sin(i * MathF.Tau / segments))).ToArray());
        }
        if (capsule)
        {
            for (int i = 0; i <= segments / 4; i++)
            { float angle = -MathF.PI / 2 + i * MathF.Tau / segments; Ring(a + axis * (radius * MathF.Sin(angle)), radius * MathF.Cos(angle)); }
            for (int i = 0; i <= segments / 4; i++)
            { float angle = i * MathF.Tau / segments; Ring(b + axis * (radius * MathF.Sin(angle)), radius * MathF.Cos(angle)); }
        }
        else { Ring(a, 0); Ring(a, radius); Ring(b, radius); Ring(b, 0); }
        var triangles = new List<Vector3[]>();
        for (int ring = 1; ring < rings.Count; ring++)
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % segments;
            triangles.Add([rings[ring - 1][i], rings[ring - 1][j], rings[ring][j]]);
            triangles.Add([rings[ring - 1][i], rings[ring][j], rings[ring][i]]);
        }
        return triangles;
    }

    internal static Vector3[][] Box(Vector3 corner, Vector3 x, Vector3 y, Vector3 z)
    {
        Vector3[] p = [corner, corner + x, corner + x + y, corner + y, corner + z, corner + x + z, corner + x + y + z, corner + y + z];
        int[] faces = [0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,5,1, 0,4,5, 3,2,6, 3,6,7, 1,5,6, 1,6,2, 0,3,7, 0,7,4];
        return Enumerable.Range(0, faces.Length / 3).Select(i => new[] { p[faces[i * 3]], p[faces[i * 3 + 1]], p[faces[i * 3 + 2]] }).ToArray();
    }
}
