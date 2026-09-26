using CodeWalker.GameFiles;
using SharpDX;

namespace BLRP.NavMesh;

internal sealed record EntitySetChoice(string Placement, string Name)
{
    public override string ToString() => $"{Placement} / {Name}";
}

internal sealed record MapDetails(Vector3 Min, Vector3 Max, EntitySetChoice[] Sets, string[] Unresolved,
    int CollisionBoundsCount, int MetadataBoundsCount);

internal static class MapSelection
{
    public static MapDetails Read(BakeSettings settings, GameSourceManifest? gameSources = null)
    {
        if (settings.Ymaps.Length == 0) throw new InvalidDataException("Select at least one map first.");
        var scene = new CollisionScene(settings);
        var game = gameSources != null || settings.GameSourceFile.Length > 0 ? new GameSource(settings, scene, gameSources) : null;
        var archetypes = new Dictionary<uint, Archetype>();
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var collisions = new Dictionary<uint, string>();
        foreach (string path in settings.ResourceRoots.SelectMany(r => Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories)))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".ytyp" or ".ybn" or ".ydr" or ".ydd" or ".yft")) continue;
            JenkIndex.Ensure(Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
            string name = Path.GetFileName(path);
            if (owners.TryGetValue(name, out var previousPath))
            {
                if (CollisionScene.Hash(previousPath) != CollisionScene.Hash(path)) throw new InvalidDataException($"Conflicting resource asset: {name}");
                continue;
            }
            owners[name] = path;
            if (extension == ".ybn") collisions[JenkHash.GenHash(Path.GetFileNameWithoutExtension(path).ToLowerInvariant())] = path;
            if (extension != ".ytyp") continue;
            var file = new YtypFile(); file.Load(scene.Read(path));
            foreach (var archetype in file.AllArchetypes ?? [])
            {
                if (archetypes.TryGetValue(archetype.Hash, out var previous) && previous.Ytyp != file)
                    throw new InvalidDataException($"Duplicate archetype {archetype.Name}; reconcile the resource selection first.");
                archetypes[archetype.Hash] = archetype;
            }
        }
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        var sets = new List<EntitySetChoice>(); var unresolved = new List<string>();
        int collisionCount = 0, metadataCount = 0;
        foreach (string path in settings.Ymaps)
        {
            var file = new YmapFile(); file.Load(scene.Read(path));
            foreach (var entity in file.AllEntities ?? [])
            {
                if ((entity.CEntityDef.flags & 4) != 0 || entity.CEntityDef.lodLevel is not (rage__eLodType.LODTYPES_DEPTH_HD or rage__eLodType.LODTYPES_DEPTH_ORPHANHD)) continue;
                uint hash = entity.CEntityDef.archetypeName;
                var archetype = archetypes.GetValueOrDefault(hash) ?? game?.FindArchetype(hash);
                if (archetype == null)
                {
                    unresolved.Add($"Cannot locate object definition {entity.CEntityDef.archetypeName} (0x{hash:X8}) in {Path.GetFileName(path)}. " +
                        (game == null ? "Use Game archives > LOAD BASE GAME, or add the resource that supplies it." : "Add its owning resource or the matching DLC archive to Game archives.") +
                        " The area below excludes this object; its collision must still be resolved before building.");
                    continue;
                }
                entity.SetArchetype(archetype);
                BoundingBox area = new(entity.BBMin, entity.BBMax);
                bool usedCollision = false;
                if (archetype is MloArchetype)
                {
                    try
                    {
                        YbnFile? collision = null;
                        if (collisions.TryGetValue(hash, out string? collisionPath))
                        { collision = new YbnFile(); collision.Load(scene.Read(collisionPath)); }
                        else if (game?.Contains(hash, ".ybn") == true) collision = game.Load<YbnFile>(hash, ".ybn");
                        if (collision?.Bounds != null)
                        {
                            var transform = Matrix.Scaling(entity.Scale) * Matrix.RotationQuaternion(entity.Orientation) * Matrix.Translation(entity.Position);
                            area = WorldBounds(collision.Bounds, transform); usedCollision = true;
                        }
                        else unresolved.Add($"No readable interior YBN for {archetype.Name}. Using its metadata bounds; review the replacement area.");
                    }
                    catch (InvalidDataException e)
                    { unresolved.Add($"Cannot fit {archetype.Name} to collision: {e.Message} Using metadata bounds; review the replacement area."); }
                }
                min = Vector3.Min(min, area.Minimum); max = Vector3.Max(max, area.Maximum);
                if (usedCollision) collisionCount++; else metadataCount++;
                foreach (var set in entity.MloInstance?.EntitySets ?? [])
                    sets.Add(new EntitySetChoice(Path.GetFileName(path) + ":" + entity.Index, set.EntitySet.Name));
            }
        }
        if (collisionCount + metadataCount == 0) throw new InvalidDataException("No map bounds could be resolved. Select the owning resource or configure Game archives, then try again.");
        if (game != null && !game.Manifest.ValidatedForBuild)
            unresolved.Add("Game archives resolved object definitions for this preview. Their compatibility with the target build remains unverified.");
        return new MapDetails(min, max, sets.ToArray(), unresolved.Distinct().ToArray(), collisionCount, metadataCount);
    }

    internal static BoundingBox WorldBounds(Bounds bound, Matrix placement)
    {
        var corners = new BoundingBox(bound.BoxMin, bound.BoxMax).GetCorners()
            .Select(v => Vector3.TransformCoordinate(v, bound.Transform * placement)).ToArray();
        if (corners.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
            throw new InvalidDataException("Collision has non-finite world bounds.");
        return BoundingBox.FromPoints(corners);
    }
}
