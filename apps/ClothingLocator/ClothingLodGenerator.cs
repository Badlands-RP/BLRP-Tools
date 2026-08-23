using CodeWalker.GameFiles;
using Meshoptimizer;

namespace BLRP.ClothingLocator;

internal sealed record ClothingLodStats(long High, long Medium, long Low)
{
    public bool HasMedium => Medium > 0;
    public bool HasLow => Low > 0;
}

internal sealed record ClothingLodResult(string CandidatePath, ClothingLodStats Before, ClothingLodStats After);

internal static class ClothingLodGenerator
{
    private const uint SimplifyLockBorder = 1;
    private const uint SimplifyRegularize = 16;

    public static ClothingLodStats Analyze(string path)
    {
        YddFile file = Load(path);
        return Analyze(file);
    }

    public static ClothingLodResult Generate(string sourcePath, float mediumRatio, float lowRatio)
    {
        if (mediumRatio is <= 0 or >= 1 || lowRatio is <= 0 or >= 1 || lowRatio >= mediumRatio)
            throw new ArgumentOutOfRangeException(nameof(mediumRatio), "LOD ratios must satisfy 0 < Low < Medium < 100%.");

        YddFile candidate = Load(sourcePath);
        YddFile mediumSource = Load(sourcePath);
        YddFile lowSource = Load(sourcePath);
        Drawable[] candidateDrawables = candidate.DrawableDict?.Drawables?.data_items ?? [];
        Drawable[] mediumDrawables = mediumSource.DrawableDict?.Drawables?.data_items ?? [];
        Drawable[] lowDrawables = lowSource.DrawableDict?.Drawables?.data_items ?? [];
        if (candidateDrawables.Length == 0) throw new InvalidDataException("The YDD contains no drawables.");

        ClothingLodStats before = Analyze(candidate);
        for (int index = 0; index < candidateDrawables.Length; index++)
        {
            Drawable target = candidateDrawables[index];
            DrawableModelsBlock models = target.DrawableModels ?? throw new InvalidDataException("A drawable has no model block.");
            DrawableModel[] high = models.High ?? [];
            if (high.Length == 0) continue;
            if (models.Med is not { Length: > 0 })
                models.Med = SimplifyModels(mediumDrawables[index].DrawableModels?.High ?? [], mediumRatio, 0.025f, target);
            if (models.Low is not { Length: > 0 })
            {
                DrawableModel[] existingMedium = lowDrawables[index].DrawableModels?.Med ?? [];
                bool deriveFromMedium = existingMedium.Length > 0;
                models.Low = SimplifyModels(
                    deriveFromMedium ? existingMedium : lowDrawables[index].DrawableModels?.High ?? [],
                    deriveFromMedium ? Math.Clamp(lowRatio / mediumRatio, 0.05f, 0.9f) : lowRatio,
                    0.075f,
                    target);
            }
        }

        string candidatePath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(sourcePath)}-lod-review-{Guid.NewGuid():N}.ydd");
        File.WriteAllBytes(candidatePath, candidate.Save());
        YddFile savedCandidate = Load(candidatePath);
        ValidateHighUnchanged(Load(sourcePath), savedCandidate);
        ValidateIndices(savedCandidate);
        ClothingLodStats after = Analyze(savedCandidate);
        if (!after.HasMedium || !after.HasLow)
            throw new InvalidDataException("The generated candidate is missing a Medium or Low LOD.");
        if (!before.HasMedium && after.Medium >= before.High)
            throw new InvalidDataException("Medium LOD generation did not reduce the model.");
        if (!before.HasLow && after.Low >= (after.HasMedium ? after.Medium : before.High))
            throw new InvalidDataException("Low LOD generation did not reduce the model below Medium.");
        return new ClothingLodResult(candidatePath, before, after);
    }

    public static string Apply(string sourcePath, string candidatePath, string rootPath)
    {
        YddFile source = Load(sourcePath);
        YddFile candidate = Load(candidatePath);
        ValidateHighUnchanged(source, candidate);
        ValidateIndices(candidate);
        string backupDirectory = Path.Combine(rootPath, ".clothing-locator-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"), "lod-generation");
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, backupPath, false);
        string temporary = sourcePath + ".blrp-lod-applying";
        File.Copy(candidatePath, temporary, true);
        File.Move(temporary, sourcePath, true);
        return backupPath;
    }

    internal static bool SelfTest(string rootPath)
    {
        if (File.Exists(rootPath)) return SelfTestFile(rootPath);
        foreach (string path in Directory.EnumerateFiles(rootPath, "*.ydd", SearchOption.AllDirectories))
        {
            ClothingLodStats? inspected = TryAnalyze(path);
            if (inspected is null || inspected.High < 100 || inspected.HasMedium && inspected.HasLow) continue;
            return SelfTestFile(path);
        }
        throw new InvalidDataException("No YDD with a missing Medium or Low LOD was found for the LOD generator self-test.");
    }

    private static bool SelfTestFile(string path)
    {
        ClothingLodStats before = Analyze(path);
        ClothingLodResult result = Generate(path, 0.5f, 0.2f);
        try
        {
            return result.After.High == before.High &&
                (before.HasMedium || result.After.Medium is > 0 && result.After.Medium < before.High) &&
                (before.HasLow || result.After.Low is > 0 && result.After.Low < result.After.Medium);
        }
        finally
        {
            if (File.Exists(result.CandidatePath)) File.Delete(result.CandidatePath);
        }
    }

    private static ClothingLodStats? TryAnalyze(string path)
    {
        try { return Analyze(path); }
        catch { return null; }
    }

    private static DrawableModel[] SimplifyModels(DrawableModel[] models, float ratio, float maxError, Drawable targetDrawable)
    {
        foreach (DrawableModel model in models)
        {
            foreach (DrawableGeometry geometry in model.Geometries ?? [])
            {
                SimplifyGeometry(geometry, ratio, maxError);
                ShaderFX[] shaders = targetDrawable.ShaderGroup?.Shaders?.data_items ?? [];
                if (geometry.ShaderID < shaders.Length) geometry.Shader = shaders[geometry.ShaderID];
            }
        }
        return models;
    }

    private static void SimplifyGeometry(DrawableGeometry geometry, float ratio, float maxError)
    {
        VertexData vertices = geometry.VertexData ?? throw new InvalidDataException("A geometry has no vertex data.");
        ushort[] sourceUshort = geometry.IndexBuffer?.Indices ?? [];
        if (sourceUshort.Length < 6 || vertices.VertexCount < 3) return;

        uint[] source = sourceUshort.Select(value => (uint)value).ToArray();
        float[] positions = new float[vertices.VertexCount * 3];
        for (int index = 0; index < vertices.VertexCount; index++)
        {
            SharpDX.Vector3 position = vertices.GetVector3(index, 0);
            positions[index * 3] = position.X;
            positions[index * 3 + 1] = position.Y;
            positions[index * 3 + 2] = position.Z;
        }

        int targetCount = Math.Clamp((int)(source.Length * ratio) / 3 * 3, 3, source.Length - 3);
        uint[] destination = new uint[source.Length];
        float resultError = 0;
        int resultCount = Meshopt.Simplify(
            destination, source, in positions[0], vertices.VertexCount, sizeof(float) * 3,
            targetCount, maxError, SimplifyLockBorder | SimplifyRegularize, out resultError);
        if (resultCount < 3 || resultCount >= source.Length) return;
        CompactGeometry(geometry, destination.AsSpan(0, resultCount));
    }

    private static void CompactGeometry(DrawableGeometry geometry, ReadOnlySpan<uint> sourceIndices)
    {
        var remap = new Dictionary<uint, ushort>();
        var oldVertices = new List<int>();
        ushort[] indices = new ushort[sourceIndices.Length];
        for (int index = 0; index < sourceIndices.Length; index++)
        {
            uint source = sourceIndices[index];
            if (!remap.TryGetValue(source, out ushort target))
            {
                target = checked((ushort)remap.Count);
                remap.Add(source, target);
                oldVertices.Add(checked((int)source));
            }
            indices[index] = target;
        }

        VertexBuffer buffer = geometry.VertexBuffer;
        VertexData? first = buffer.Data1 == null ? null : CompactVertices(buffer.Data1, oldVertices);
        VertexData? second = buffer.Data2 == null
            ? null
            : ReferenceEquals(buffer.Data1, buffer.Data2) ? first : CompactVertices(buffer.Data2, oldVertices);
        buffer.Data1 = first;
        buffer.Data2 = second;
        geometry.VertexData = first ?? second ?? throw new InvalidDataException("A geometry has no vertex stream.");
        geometry.IndexBuffer.Indices = indices;
    }

    private static VertexData CompactVertices(VertexData source, IReadOnlyList<int> oldVertices)
    {
        byte[] bytes = new byte[oldVertices.Count * source.VertexStride];
        for (int index = 0; index < oldVertices.Count; index++)
            Buffer.BlockCopy(source.VertexBytes, oldVertices[index] * source.VertexStride, bytes, index * source.VertexStride, source.VertexStride);
        return new VertexData
        {
            VertexStride = source.VertexStride,
            VertexCount = oldVertices.Count,
            Info = source.Info,
            VertexType = source.VertexType,
            VertexBytes = bytes
        };
    }

    private static ClothingLodStats Analyze(YddFile file)
    {
        Drawable[] drawables = file.DrawableDict?.Drawables?.data_items ?? [];
        long Count(Func<DrawableModelsBlock, DrawableModel[]?> select) => drawables.Sum(drawable =>
            (select(drawable.DrawableModels ?? new DrawableModelsBlock()) ?? []).Sum(model =>
                (model.Geometries ?? []).Sum(geometry => (long)(geometry.IndexBuffer?.Indices?.Length ?? 0) / 3)));
        return new ClothingLodStats(Count(models => models.High), Count(models => models.Med), Count(models => models.Low));
    }

    private static void ValidateHighUnchanged(YddFile source, YddFile candidate)
    {
        Drawable[] originals = source.DrawableDict?.Drawables?.data_items ?? [];
        Drawable[] generated = candidate.DrawableDict?.Drawables?.data_items ?? [];
        if (originals.Length != generated.Length)
            throw new InvalidDataException("The candidate changed the drawable count.");

        for (int drawableIndex = 0; drawableIndex < originals.Length; drawableIndex++)
        {
            DrawableModel[] originalModels = originals[drawableIndex].DrawableModels?.High ?? [];
            DrawableModel[] generatedModels = generated[drawableIndex].DrawableModels?.High ?? [];
            if (originalModels.Length != generatedModels.Length)
                throw new InvalidDataException("The candidate changed the High LOD model count.");
            for (int modelIndex = 0; modelIndex < originalModels.Length; modelIndex++)
            {
                DrawableGeometry[] originalGeometry = originalModels[modelIndex].Geometries ?? [];
                DrawableGeometry[] generatedGeometry = generatedModels[modelIndex].Geometries ?? [];
                if (originalGeometry.Length != generatedGeometry.Length)
                    throw new InvalidDataException("The candidate changed the High LOD geometry count.");
                for (int geometryIndex = 0; geometryIndex < originalGeometry.Length; geometryIndex++)
                {
                    DrawableGeometry before = originalGeometry[geometryIndex];
                    DrawableGeometry after = generatedGeometry[geometryIndex];
                    if (!(before.IndexBuffer?.Indices ?? []).SequenceEqual(after.IndexBuffer?.Indices ?? []) ||
                        !(before.VertexData?.VertexBytes ?? []).SequenceEqual(after.VertexData?.VertexBytes ?? []))
                        throw new InvalidDataException("The candidate changed High LOD geometry.");
                }
            }
        }
    }

    private static void ValidateIndices(YddFile file)
    {
        foreach (Drawable drawable in file.DrawableDict?.Drawables?.data_items ?? [])
        foreach (DrawableModel model in (drawable.DrawableModels?.Med ?? []).Concat(drawable.DrawableModels?.Low ?? []))
        foreach (DrawableGeometry geometry in model.Geometries ?? [])
        {
            int vertexCount = geometry.VertexData?.VertexCount ?? 0;
            if ((geometry.IndexBuffer?.Indices ?? []).Any(index => index >= vertexCount))
                throw new InvalidDataException("A generated LOD contains an invalid vertex index.");
        }
    }

    private static YddFile Load(string path)
    {
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".ydd", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Select a valid clothing YDD.", path);
        var file = new YddFile();
        file.Load(File.ReadAllBytes(path));
        return file;
    }
}
