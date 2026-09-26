using System.Security.Cryptography;
using System.Text;
using CodeWalker.GameFiles;
using SharpDX;

namespace BLRP.NavMesh;

public sealed class CollisionScene
{
    public List<Vector3[]> Triangles { get; } = [];
    public SortedDictionary<string, string> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Issues { get; } = [];
    public List<object> Placements { get; } = [];
    public List<object> Masks { get; } = [];
    public List<string> Exclusions { get; } = [];
    private readonly Dictionary<(uint, string), string> files = [];
    private readonly Dictionary<uint, Archetype> archetypes = [];
    private readonly Dictionary<string, Bounds?> bounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly BakeSettings settings;
    private GameSource? game;

    public CollisionScene(BakeSettings settings) { this.settings = settings; }

    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    public byte[] Read(string path)
    {
        var data = File.ReadAllBytes(path);
        Inputs[path] = Convert.ToHexString(SHA256.HashData(data));
        if (data.Length < 4 || Encoding.ASCII.GetString(data, 0, 4) != "RSC7")
            throw new InvalidDataException($"Unreadable native asset (possibly escrowed): {path}");
        return data;
    }

    public void Load()
    {
        if (settings.GameSourceFile.Length != 0) game = new GameSource(settings, this);
        foreach (var root in settings.ResourceRoots)
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is not (".ytyp" or ".ydr" or ".ybn" or ".ydd" or ".yft")) continue;
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                JenkIndex.Ensure(name);
                var key = (JenkHash.GenHash(name), extension);
                if (files.TryGetValue(key, out var previous) && Hash(previous) != Hash(file))
                    throw new InvalidDataException($"Conflicting asset names: {previous} and {file}");
                files[key] = file;
            }
            var manifest = Path.Combine(root, "fxmanifest.lua");
            if (File.Exists(manifest)) Inputs[manifest] = Hash(manifest);
        }
        foreach (var pair in files.Where(p => p.Key.Item2 == ".ytyp"))
        {
            var ytyp = new YtypFile();
            ytyp.Load(Read(pair.Value));
            ytyp.Name = Path.GetFileName(pair.Value);
            foreach (var archetype in ytyp.AllArchetypes ?? [])
            {
                uint hash = archetype.Hash;
                if (archetypes.TryGetValue(hash, out var previous) && previous.Ytyp != ytyp)
                    throw new InvalidDataException($"Conflicting archetype {archetype.Name}: {previous.Ytyp?.Name} and {ytyp.Name}.");
                archetypes[hash] = archetype;
            }
        }
        foreach (var filename in settings.Ymaps.Order(StringComparer.OrdinalIgnoreCase))
        {
            var ymap = new YmapFile();
            ymap.Load(Read(filename));
            foreach (var entity in ymap.AllEntities ?? []) AddEntity(entity, filename);
        }
        foreach (var input in settings.Collision)
        {
            try
            {
                var collision = ReadBounds(input.Path, null);
                if (collision == null) throw new InvalidDataException($"No collision in {input.Path}");
                AddBounds(collision, input.Transform(), input.Path);
                Placements.Add(new { source = input.Path, input.Position, input.Orientation, input.Scale });
            }
            catch (Exception e) when (e is InvalidDataException || e is NotSupportedException) { Issues.Add(e.Message); }
        }
        if (settings.IgnoreArchetypes.Length > 0 && string.IsNullOrWhiteSpace(settings.CollisionReview))
            Issues.Add("Ignored archetypes require a collisionReview explaining why their collision is covered or intentionally excluded.");
    }

    private void AddEntity(YmapEntityDef entity, string source)
    {
        if ((entity.CEntityDef.flags & 4) != 0 || entity.CEntityDef.lodLevel is not (rage__eLodType.LODTYPES_DEPTH_HD or rage__eLodType.LODTYPES_DEPTH_ORPHANHD))
        { Exclusions.Add($"Entity collision disabled: {source}/{entity.CEntityDef.archetypeName}"); return; }
        uint hash = entity.CEntityDef.archetypeName;
        string name = entity.CEntityDef.archetypeName.ToString();
        if (settings.IgnoreArchetypes.Any(n => JenkHash.GenHash(n.ToLowerInvariant()) == hash))
        {
            Exclusions.Add($"{source}: {name}");
            return;
        }
        archetypes.TryGetValue(hash, out var archetype);
        archetype ??= game?.FindArchetype(hash);
        if (archetype == null)
        {
            Issues.Add($"Missing owning YTYP for {name} at {entity.Position} ({source}).");
            return;
        }
        entity.SetArchetype(archetype);
        float padding = settings.Agent.Radius + settings.Agent.CellSize * 4;
        if (archetype is not MloArchetype && !Geometry.Overlaps([entity.BBMin, entity.BBMax],
            settings.BoundsMin - new Vector3(padding), settings.BoundsMax + new Vector3(padding))) return;
        var transform = Matrix.Scaling(entity.Scale) * Matrix.RotationQuaternion(entity.Orientation) * Matrix.Translation(entity.Position);
        Placements.Add(new { source, archetype = archetype.Name, ownerYtyp = archetype.Ytyp?.Name,
            position = new[] { entity.Position.X, entity.Position.Y, entity.Position.Z },
            orientation = new[] { entity.Orientation.X, entity.Orientation.Y, entity.Orientation.Z, entity.Orientation.W },
            scale = new[] { entity.Scale.X, entity.Scale.Y, entity.Scale.Z } });
        string? collisionFile = null;
        string? Find(uint asset, string extension) => files.TryGetValue((asset, extension), out var path) ? path :
            game?.Contains(asset, extension) == true ? $"game:{asset:X8}{extension}" : null;
        if (archetype is MloArchetype)
        {
            collisionFile = Find(hash, ".ybn");
        }
        else
        {
            uint physics = archetype.BaseArchetypeDef.physicsDictionary;
            if (physics != 0) collisionFile = Find(physics, ".ybn");
            if (collisionFile == null && (uint)archetype.DrawableDict != 0)
                collisionFile = Find(archetype.DrawableDict, ".ydd");
            collisionFile ??= Find(hash, ".ydr");
            collisionFile ??= Find(hash, ".yft");
        }
        if (collisionFile == null) Issues.Add($"Missing collision/drawable for {archetype.Name} ({source}).");
        else
        {
            try
            {
                var collision = ReadBounds(collisionFile, hash);
                if (collision != null) AddBounds(collision, transform, $"{source}/{archetype.Name}");
            }
            catch (Exception e) when (e is InvalidDataException || e is NotSupportedException) { Issues.Add(e.Message); }
        }
        if (entity.MloInstance == null) return;
        if (entity.Scale != Vector3.One)
        {
            Issues.Add($"Scaled MLO {archetype.Name}: child placement requires explicit validation; bake is incomplete.");
            return;
        }
        foreach (var child in entity.MloInstance.Entities ?? []) AddEntity(child, source);
        var sets = entity.MloInstance.EntitySets ?? [];
        var selectionKey = Path.GetFileName(source) + ":" + entity.Index;
        if (!settings.EntitySets.TryGetValue(selectionKey, out var selected) && sets.Length != 0)
        {
            Issues.Add($"Specify entitySets['{selectionKey}'] explicitly; available: {string.Join(", ", sets.Select(s => s.EntitySet.Name))}");
            return;
        }
        selected ??= [];
        foreach (var nameToEnable in selected)
        {
            var set = sets.SingleOrDefault(s => s.EntitySet.Name == nameToEnable);
            if (set == null) throw new InvalidDataException($"Unknown entity set {nameToEnable} for {selectionKey}");
            foreach (var child in set.Entities) AddEntity(child, source + "/" + nameToEnable);
        }
    }

    private Bounds? ReadBounds(string filename, uint? drawableHash)
    {
        string key = filename + "#" + drawableHash;
        if (bounds.TryGetValue(key, out var cached)) return cached;
        bool fromGame = filename.StartsWith("game:", StringComparison.Ordinal);
        var data = fromGame ? null : Read(filename);
        uint asset = fromGame ? Convert.ToUInt32(Path.GetFileNameWithoutExtension(filename)[5..], 16) : 0;
        Bounds? result;
        switch (Path.GetExtension(filename).ToLowerInvariant())
        {
            case ".ybn": var ybn = fromGame ? game!.Load<YbnFile>(asset, ".ybn") : new YbnFile(); if (!fromGame) ybn.Load(data); result = ybn.Bounds; break;
            case ".ydr": var ydr = fromGame ? game!.Load<YdrFile>(asset, ".ydr") : new YdrFile(); if (!fromGame) ydr.Load(data); result = ydr.Drawable?.Bound; break;
            case ".ydd":
                var ydd = fromGame ? game!.Load<YddFile>(asset, ".ydd") : new YddFile(); if (!fromGame) ydd.Load(data);
                if (!drawableHash.HasValue || !ydd.Dict.TryGetValue(drawableHash.Value, out var drawable))
                    throw new InvalidDataException($"Drawable {drawableHash} not found in {filename}");
                result = drawable.Bound; break;
            case ".yft":
                var yft = fromGame ? game!.Load<YftFile>(asset, ".yft") : new YftFile(); if (!fromGame) yft.Load(data);
                if (yft.Fragment?.PhysicsLODGroup?.PhysicsLOD1?.Bound != null)
                    throw new NotSupportedException($"Fragment physics requires validated bone/child transforms: {filename}");
                result = yft.Fragment?.Drawable?.Bound; break;
            default: throw new NotSupportedException($"Unsupported collision input: {filename}");
        }
        bounds[key] = result;
        return result;
    }

    public void AddBounds(Bounds bound, Matrix parent, string source)
    {
        var transform = bound.Transform * parent;
        if (bound is BoundComposite composite)
        {
            foreach (var child in composite.Children?.data_items ?? [])
            {
                if (child == null) continue;
                Masks.Add(new { source, type = child.Type.ToString(),
                    first = new[] { (uint)child.CompositeFlags1.Flags1, (uint)child.CompositeFlags1.Flags2 },
                    second = new[] { (uint)child.CompositeFlags2.Flags1, (uint)child.CompositeFlags2.Flags2 } });
                // Preserve both sets in the report; exclude only when neither actor mask includes PED.
                bool ped1 = (child.CompositeFlags1.Flags2 & EBoundCompositeFlags.PED) != 0;
                bool ped2 = (child.CompositeFlags2.Flags2 & EBoundCompositeFlags.PED) != 0;
                if (!ped1 && !ped2) { Exclusions.Add($"Non-ped collision: {source}/{child.Type}"); continue; }
                if (ped1 != ped2) Issues.Add($"Mismatched PED masks: {source}/{child.Type}");
                AddBounds(child, transform, source);
            }
            return;
        }
        if (bound is BoundGeometry geometry)
        {
            foreach (var polygon in geometry.Polygons ?? [])
            {
                if (polygon is not BoundPolygonTriangle triangle)
                {
                    Issues.Add($"Unsupported collision primitive {polygon.Type}: {source}. Supply triangulated collision.");
                    continue;
                }
                var indices = new[] { triangle.vertIndex1, triangle.vertIndex2, triangle.vertIndex3 };
                if (indices.Any(i => i < 0 || i >= geometry.Vertices.Length)) throw new InvalidDataException($"Invalid collision indices: {source}");
                // GetVertexPos already applies Transform; use raw vertices here to avoid applying it twice.
                var vertices = indices.Select(i => Vector3.TransformCoordinate(geometry.Vertices[i] + geometry.CenterGeom, transform)).ToArray();
                if (Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]).LengthSquared() > 1e-12f)
                    Triangles.Add(vertices);
            }
        }
        else if (bound is BoundBox)
        {
            var corners = new BoundingBox(bound.BoxMin, bound.BoxMax).GetCorners().Select(v => Vector3.TransformCoordinate(v, transform)).ToArray();
            // SharpDX corner order is verified by the transformed-box self-test.
            int[] faces = [0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 3,7,6, 3,6,2, 1,2,6, 1,6,5, 0,4,7, 0,7,3];
            for (int i = 0; i < faces.Length; i += 3) Triangles.Add([corners[faces[i]], corners[faces[i + 1]], corners[faces[i + 2]]]);
        }
        else Issues.Add($"Unsupported bounds type {bound.Type}: {source}. Supply triangulated collision.");
    }
}
