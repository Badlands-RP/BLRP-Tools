using CodeWalker.GameFiles;
using SharpDX;

namespace BLRP.NavMesh;

internal sealed record EntitySetChoice(string Placement, string Name)
{
    public override string ToString() => $"{Placement} / {Name}";
}

internal sealed record MapDetails(Vector3 Min, Vector3 Max, EntitySetChoice[] Sets, string[] Unresolved);

internal static class MapSelection
{
    public static MapDetails Read(string[] resources, string[] maps)
    {
        if (maps.Length == 0) throw new InvalidDataException("Select at least one map first.");
        var archetypes = new Dictionary<uint, Archetype>();
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in resources.SelectMany(r => Directory.EnumerateFiles(r, "*.ytyp", SearchOption.AllDirectories)))
        {
            string name = Path.GetFileName(path);
            if (owners.TryGetValue(name, out var previousPath))
            {
                if (CollisionScene.Hash(previousPath) != CollisionScene.Hash(path)) throw new InvalidDataException($"Conflicting resource asset: {name}");
                continue;
            }
            owners[name] = path;
            var file = new YtypFile(); file.Load(File.ReadAllBytes(path));
            foreach (var archetype in file.AllArchetypes ?? [])
            {
                if (archetypes.TryGetValue(archetype.Hash, out var previous) && previous.Ytyp != file)
                    throw new InvalidDataException($"Duplicate archetype {archetype.Name}; reconcile the resource selection first.");
                archetypes[archetype.Hash] = archetype;
            }
        }
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        var sets = new List<EntitySetChoice>(); var unresolved = new List<string>();
        int count = 0;
        foreach (string path in maps)
        {
            var file = new YmapFile(); file.Load(File.ReadAllBytes(path));
            foreach (var entity in file.AllEntities ?? [])
            {
                if (!archetypes.TryGetValue(entity.CEntityDef.archetypeName, out var archetype))
                {
                    unresolved.Add($"Bounds unavailable for {entity.CEntityDef.archetypeName} in {Path.GetFileName(path)}. Set the area manually or include its owning resource.");
                    continue;
                }
                entity.SetArchetype(archetype);
                min = Vector3.Min(min, entity.BBMin); max = Vector3.Max(max, entity.BBMax); count++;
                foreach (var set in entity.MloInstance?.EntitySets ?? [])
                    sets.Add(new EntitySetChoice(Path.GetFileName(path) + ":" + entity.Index, set.EntitySet.Name));
            }
        }
        if (count == 0) throw new InvalidDataException("No custom archetype bounds were found. Include the owning resource or enter the world area manually.");
        return new MapDetails(min, max, sets.ToArray(), unresolved.Distinct().ToArray());
    }
}
