using System.Text.Json;
using System.Text.Json.Serialization;
using SharpDX;
using System.ComponentModel;

namespace BLRP.NavMesh;

public sealed class BakeSettings
{
    public int GameBuild { get; set; }
    public string GameSourceFile { get; set; } = "";
    public bool AutoDetectGame { get; set; } = true;
    public bool AutoFitArea { get; set; }
    public string[] ResourceRoots { get; set; } = [];
    public string[] ConflictScanRoots { get; set; } = [];
    public string[] Ymaps { get; set; } = [];
    public CollisionInput[] Collision { get; set; } = [];
    public Dictionary<string, string[]> EntitySets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] IgnoreArchetypes { get; set; } = [];
    public string BaselineDirectory { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public float[] Min { get; set; } = [];
    public float[] Max { get; set; } = [];
    public AgentSettings Agent { get; set; } = new();
    public byte[] PolygonFlags { get; set; } = [0, 0, 0, 0, 0];
    public bool Interior { get; set; }
    public string[] Dependencies { get; set; } = [];
    public string CollisionReview { get; set; } = "";
    public bool AllowIsolatedComponents { get; set; }
    public bool AllowWarnings { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static BakeSettings Load(string filename)
    {
        var settings = JsonSerializer.Deserialize<BakeSettings>(File.ReadAllText(filename), Json)
            ?? throw new InvalidDataException("Empty bake settings.");
        var root = Path.GetDirectoryName(Path.GetFullPath(filename))!;
        string Resolve(string path) => string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path, root);
        settings.ResourceRoots = settings.ResourceRoots.Select(Resolve).ToArray();
        settings.ConflictScanRoots = settings.ConflictScanRoots.Select(Resolve).ToArray();
        settings.Ymaps = settings.Ymaps.Select(Resolve).ToArray();
        foreach (var input in settings.Collision) input.Path = Resolve(input.Path);
        settings.BaselineDirectory = Resolve(settings.BaselineDirectory);
        settings.GameSourceFile = Resolve(settings.GameSourceFile);
        settings.OutputDirectory = Resolve(settings.OutputDirectory);
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        if (GameBuild <= 0) throw new InvalidDataException("gameBuild is required.");
        if (string.IsNullOrWhiteSpace(OutputDirectory)) throw new InvalidDataException("outputDirectory is required.");
        if (Min.Length != 3 || Max.Length != 3 || Min.Concat(Max).Any(v => !float.IsFinite(v)) ||
            Enumerable.Range(0, 3).Any(i => Min[i] >= Max[i]))
            throw new InvalidDataException("min/max must be finite ordered XYZ bounds.");
        if (Min[0] < -6000 || Min[1] < -6000 || Max[0] > 9000 || Max[1] > 9000)
            throw new InvalidDataException("Bounds exceed GTA's supported navmesh grid.");
        if (PolygonFlags.Length != 5) throw new InvalidDataException("polygonFlags requires five bytes.");
        if (Ymaps.Length + Collision.Length == 0) throw new InvalidDataException("Specify ymaps or explicit collision inputs.");
        Agent.Validate();
        foreach (string dependency in Dependencies)
            if (dependency.Length == 0 || dependency.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
                throw new InvalidDataException("Invalid resource dependency name.");
        foreach (var source in ResourceRoots.Concat(new[] { BaselineDirectory }).Where(s => s.Length != 0))
        {
            var relative = Path.GetRelativePath(source, OutputDirectory);
            bool outside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative);
            if (!outside)
                throw new InvalidDataException("Output must be outside source resources and baseline directories.");
        }
    }

    public Vector3 BoundsMin => new(Min[0], Min[1], Min[2]);
    public Vector3 BoundsMax => new(Max[0], Max[1], Max[2]);
}

public sealed class CollisionInput
{
    public string Path { get; set; } = "";
    public float[] Position { get; set; } = [0, 0, 0];
    // This is a decoded world orientation, not raw CEntityDef.rotation.
    public float[] Orientation { get; set; } = [0, 0, 0, 1];
    public float[] Scale { get; set; } = [1, 1, 1];

    public Matrix Transform()
    {
        if (Position.Length != 3 || Orientation.Length != 4 || Scale.Length != 3 ||
            Position.Concat(Orientation).Concat(Scale).Any(v => !float.IsFinite(v)) || Scale.Any(v => v <= 0))
            throw new InvalidDataException($"Invalid transform: {Path}");
        var rotation = new Quaternion(Orientation[0], Orientation[1], Orientation[2], Orientation[3]);
        if (Math.Abs(rotation.LengthSquared() - 1) > 0.001f) throw new InvalidDataException("Orientation must be a unit quaternion.");
        return Matrix.Scaling(Scale[0], Scale[1], Scale[2]) * Matrix.RotationQuaternion(rotation) *
            Matrix.Translation(Position[0], Position[1], Position[2]);
    }
}

public sealed class AgentSettings
{
    [DisplayName("Cell width (m)"), Description("Horizontal raster resolution. Smaller values capture narrow openings but use more memory.")]
    public float CellSize { get; set; } = 0.15f;
    [DisplayName("Cell height (m)"), Description("Vertical raster resolution.")]
    public float CellHeight { get; set; } = 0.05f;
    [DisplayName("Pedestrian height (m)"), Description("Required headroom above a walkable surface.")]
    public float Height { get; set; } = 1.8f;
    [DisplayName("Pedestrian radius (m)"), Description("Clearance from walls and obstacles.")]
    public float Radius { get; set; } = 0.3f;
    [DisplayName("Maximum step (m)"), Description("Maximum climbable step height.")]
    public float Climb { get; set; } = 0.3f;
    [DisplayName("Maximum slope (degrees)")]
    public float Slope { get; set; } = 45;
    [DisplayName("Maximum edge length (m)")]
    public float MaxEdgeLength { get; set; } = 12;
    [DisplayName("Contour simplification"), Description("Maximum contour error in raster cells.")]
    public float SimplificationError { get; set; } = 1;
    [DisplayName("Raster cell budget"), Description("Upper limit on raster area to bound memory usage.")]
    public int MaxRasterCells { get; set; } = 16_000_000;

    public void Validate()
    {
        if (new[] { CellSize, CellHeight, Height, Radius, Climb, Slope, MaxEdgeLength, SimplificationError }.Any(v => !float.IsFinite(v)) ||
            CellSize < 0.02f || CellHeight < 0.01f || Height < CellHeight * 3 || Radius < 0 || Climb < 0 || Climb >= Height ||
            Slope < 0 || Slope >= 90 || MaxEdgeLength <= 0 || SimplificationError <= 0 || MaxRasterCells < 1)
            throw new InvalidDataException("Invalid agent/rasterization settings.");
    }
}
