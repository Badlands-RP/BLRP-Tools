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
        if (data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "FXAP")
            throw new InvalidDataException($"Escrow-protected {Path.GetFileName(path)}: readable collision from the map author is required; previews omit this geometry. ({path})");
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
                if (files.TryGetValue(key, out var previous))
                {
                    if (Hash(previous) != Hash(file)) Issues.Add($"Conflicting resource asset: {name}{extension}. Preview uses {previous}; also found {file}. Resolve the active resource versions before exporting.");
                    continue;
                }
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
                {
                    Issues.Add($"Conflicting archetype {archetype.Name}: {previous.Ytyp?.Name} and {ytyp.Name}. Preview keeps the first selected definition; resolve the active definition before exporting.");
                    continue;
                }
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
        uint asset = archetype.BaseArchetypeDef.assetName;
        if (asset == 0) asset = hash;
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
            collisionFile ??= Find(asset, ".ydr");
            collisionFile ??= Find(asset, ".yft");
        }
        if (collisionFile == null) Issues.Add($"Missing collision/drawable for {archetype.Name} ({source}).");
        else
        {
            try
            {
                var collision = ReadBounds(collisionFile, asset);
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
                result = FragmentBounds(yft, filename); break;
            default: throw new NotSupportedException($"Unsupported collision input: {filename}");
        }
        bounds[key] = result;
        return result;
    }

    internal static Bounds? FragmentBounds(YftFile file, string source)
    {
        var fragment = file.Fragment;
        var lod = fragment?.PhysicsLODGroup?.PhysicsLOD1;
        var physics = lod?.Archetype1?.Bound;
        if (physics == null) return fragment?.Drawable?.Bound;
        if (physics is not BoundComposite composite)
            throw new NotSupportedException($"Unsupported fragment physics root: {source}");
        var children = composite.Children?.data_items ?? [];
        var parts = lod!.Children?.data_items ?? [];
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is not { } bound) continue;
            if (i >= parts.Length || parts[i]?.Drawable1 == null ||
                fragment!.Drawable?.Skeleton?.BonesMap?.TryGetValue(parts[i].BoneTag, out var bone) != true)
                throw new NotSupportedException($"Fragment physics has no rest-pose bone mapping for child {i}: {source}");
            // Match Sollumz's pristine collision placement: fragment bound matrix followed by the absolute bind bone.
            // CodeWalker decodes this into the composite child transform. Verify, rather than applying it twice.
            Matrix expected = parts[i].Drawable1.FragMatrix.ToMatrix() * bone!.AbsTransform;
            float error = expected.ToArray().Zip(bound.Transform.ToArray(), (a, b) => Math.Abs(a - b)).Max();
            if (!float.IsFinite(error) || error > 0.0001f)
                throw new NotSupportedException($"Fragment physics rest-pose transform differs for child {i} ({error:G4}): {source}");
        }
        return composite;
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
                Vector3 Vertex(int index)
                {
                    if (index < 0 || index >= geometry.Vertices.Length) throw new InvalidDataException($"Invalid collision indices: {source}");
                    return geometry.Vertices[index] + geometry.CenterGeom;
                }
                switch (polygon)
                {
                    case BoundPolygonTriangle triangle:
                        AddTriangle([Vertex(triangle.vertIndex1), Vertex(triangle.vertIndex2), Vertex(triangle.vertIndex3)], transform); break;
                    case BoundPolygonSphere sphere:
                        AddRound(Vertex(sphere.sphereIndex), Vertex(sphere.sphereIndex), sphere.sphereRadius, true, transform); break;
                    case BoundPolygonCapsule capsule:
                        AddRound(Vertex(capsule.capsuleIndex1), Vertex(capsule.capsuleIndex2), capsule.capsuleRadius, true, transform); break;
                    case BoundPolygonCylinder cylinder:
                        AddRound(Vertex(cylinder.cylinderIndex1), Vertex(cylinder.cylinderIndex2), cylinder.cylinderRadius, false, transform); break;
                    case BoundPolygonBox box:
                        var a = Vertex(box.boxIndex1); var b = Vertex(box.boxIndex2); var c = Vertex(box.boxIndex3); var d = Vertex(box.boxIndex4);
                        var edge = ((c + d) - (a + b)) * 0.5f; // CodeWalker's four diagonal corner representation.
                        AddConvex(CollisionPrimitives.Box(a, edge, c - edge - a, d - edge - a), (a + b + c + d) * 0.25f, transform); break;
                    default: Issues.Add($"Unsupported collision primitive {polygon.Type}: {source}."); break;
                }
            }
        }
        else if (bound is BoundBox)
        {
            var corners = new BoundingBox(bound.BoxMin, bound.BoxMax).GetCorners().Select(v => Vector3.TransformCoordinate(v, transform)).ToArray();
            // SharpDX corner order is verified by the transformed-box self-test.
            int[] faces = [0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 3,7,6, 3,6,2, 1,2,6, 1,6,5, 0,4,7, 0,7,3];
            for (int i = 0; i < faces.Length; i += 3) Triangles.Add([corners[faces[i]], corners[faces[i + 1]], corners[faces[i + 2]]]);
        }
        else if (bound is BoundSphere) AddRound(bound.SphereCenter, bound.SphereCenter, bound.SphereRadius, true, transform);
        else if (bound is BoundCapsule)
        {
            var extent = new Vector3(0, Math.Max(0, bound.SphereRadius - bound.Margin), 0);
            AddRound(bound.SphereCenter - extent, bound.SphereCenter + extent, bound.Margin, true, transform);
        }
        else if (bound is BoundCylinder)
        {
            var extent = new Vector3(0, (bound.BoxMax.Y - bound.BoxMin.Y) * 0.5f, 0);
            AddRound(bound.SphereCenter - extent, bound.SphereCenter + extent, (bound.BoxMax.X - bound.BoxMin.X) * 0.5f, false, transform);
        }
        else Issues.Add($"Unsupported bounds type {bound.Type}: {source}. Supply triangulated collision.");
    }

    private void AddTriangle(Vector3[] points, Matrix transform)
    {
        var vertices = points.Select(p => Vector3.TransformCoordinate(p, transform)).ToArray();
        if (vertices.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))) throw new InvalidDataException("Non-finite collision geometry.");
        if (Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]).LengthSquared() > 1e-12f) Triangles.Add(vertices);
    }

    private void AddConvex(IEnumerable<Vector3[]> triangles, Vector3 center, Matrix transform)
    {
        var worldCenter = Vector3.TransformCoordinate(center, transform);
        foreach (var triangle in triangles)
        {
            var p = triangle.Select(v => Vector3.TransformCoordinate(v, transform)).ToArray();
            if (Vector3.Dot(Vector3.Cross(p[1] - p[0], p[2] - p[0]), (p[0] + p[1] + p[2]) / 3 - worldCenter) < 0) (p[1], p[2]) = (p[2], p[1]);
            AddTriangle(p, Matrix.Identity);
        }
    }

    private void AddRound(Vector3 a, Vector3 b, float radius, bool capsule, Matrix transform)
    {
        float stretch = MathF.Sqrt(transform.Row1.LengthSquared() + transform.Row2.LengthSquared() + transform.Row3.LengthSquared());
        AddConvex(CollisionPrimitives.Round(a, b, radius, capsule, CollisionPrimitives.MaxWorldError / stretch), (a + b) * 0.5f, transform);
    }
}
