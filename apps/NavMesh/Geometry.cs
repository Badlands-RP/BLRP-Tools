using SharpDX;

namespace BLRP.NavMesh;

public static class Geometry
{
    public static bool IsConvex(Vector3[] p)
    {
        for (int i = 0; i < p.Length; i++)
            if (SignedArea([p[i], p[(i + 1) % p.Length], p[(i + 2) % p.Length]]) < -1e-6) return false;
        return SignedArea(p) > 0;
    }
    public static bool Overlaps(Vector3[] points, Vector3 min, Vector3 max) =>
        points.Min(v => v.X) <= max.X && points.Max(v => v.X) >= min.X &&
        points.Min(v => v.Y) <= max.Y && points.Max(v => v.Y) >= min.Y &&
        points.Min(v => v.Z) <= max.Z && points.Max(v => v.Z) >= min.Z;

    public static double SignedArea(Vector3[] p)
    {
        double area = 0;
        for (int i = 1; i + 1 < p.Length; i++)
            area += ((double)p[i].X - p[0].X) * ((double)p[i + 1].Y - p[0].Y) -
                ((double)p[i].Y - p[0].Y) * ((double)p[i + 1].X - p[0].X);
        return area * 0.5;
    }

    public static Vector3[] Clip(Vector3[] points, int axis, float plane, bool greater)
    {
        if (points.Length == 0) return [];
        var output = new List<Vector3>();
        var previous = points[^1];
        bool previousInside = greater ? previous[axis] >= plane : previous[axis] <= plane;
        foreach (var current in points)
        {
            bool inside = greater ? current[axis] >= plane : current[axis] <= plane;
            if (inside != previousInside)
            {
                var t = ((double)plane - previous[axis]) / ((double)current[axis] - previous[axis]);
                var v = previous + (current - previous) * (float)t;
                v[axis] = plane;
                output.Add(v);
            }
            if (inside) output.Add(current);
            previous = current;
            previousInside = inside;
        }
        for (int i = output.Count - 1; i >= 0 && output.Count > 1; i--)
            if (Vector3.DistanceSquared(output[i], output[(i + 1) % output.Count]) < 1e-12f) output.RemoveAt(i);
        return output.Count >= 3 ? output.ToArray() : [];
    }

    public static Vector3[] ClipBox(Vector3[] points, Vector3 min, Vector3 max)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            points = Clip(points, axis, min[axis], true);
            points = Clip(points, axis, max[axis], false);
        }
        return points;
    }

    public static List<Vector3[]> SubtractBox(Vector3[] polygon, Vector3 min, Vector3 max)
    {
        var remaining = polygon;
        var pieces = new List<Vector3[]>();
        for (int axis = 0; axis < 3 && remaining.Length >= 3; axis++)
        {
            var outside = Clip(remaining, axis, min[axis], false);
            if (remaining.Any(v => v[axis] < min[axis]) && outside.Length >= 3 && SignedArea(outside) > 1e-8) pieces.Add(outside);
            remaining = Clip(remaining, axis, min[axis], true);
            outside = Clip(remaining, axis, max[axis], true);
            if (remaining.Any(v => v[axis] > max[axis]) && outside.Length >= 3 && SignedArea(outside) > 1e-8) pieces.Add(outside);
            remaining = Clip(remaining, axis, max[axis], false);
        }
        return pieces;
    }

    public static void WriteObj(string filename, IEnumerable<Vector3[]> polygons)
    {
        using var writer = new StreamWriter(filename);
        int offset = 1;
        foreach (var polygon in polygons)
        {
            foreach (var p in polygon) writer.WriteLine(FormattableString.Invariant($"v {p.X:R} {p.Y:R} {p.Z:R}"));
            writer.WriteLine("f " + string.Join(" ", Enumerable.Range(offset, polygon.Length)));
            offset += polygon.Length;
        }
    }
}
