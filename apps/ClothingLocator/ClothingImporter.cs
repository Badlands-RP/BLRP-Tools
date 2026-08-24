using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace BLRP.ClothingLocator;

internal sealed record ClothingImportPlan(
    string RootPath,
    Gender Gender,
    ComponentDefinition Component,
    string ModelPath,
    IReadOnlyList<string> TexturePaths,
    bool HasSkin,
    int Pack,
    int PackDrawableIndex,
    int GlobalIndex,
    int ExistingCount,
    string CollectionName,
    string YmtPath,
    string TargetDirectory,
    string ModelFileName,
    IReadOnlyList<string> TextureFileNames,
    PedFile PedFile,
    MCPVDrawblData? DrawableTemplate = null,
    MCComponentInfo? ComponentInfoTemplate = null,
    CPedPropMetaData? PropTemplate = null,
    bool CopyRawAssets = false)
{
    public int CountAfterImport => ExistingCount + 1;
    public int RemainingSlots => ClothingImporter.MaxDrawablesPerType - CountAfterImport;
}

internal sealed record ClothingTextureImportResult(string TexturePath, int TextureCount, string Compression);
internal sealed record ClothingMetadataUpdateResult(string BackupDirectory, string CreatureMetadataPath);
internal sealed record ClothingModelQuality(
    string Summary,
    string Details,
    long? HighPolygons = null,
    long? MediumPolygons = null,
    long? LowPolygons = null);

internal static class ClothingImporter
{
    public const int MaxDrawablesPerType = 128;

    internal static string DumpYmtXml(string path) => MetaXml.GetXml(LoadPedFile(path).Meta);

    internal static bool OwnsCloth(string rootPath, ClothingEntry entry)
    {
        if (entry.Component.IsProp) return false;
        PedFile ped = LoadPedFile(GetYmtPath(rootPath, entry));
        return GetDrawables(ped, entry.Component.Slot)?.ElementAtOrDefault(entry.RelativeIndex)?.Data.clothData.ownsCloth != 0;
    }

    private static readonly string[] RomanPacks = ["i", "ii", "iii", "iv", "v"];
    private static readonly Regex TextureNamePattern = new(
        @"_(?<variant>[a-z])_(?<suffix>uni|whi|bla|chi|lat|ara|kor|pak)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed record TextureLayout(string Path, char Variant, string Suffix);
    private static readonly uint PedHairSpiked = JenkHash.GenHash("ped_hair_spiked");
    private static readonly uint PedHairSpikedFile = JenkHash.GenHash("ped_hair_spiked.sps");
    private static readonly uint PedHairCutoutAlpha = JenkHash.GenHash("ped_hair_cutout_alpha");
    private static readonly uint PedHairCutoutAlphaFile = JenkHash.GenHash("ped_hair_cutout_alpha.sps");
    private static readonly uint HairOrderNumber = JenkHash.GenHash("ordernumber");
    private static readonly uint DiffuseSampler = JenkHash.GenHash("diffusesampler");

    public static ClothingImportPlan CreatePlan(
        string rootPath,
        Gender gender,
        ComponentDefinition component,
        string modelPath,
        IReadOnlyList<string> texturePaths,
        bool hasSkin) => CreatePlans(rootPath, gender, component, modelPath, texturePaths, hasSkin)[0];

    public static IReadOnlyList<ClothingImportPlan> CreatePlans(
        string rootPath,
        Gender gender,
        ComponentDefinition component,
        string modelPath,
        IReadOnlyList<string> texturePaths,
        bool hasSkin)
    {
        if (!File.Exists(modelPath) || !new[] { ".ydd", ".ydr" }.Contains(Path.GetExtension(modelPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Select a valid .ydd or .ydr model.", modelPath);
        }
        if (texturePaths.Count < 1 || texturePaths.Any(path => !File.Exists(path) || !Path.GetExtension(path).Equals(".ytd", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Select one or more valid .ytd texture files.");
        }
        if (component.IsProp)
        {
            return CreatePropPlans(rootPath, gender, component, modelPath, texturePaths);
        }
        IReadOnlyList<TextureLayout> textureLayout = CreateTextureLayout(texturePaths, hasSkin);

        string fullRoot = Path.GetFullPath(rootPath);
        var packData = new List<(int Pack, string Collection, string YmtPath, PedFile Ped, int Count)>();
        for (int pack = 1; pack <= RomanPacks.Length; pack++)
        {
            string collection = GetCollectionName(gender, pack);
            string ymtPath = Path.Combine(fullRoot, $"clothing_addon_{pack}", "stream", collection + ".ymt");
            if (!File.Exists(ymtPath))
            {
                continue;
            }

            PedFile ped = LoadPedFile(ymtPath);
            int count = GetDrawables(ped, component.Slot)?.Length ?? 0;
            packData.Add((pack, collection, ymtPath, ped, count));
        }

        if (packData.Count == 0)
        {
            throw new InvalidOperationException($"No {gender.ToString().ToLowerInvariant()} clothing addon YMT files were found.");
        }

        int lastUsedPack = packData.Where(item => item.Count > 0).Select(item => item.Pack).DefaultIfEmpty(packData[0].Pack).Max();
        var targets = packData
            .Where(item => item.Pack >= lastUsedPack && item.Count < MaxDrawablesPerType)
            .OrderBy(item => item.Pack)
            .ToArray();
        if (targets.Length == 0)
        {
            throw new InvalidOperationException(
                $"{gender} {component.Code.ToUpperInvariant()} has reached 128 drawables in the last available addon pack. " +
                "A new clothing_addon pack/YMT is required; it was not created automatically because it consumes another game YMT slot.");
        }

        var plans = new List<ClothingImportPlan>();
        foreach (var target in targets)
        {
            int relativeIndex = target.Count;
            string drawableSuffix = hasSkin ? "r" : "u";
            string modelBaseName = $"{component.Code}_{relativeIndex:000}_{drawableSuffix}";
            string modelFileName = $"{target.Collection}^{modelBaseName}.ydd";
            var textureFileNames = new List<string>();
            foreach (TextureLayout texture in textureLayout)
            {
                string textureName = $"{component.Code}_diff_{relativeIndex:000}_{texture.Variant}_{texture.Suffix}";
                textureFileNames.Add($"{target.Collection}^{textureName}.ytd");
            }

            string targetDirectory = Path.Combine(
                fullRoot,
                $"clothing_addon_{target.Pack}",
                "stream",
                target.Collection,
                component.Code);
            int priorCount = packData.Where(item => item.Pack < target.Pack).Sum(item => item.Count);
            int globalIndex = component.DefaultOffset(gender) + priorCount + relativeIndex;

            plans.Add(new ClothingImportPlan(
                fullRoot,
                gender,
                component,
                Path.GetFullPath(modelPath),
                textureLayout.Select(texture => texture.Path).ToArray(),
                hasSkin,
                target.Pack,
                relativeIndex,
                globalIndex,
                target.Count,
                target.Collection,
                target.YmtPath,
                targetDirectory,
                modelFileName,
                textureFileNames,
                target.Ped));
        }
        return plans;
    }

    private static IReadOnlyList<ClothingImportPlan> CreatePropPlans(
        string rootPath,
        Gender gender,
        ComponentDefinition component,
        string modelPath,
        IReadOnlyList<string> texturePaths)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        if (texturePaths.Count > 26)
        {
            throw new InvalidOperationException("A prop can have at most 26 textures.");
        }
        var packData = new List<(int Pack, string Collection, string YmtPath, PedFile Ped, int Count, int Total)>();
        for (int pack = 1; pack <= RomanPacks.Length; pack++)
        {
            string collection = GetCollectionName(gender, pack);
            string ymtPath = Path.Combine(fullRoot, $"clothing_addon_{pack}", "stream", collection + ".ymt");
            if (!File.Exists(ymtPath)) continue;
            PedFile ped = LoadPedFile(ymtPath);
            MCPedPropMetaData[] props = ped.VariationInfo?.PropInfo?.PropMetaData ?? [];
            packData.Add((pack, collection, ymtPath, ped,
                props.Count(item => item.Data.anchorId == component.Slot), props.Length));
        }
        if (packData.Count == 0)
        {
            throw new InvalidOperationException($"No {gender.ToString().ToLowerInvariant()} clothing addon YMT files were found.");
        }

        int lastUsedPack = packData.Where(item => item.Count > 0).Select(item => item.Pack).DefaultIfEmpty(packData[0].Pack).Max();
        var targets = packData.Where(item => item.Pack >= lastUsedPack && item.Count < MaxDrawablesPerType && item.Total < byte.MaxValue)
            .OrderBy(item => item.Pack).ToArray();
        if (targets.Length == 0)
        {
            throw new InvalidOperationException($"{gender} {component.Code.ToUpperInvariant()} has no available prop slots in the addon YMT files.");
        }

        CPedPropMetaData template = packData.SelectMany(item => item.Ped.VariationInfo?.PropInfo?.PropMetaData ?? [])
            .Where(item => item.Data.anchorId == component.Slot)
            .Select(item => item.Data)
            .LastOrDefault();
        if (template.audioId.Hash == 0) template.audioId = JenkHash.GenHash("none");
        string[] sourceTextures = texturePaths.Select(Path.GetFullPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var plans = new List<ClothingImportPlan>();
        foreach (var target in targets)
        {
            int relativeIndex = target.Count;
            string propCollection = target.Collection.Replace("_01_mp_", "_01_p_mp_", StringComparison.Ordinal);
            string modelFileName = $"{propCollection}^{component.Code}_{relativeIndex:000}.ydd";
            string[] textureFileNames = sourceTextures.Select((_, index) =>
                $"{propCollection}^{component.Code}_diff_{relativeIndex:000}_{(char)('a' + index)}.ytd").ToArray();
            string targetDirectory = Path.Combine(fullRoot, $"clothing_addon_{target.Pack}", "stream", propCollection, component.Code);
            int priorCount = packData.Where(item => item.Pack < target.Pack).Sum(item => item.Count);
            plans.Add(new ClothingImportPlan(
                fullRoot, gender, component, Path.GetFullPath(modelPath), sourceTextures, false,
                target.Pack, relativeIndex, component.DefaultOffset(gender) + priorCount + relativeIndex,
                target.Count, target.Collection, target.YmtPath, targetDirectory, modelFileName,
                textureFileNames, target.Ped, PropTemplate: template));
        }
        return plans;
    }

    public static IReadOnlyList<ClothingImportPlan> CreateDuplicatePlans(
        string rootPath,
        ClothingEntry source,
        ComponentDefinition targetComponent)
    {
        if (source.Component.IsProp || targetComponent.IsProp)
        {
            throw new NotSupportedException("Only clothing components can be duplicated.");
        }
        if (source.Component.Code.Equals(targetComponent.Code, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Choose a different destination category.");
        }

        string sourceYmt = Path.Combine(
            Path.GetFullPath(rootPath),
            $"clothing_addon_{source.Pack}",
            "stream",
            GetCollectionName(source.Gender, source.Pack) + ".ymt");
        PedFile sourcePed = LoadPedFile(sourceYmt);
        MCPVDrawblData sourceDrawable = GetDrawables(sourcePed, source.Component.Slot)?
            .ElementAtOrDefault(source.RelativeIndex)
            ?? throw new InvalidDataException("The source model has no matching YMT drawable entry.");
        if (sourceDrawable.NumAlternatives != 0)
        {
            throw new NotSupportedException("Models with alternate drawables cannot be duplicated automatically.");
        }
        if (sourceDrawable.Data.clothData.ownsCloth != 0)
        {
            throw new NotSupportedException("Cloth-simulated models require their companion cloth assets and cannot be duplicated automatically.");
        }

        string collection = GetCollectionName(source.Gender, source.Pack);
        string texturePrefix = $"{collection}^{source.Component.Code}_diff_{source.RelativeIndex:000}_";
        string sourceDirectory = Path.GetDirectoryName(source.FilePath)!;
        string[] texturePaths = Directory.EnumerateFiles(sourceDirectory, "*.ytd")
            .Where(path => Path.GetFileName(path).StartsWith(texturePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (texturePaths.Length == 0 || texturePaths.Length != (sourceDrawable.TexData?.Length ?? 0))
        {
            throw new InvalidDataException(
                $"The source YMT expects {sourceDrawable.TexData?.Length ?? 0} texture(s), but {texturePaths.Length} matching YTD file(s) were found.");
        }

        bool hasSkin = ((sourceDrawable.Data.propMask >> 4) & 3) == 1 ||
            Path.GetFileNameWithoutExtension(source.FilePath).EndsWith("_r", StringComparison.OrdinalIgnoreCase);
        MCComponentInfo? componentInfo = sourcePed.VariationInfo?.CompInfos?.FirstOrDefault(info =>
            info.Data.pedXml_compIdx == source.Component.Slot &&
            info.Data.pedXml_drawblIdx == source.RelativeIndex);

        return CreatePlans(rootPath, source.Gender, targetComponent, source.FilePath, texturePaths, hasSkin)
            .Select(plan => plan with
            {
                DrawableTemplate = sourceDrawable,
                ComponentInfoTemplate = componentInfo,
                CopyRawAssets = true
            })
            .ToArray();
    }

    public static IReadOnlyList<string> Import(ClothingImportPlan plan)
    {
        string modelAssetName = GetAssetName(plan.ModelFileName);
        string[] textureAssetNames = plan.TextureFileNames.Select(GetAssetName).ToArray();
        byte[] modelBytes = plan.CopyRawAssets || plan.Component.IsProp
            ? BuildDuplicateModel(plan.ModelPath, plan.Component, modelAssetName, textureAssetNames[0])
            : BuildModel(plan);
        byte[][] textureBytes = plan.TexturePaths
            .Select((path, index) => plan.CopyRawAssets || plan.Component.IsProp
                ? BuildDuplicateTexture(path, textureAssetNames[index])
                : BuildTexture(path))
            .ToArray();
        byte[] ymtBytes = plan.Component.IsProp
            ? AppendPropDrawable(plan.PedFile, plan.CollectionName, Path.Combine(Path.GetDirectoryName(plan.YmtPath)!, plan.CollectionName),
                plan.Component.Slot, plan.PackDrawableIndex, plan.TexturePaths.Count, plan.PropTemplate ?? new CPedPropMetaData())
            : AppendComponentDrawable(
                plan.PedFile,
                plan.CollectionName,
                Path.Combine(Path.GetDirectoryName(plan.YmtPath)!, plan.CollectionName),
                plan.Component.Slot,
                plan.HasSkin,
                plan.TexturePaths,
                plan.DrawableTemplate,
                plan.ComponentInfoTemplate);
        ValidateAppendPreservesExisting(plan.PedFile, ymtBytes, plan);
        (string Path, byte[] Bytes)? creatureMetadata = plan.Component.Code.Equals("feet", StringComparison.OrdinalIgnoreCase)
            ? BuildCreatureMetadata(plan.RootPath, plan.Gender, plan.Pack)
            : null;

        string modelTarget = Path.Combine(plan.TargetDirectory, plan.ModelFileName);
        string[] textureTargets = plan.TextureFileNames.Select(name => Path.Combine(plan.TargetDirectory, name)).ToArray();
        string[] allTargets = [modelTarget, .. textureTargets];
        string? collision = allTargets.FirstOrDefault(File.Exists);
        if (collision != null)
        {
            throw new IOException("Import target already exists: " + collision);
        }

        Directory.CreateDirectory(plan.TargetDirectory);
        string backupRoot = Path.Combine(plan.RootPath, ".clothing-locator-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backupRoot);
        File.Copy(plan.YmtPath, Path.Combine(backupRoot, Path.GetFileName(plan.YmtPath)), false);
        bool creatureExisted = creatureMetadata is { } existingCreature && File.Exists(existingCreature.Path);
        if (creatureExisted)
        {
            File.Copy(creatureMetadata!.Value.Path, Path.Combine(backupRoot, Path.GetFileName(creatureMetadata.Value.Path)), false);
        }

        var written = new List<string>();
        string temporaryYmt = plan.YmtPath + ".blrp-importing";
        string? temporaryCreature = creatureMetadata?.Path + ".blrp-importing";
        try
        {
            File.WriteAllBytes(modelTarget, modelBytes);
            written.Add(modelTarget);
            for (int index = 0; index < textureTargets.Length; index++)
            {
                File.WriteAllBytes(textureTargets[index], textureBytes[index]);
                written.Add(textureTargets[index]);
            }

            File.WriteAllBytes(temporaryYmt, ymtBytes);
            if (creatureMetadata is { } creature)
            {
                File.WriteAllBytes(temporaryCreature!, creature.Bytes);
            }
            File.Move(temporaryYmt, plan.YmtPath, true);
            written.Add(plan.YmtPath);
            if (creatureMetadata is { } updatedCreature)
            {
                File.Move(temporaryCreature!, updatedCreature.Path, true);
                written.Add(updatedCreature.Path);
            }
            return written;
        }
        catch
        {
            if (File.Exists(temporaryYmt)) File.Delete(temporaryYmt);
            if (temporaryCreature != null && File.Exists(temporaryCreature)) File.Delete(temporaryCreature);
            foreach (string path in written.Where(path =>
                         !path.Equals(plan.YmtPath, StringComparison.OrdinalIgnoreCase) &&
                         (creatureMetadata == null || !path.Equals(creatureMetadata.Value.Path, StringComparison.OrdinalIgnoreCase))))
            {
                if (File.Exists(path)) File.Delete(path);
            }
            if (written.Contains(plan.YmtPath, StringComparer.OrdinalIgnoreCase))
            {
                File.Copy(Path.Combine(backupRoot, Path.GetFileName(plan.YmtPath)), plan.YmtPath, true);
            }
            if (creatureMetadata is { } failedCreature && written.Contains(failedCreature.Path, StringComparer.OrdinalIgnoreCase))
            {
                string creatureBackup = Path.Combine(backupRoot, Path.GetFileName(failedCreature.Path));
                if (creatureExisted) File.Copy(creatureBackup, failedCreature.Path, true);
                else File.Delete(failedCreature.Path);
            }
            throw;
        }
    }

    public static string ReplaceClothing(
        string rootPath,
        ClothingEntry target,
        string sourceModelPath,
        IReadOnlyList<string> sourceTexturePaths,
        bool hasSkin)
    {
        if (target.Component.IsProp)
        {
            throw new NotSupportedException("Component models are supported; prop replacement is not available yet.");
        }
        string fullRoot = Path.GetFullPath(rootPath);
        string targetPath = Path.GetFullPath(target.FilePath);
        string sourcePath = Path.GetFullPath(sourceModelPath);
        string relativeTarget = Path.GetRelativePath(fullRoot, targetPath);
        if (Path.IsPathRooted(relativeTarget) || relativeTarget == ".." || relativeTarget.StartsWith(".." + Path.DirectorySeparatorChar))
        {
            throw new InvalidOperationException("The selected target is outside the EUP directory.");
        }
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("The drawable being replaced was not found.", targetPath);
        }
        if (!File.Exists(sourcePath) || !new[] { ".ydd", ".ydr" }.Contains(Path.GetExtension(sourcePath), StringComparer.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Select a valid replacement .ydd or .ydr model.", sourcePath);
        }
        if (targetPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The replacement model is already the selected drawable.");
        }
        if (sourceTexturePaths.Count < 1 || sourceTexturePaths.Any(path =>
                !File.Exists(path) || !Path.GetExtension(path).Equals(".ytd", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Select one or more valid replacement .ytd texture files.");
        }

        IReadOnlyList<TextureLayout> textureLayout = CreateTextureLayout(sourceTexturePaths, hasSkin);
        string collection = GetCollectionName(target.Gender, target.Pack);
        string ymtPath = Path.Combine(fullRoot, $"clothing_addon_{target.Pack}", "stream", collection + ".ymt");
        if (!File.Exists(ymtPath))
        {
            throw new FileNotFoundException("The YMT for the selected drawable was not found.", ymtPath);
        }
        PedFile ped = LoadPedFile(ymtPath);
        _ = GetDrawables(ped, target.Component.Slot)?.ElementAtOrDefault(target.RelativeIndex)
            ?? throw new InvalidDataException("The selected model has no matching YMT drawable entry.");

        string targetDirectory = Path.GetDirectoryName(targetPath)!;
        string texturePrefix = $"{target.Component.Code}_diff_{target.RelativeIndex:000}_";
        string[] oldTexturePaths = Directory.EnumerateFiles(targetDirectory, "*.ytd")
            .Where(path => GetAssetName(path).StartsWith(texturePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] textureAssetNames = textureLayout
            .Select(texture => $"{target.Component.Code}_diff_{target.RelativeIndex:000}_{texture.Variant}_{texture.Suffix}")
            .ToArray();
        string[] textureTargets = textureAssetNames
            .Select(name => Path.Combine(targetDirectory, $"{collection}^{name}.ytd"))
            .ToArray();
        byte[] modelBytes = BuildDuplicateModel(sourcePath, target.Component, GetAssetName(targetPath), textureAssetNames[0]);
        byte[][] textureBytes = textureLayout
            .Select((texture, index) => BuildDuplicateTexture(texture.Path, textureAssetNames[index]))
            .ToArray();
        byte[] ymtBytes = ReplaceComponentDrawable(
            ped,
            collection,
            Path.Combine(Path.GetDirectoryName(ymtPath)!, collection),
            target.Component.Slot,
            target.RelativeIndex,
            hasSkin,
            textureLayout.Select(texture => texture.Path).ToArray());

        string backupRoot = Path.Combine(fullRoot, ".clothing-locator-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backupRoot);
        string[] oldPaths = [targetPath, ymtPath, .. oldTexturePaths];
        foreach (string oldPath in oldPaths)
        {
            File.Copy(oldPath, Path.Combine(backupRoot, Path.GetFileName(oldPath)), false);
        }

        string temporaryModel = targetPath + ".blrp-replacing";
        string temporaryYmt = ymtPath + ".blrp-replacing";
        string[] temporaryTextures = textureTargets.Select(path => path + ".blrp-replacing").ToArray();
        try
        {
            File.WriteAllBytes(temporaryModel, modelBytes);
            File.WriteAllBytes(temporaryYmt, ymtBytes);
            for (int index = 0; index < temporaryTextures.Length; index++)
            {
                File.WriteAllBytes(temporaryTextures[index], textureBytes[index]);
            }

            File.Move(temporaryModel, targetPath, true);
            for (int index = 0; index < temporaryTextures.Length; index++)
            {
                File.Move(temporaryTextures[index], textureTargets[index], true);
            }
            File.Move(temporaryYmt, ymtPath, true);
            foreach (string obsoleteTexture in oldTexturePaths.Except(textureTargets, StringComparer.OrdinalIgnoreCase))
            {
                File.Delete(obsoleteTexture);
            }
            return backupRoot;
        }
        catch
        {
            foreach (string temporaryPath in new[] { temporaryModel, temporaryYmt }.Concat(temporaryTextures))
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            foreach (string oldPath in oldPaths)
            {
                File.Copy(Path.Combine(backupRoot, Path.GetFileName(oldPath)), oldPath, true);
            }
            foreach (string newTexture in textureTargets.Except(oldTexturePaths, StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(newTexture)) File.Delete(newTexture);
            }
            throw;
        }
    }

    public static float GetHeelHeight(string rootPath, ClothingEntry target)
    {
        if (!target.Component.Code.Equals("feet", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Heel height is only available for the FEET component.");
        }
        PedFile ped = LoadPedFile(GetYmtPath(rootPath, target));
        MCComponentInfo? info = ped.VariationInfo?.CompInfos?.FirstOrDefault(item =>
            item.Data.pedXml_compIdx == target.Component.Slot &&
            item.Data.pedXml_drawblIdx == target.RelativeIndex);
        return info?.Data.pedXml_expressionMods.f4 ?? 0;
    }

    public static ClothingMetadataUpdateResult SetHeelHeight(
        string rootPath,
        ClothingEntry target,
        float heelHeight)
    {
        if (!target.Component.Code.Equals("feet", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Heel height is only available for the FEET component.");
        }
        if (!float.IsFinite(heelHeight) || heelHeight < 0 || heelHeight > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(heelHeight), "Heel height must be between 0 and 3.");
        }

        string fullRoot = Path.GetFullPath(rootPath);
        string ymtPath = GetYmtPath(fullRoot, target);
        PedFile ped = LoadPedFile(ymtPath);
        _ = GetDrawables(ped, target.Component.Slot)?.ElementAtOrDefault(target.RelativeIndex)
            ?? throw new InvalidDataException("The selected model has no matching YMT drawable entry.");
        byte[] ymtBytes = UpdateComponentHeelHeight(
            ped,
            GetCollectionName(target.Gender, target.Pack),
            Path.Combine(Path.GetDirectoryName(ymtPath)!, GetCollectionName(target.Gender, target.Pack)),
            target.Component.Slot,
            target.RelativeIndex,
            heelHeight);
        (string creaturePath, byte[] creatureBytes) = BuildCreatureMetadata(
            fullRoot,
            target.Gender,
            target.Pack,
            ymtPath,
            target.RelativeIndex,
            heelHeight);

        string backupRoot = Path.Combine(fullRoot, ".clothing-locator-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backupRoot);
        string ymtBackup = Path.Combine(backupRoot, Path.GetFileName(ymtPath));
        File.Copy(ymtPath, ymtBackup, false);
        bool creatureExisted = File.Exists(creaturePath);
        string creatureBackup = Path.Combine(backupRoot, Path.GetFileName(creaturePath));
        if (creatureExisted)
        {
            File.Copy(creaturePath, creatureBackup, false);
        }

        string temporaryYmt = ymtPath + ".blrp-metadata";
        string temporaryCreature = creaturePath + ".blrp-metadata";
        try
        {
            File.WriteAllBytes(temporaryYmt, ymtBytes);
            File.WriteAllBytes(temporaryCreature, creatureBytes);
            File.Move(temporaryYmt, ymtPath, true);
            File.Move(temporaryCreature, creaturePath, true);
            return new ClothingMetadataUpdateResult(backupRoot, creaturePath);
        }
        catch
        {
            if (File.Exists(temporaryYmt)) File.Delete(temporaryYmt);
            if (File.Exists(temporaryCreature)) File.Delete(temporaryCreature);
            File.Copy(ymtBackup, ymtPath, true);
            if (creatureExisted)
            {
                File.Copy(creatureBackup, creaturePath, true);
            }
            else if (File.Exists(creaturePath))
            {
                File.Delete(creaturePath);
            }
            throw;
        }
    }

    public static ClothingMetadataUpdateResult RepairHeelMetadata(string rootPath, Gender gender, int pack)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        (string creaturePath, byte[] creatureBytes) = BuildCreatureMetadata(fullRoot, gender, pack);
        string backupRoot = Path.Combine(fullRoot, ".clothing-locator-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backupRoot);
        bool creatureExisted = File.Exists(creaturePath);
        string creatureBackup = Path.Combine(backupRoot, Path.GetFileName(creaturePath));
        if (creatureExisted) File.Copy(creaturePath, creatureBackup, false);

        string temporaryCreature = creaturePath + ".blrp-metadata";
        try
        {
            File.WriteAllBytes(temporaryCreature, creatureBytes);
            File.Move(temporaryCreature, creaturePath, true);
            return new ClothingMetadataUpdateResult(backupRoot, creaturePath);
        }
        catch
        {
            if (File.Exists(temporaryCreature)) File.Delete(temporaryCreature);
            if (creatureExisted) File.Copy(creatureBackup, creaturePath, true);
            else if (File.Exists(creaturePath)) File.Delete(creaturePath);
            throw;
        }
    }

    public static IReadOnlyList<ClothingTextureImportResult> ImportTextures(
        string rootPath,
        ClothingEntry target,
        IReadOnlyList<string> sourceTexturePaths,
        bool optimizeCompression = false)
    {
        foreach (string sourceTexturePath in sourceTexturePaths)
            _ = BuildDuplicateTexture(sourceTexturePath, "blrp_preflight", optimizeCompression, out _);
        var results = new List<ClothingTextureImportResult>(sourceTexturePaths.Count);
        foreach (string sourceTexturePath in sourceTexturePaths)
        {
            try
            {
                results.Add(ImportTexture(rootPath, target, sourceTexturePath, optimizeCompression));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Imported {results.Count} of {sourceTexturePaths.Count} selected textures before {Path.GetFileName(sourceTexturePath)} failed: {exception.Message}",
                    exception);
            }
        }
        return results;
    }

    public static ClothingTextureImportResult ImportTexture(
        string rootPath,
        ClothingEntry target,
        string sourceTexturePath,
        bool optimizeCompression = false)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        string fullTargetModel = Path.GetFullPath(target.FilePath);
        if (!fullTargetModel.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected model is outside the active EUP directory.");
        }
        if (!File.Exists(fullTargetModel))
        {
            throw new FileNotFoundException("The target clothing or prop model was not found.", fullTargetModel);
        }
        if (!File.Exists(sourceTexturePath) || !Path.GetExtension(sourceTexturePath).Equals(".ytd", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Select a valid .ytd texture file.", sourceTexturePath);
        }

        string collection = GetCollectionName(target.Gender, target.Pack);
        string ymtPath = Path.Combine(fullRoot, $"clothing_addon_{target.Pack}", "stream", collection + ".ymt");
        PedFile ped = LoadPedFile(ymtPath);
        MCPedPropMetaData? prop = target.Component.IsProp
            ? ped.VariationInfo?.PropInfo?.PropMetaData?.FirstOrDefault(item =>
                item.Data.anchorId == target.Component.Slot && item.Data.propId == target.RelativeIndex)
            : null;
        MCPVDrawblData? drawable = target.Component.IsProp
            ? null
            : GetDrawables(ped, target.Component.Slot)?.ElementAtOrDefault(target.RelativeIndex);
        if (prop == null && drawable == null)
        {
            throw new InvalidDataException("The target model has no matching YMT entry.");
        }
        int textureCount = target.Component.IsProp ? prop!.TexData?.Length ?? 0 : drawable!.TexData?.Length ?? 0;
        if (textureCount >= 26)
        {
            throw new InvalidOperationException("This drawable already has the maximum 26 textures.");
        }

        bool hasSkin = !target.Component.IsProp && (((drawable!.Data.propMask >> 4) & 3) == 1 ||
            Path.GetFileNameWithoutExtension(fullTargetModel).EndsWith("_r", StringComparison.OrdinalIgnoreCase));
        string suffix = target.Component.IsProp ? string.Empty : GetTextureSuffix(sourceTexturePath, hasSkin);
        char variant = (char)('a' + textureCount);
        string assetName = target.Component.IsProp
            ? $"{target.Component.Code}_diff_{target.RelativeIndex:000}_{variant}"
            : $"{target.Component.Code}_diff_{target.RelativeIndex:000}_{variant}_{suffix}";
        string assetCollection = target.Component.IsProp
            ? collection.Replace("_01_mp_", "_01_p_mp_", StringComparison.Ordinal)
            : collection;
        string fileName = $"{assetCollection}^{assetName}.ytd";
        string targetDirectory = Path.GetDirectoryName(fullTargetModel)!;
        string textureTarget = Path.Combine(targetDirectory, fileName);
        if (File.Exists(textureTarget))
        {
            throw new IOException("Import target already exists: " + textureTarget);
        }

        byte[] textureBytes = BuildDuplicateTexture(sourceTexturePath, assetName, optimizeCompression, out TextureFormat format);
        string collectionDirectory = Path.Combine(Path.GetDirectoryName(ymtPath)!, collection);
        byte[] ymtBytes = target.Component.IsProp
            ? AppendPropTexture(ped, collection, collectionDirectory, target.Component.Slot, target.RelativeIndex)
            : AppendComponentTexture(ped, collection, collectionDirectory, target.Component.Slot, target.RelativeIndex, suffix);

        string backupRoot = Path.Combine(fullRoot, ".clothing-locator-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backupRoot);
        File.Copy(ymtPath, Path.Combine(backupRoot, Path.GetFileName(ymtPath)), false);
        try
        {
            File.WriteAllBytes(textureTarget, textureBytes);
            string temporaryYmt = ymtPath + ".blrp-importing";
            File.WriteAllBytes(temporaryYmt, ymtBytes);
            File.Move(temporaryYmt, ymtPath, true);
            return new ClothingTextureImportResult(textureTarget, textureCount + 1, FormatName(format));
        }
        catch
        {
            if (File.Exists(textureTarget)) File.Delete(textureTarget);
            throw;
        }
    }

    internal static bool SelfTest(string sourceRoot, string fixtureRoot)
    {
        string componentCode = "jbib";
        ComponentDefinition component = ClothingComponents.ByCode[componentCode];
        for (int pack = 1; pack <= RomanPacks.Length; pack++)
        {
            string collection = GetCollectionName(Gender.Female, pack);
            string sourceYmt = Path.Combine(sourceRoot, $"clothing_addon_{pack}", "stream", collection + ".ymt");
            if (!File.Exists(sourceYmt))
            {
                continue;
            }

            string targetYmt = Path.Combine(fixtureRoot, $"clothing_addon_{pack}", "stream", collection + ".ymt");
            Directory.CreateDirectory(Path.GetDirectoryName(targetYmt)!);
            File.Copy(sourceYmt, targetYmt, false);
        }

        string sourceComponentDirectory = Path.Combine(
            sourceRoot,
            "clothing_addon_5",
            "stream",
            GetCollectionName(Gender.Female, 5),
            componentCode);
        string sourceModel = Directory.EnumerateFiles(sourceComponentDirectory, "*.ydd")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
        string sourceTexture = Directory.EnumerateFiles(sourceComponentDirectory, "*.ytd")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();

        ClothingImportPlan plan = CreatePlan(
            fixtureRoot,
            Gender.Female,
            component,
            sourceModel,
            [sourceTexture],
            false);
        int[] componentCountsBefore = Enumerable.Range(0, 12)
            .Select(slot => GetDrawables(plan.PedFile, slot)?.Length ?? 0)
            .ToArray();
        int countBefore = GetDrawables(plan.PedFile, component.Slot)?.Length ?? 0;
        int selectionCountBefore = plan.PedFile.VariationInfo?.SelectionSets?.Length ?? 0;
        int componentInfoCountBefore = plan.PedFile.VariationInfo?.CompInfos?.Length ?? 0;
        int propCountBefore = plan.PedFile.VariationInfo?.PropInfo?.PropMetaData?.Length ?? 0;
        int anchorCountBefore = plan.PedFile.VariationInfo?.PropInfo?.Anchors?.Length ?? 0;
        IReadOnlyList<string> written = Import(plan);
        PedFile updatedPed = LoadPedFile(plan.YmtPath);
        int countAfter = GetDrawables(updatedPed, component.Slot)?.Length ?? 0;
        int[] componentCountsAfter = Enumerable.Range(0, 12)
            .Select(slot => GetDrawables(updatedPed, slot)?.Length ?? 0)
            .ToArray();
        bool existingTextureMetadataValid = Enumerable.Range(0, 12)
            .SelectMany(slot => GetDrawables(updatedPed, slot) ?? [])
            .SelectMany(drawable => drawable.TexData ?? [])
            .All(texture => texture.distribution == 255 && texture.Unused0 == 0);
        MCPVDrawblData? addedDrawable = GetDrawables(updatedPed, component.Slot)?.LastOrDefault();

        string modelTarget = Path.Combine(plan.TargetDirectory, plan.ModelFileName);
        string sourceDrawableName = LoadYdd(sourceModel).DrawableDict?.Drawables?.data_items?[0].Name ?? string.Empty;
        var ydd = new YddFile();
        ydd.Load(File.ReadAllBytes(modelTarget));
        Drawable[] drawables = ydd.DrawableDict?.Drawables?.data_items ?? [];

        string textureTarget = Path.Combine(plan.TargetDirectory, plan.TextureFileNames[0]);
        string sourceTextureName = LoadYtd(sourceTexture).TextureDict?.Textures?.data_items?[0].Name ?? string.Empty;
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(textureTarget));
        Texture[] textures = ytd.TextureDict?.Textures?.data_items ?? [];

        var importedEntry = new ClothingEntry(
            modelTarget,
            new FileInfo(modelTarget).Length,
            plan.Gender,
            plan.Component,
            plan.Pack,
            plan.PackDrawableIndex);
        ClothingTextureImportResult textureImport = ImportTextures(fixtureRoot, importedEntry, [sourceTexture]).Single();
        PedFile textureUpdatedPed = LoadPedFile(plan.YmtPath);
        MCPVDrawblData? textureUpdatedDrawable = GetDrawables(textureUpdatedPed, component.Slot)?.LastOrDefault();
        string importedTextureName = GetAssetName(textureImport.TexturePath);
        Texture[] importedTextures = LoadYtd(textureImport.TexturePath).TextureDict?.Textures?.data_items ?? [];
        bool textureImportValid = textureImport.TexturePath.EndsWith("_b_uni.ytd", StringComparison.OrdinalIgnoreCase) &&
            textureImport.TextureCount == 2 &&
            textureUpdatedDrawable?.TexData is
            [
                { texId: 0, distribution: 255 },
                { texId: 0, distribution: 255 }
            ] &&
            (textureUpdatedPed.VariationInfo?.CompInfos?.Length ?? 0) == componentInfoCountBefore + 1 &&
            importedTextures is [var importedTexture] &&
            importedTexture.Name.Equals(importedTextureName, StringComparison.OrdinalIgnoreCase) &&
            importedTexture.NameHash == JenkHash.GenHash(importedTextureName.ToLowerInvariant());

        string rawSourceDirectory = Path.Combine(fixtureRoot, "raw-source");
        Directory.CreateDirectory(rawSourceDirectory);
        string rawModel = Path.Combine(rawSourceDirectory, Path.GetFileName(sourceModel));
        string rawTexture = Path.Combine(rawSourceDirectory, Path.GetFileName(sourceTexture));
        File.WriteAllBytes(rawModel, DecompressResource(sourceModel, 165));
        File.WriteAllBytes(rawTexture, DecompressResource(sourceTexture, 13));
        ClothingImportPlan rawPlan = CreatePlan(
            fixtureRoot,
            Gender.Female,
            component,
            rawModel,
            [rawTexture],
            false);
        Import(rawPlan);
        int countAfterRawImport = GetDrawables(LoadPedFile(rawPlan.YmtPath), component.Slot)?.Length ?? 0;

        ComponentDefinition raceComponent = ClothingComponents.ByCode["feet"];
        string raceCollection = GetCollectionName(Gender.Female, 1);
        string raceSourceDirectory = Path.Combine(
            sourceRoot,
            "clothing_addon_1",
            "stream",
            raceCollection,
            raceComponent.Code);
        string raceModel = Path.Combine(raceSourceDirectory, $"{raceCollection}^feet_018_r.ydd");
        string raceTextureSource = Path.Combine(raceSourceDirectory, $"{raceCollection}^feet_diff_018_a_whi.ytd");
        string raceFixtureDirectory = Path.Combine(fixtureRoot, "race-source");
        Directory.CreateDirectory(raceFixtureDirectory);
        string whiteRaceTexture = Path.Combine(raceFixtureDirectory, "feet_diff_018_a_whi.ytd");
        string blackRaceTexture = Path.Combine(raceFixtureDirectory, "feet_diff_018_a_bla.ytd");
        File.Copy(raceTextureSource, whiteRaceTexture, false);
        File.Copy(raceTextureSource, blackRaceTexture, false);
        ClothingImportPlan racePlan = CreatePlan(
            fixtureRoot,
            Gender.Female,
            raceComponent,
            raceModel,
            [blackRaceTexture, whiteRaceTexture],
            true);
        string raceShopMetaSource = Path.Combine(
            sourceRoot,
            $"clothing_addon_{racePlan.Pack}",
            racePlan.CollectionName + ".meta");
        string raceShopMetaTarget = Path.Combine(
            fixtureRoot,
            $"clothing_addon_{racePlan.Pack}",
            racePlan.CollectionName + ".meta");
        File.Copy(raceShopMetaSource, raceShopMetaTarget, false);
        string raceCreatureSource = Path.Combine(
            sourceRoot,
            $"clothing_addon_{racePlan.Pack}",
            "stream",
            ReadCreatureReference(raceShopMetaSource) + ".ymt");
        string raceCreatureTarget = Path.Combine(
            fixtureRoot,
            $"clothing_addon_{racePlan.Pack}",
            "stream",
            "legacy-" + Path.GetFileName(raceCreatureSource));
        File.Copy(raceCreatureSource, raceCreatureTarget, false);
        Import(racePlan);
        string repairedRaceCreatureTarget = Path.Combine(
            fixtureRoot,
            $"clothing_addon_{racePlan.Pack}",
            "stream",
            ReadCreatureReference(raceShopMetaTarget) + ".ymt");
        MCPVDrawblData? raceDrawable = GetDrawables(LoadPedFile(racePlan.YmtPath), raceComponent.Slot)?.LastOrDefault();
        bool raceImportValid = racePlan.ModelFileName.EndsWith("_r.ydd", StringComparison.OrdinalIgnoreCase) &&
            racePlan.TextureFileNames.Select(GetAssetName).SequenceEqual(new[]
            {
                $"feet_diff_{racePlan.PackDrawableIndex:000}_a_whi",
                $"feet_diff_{racePlan.PackDrawableIndex:000}_b_bla"
            }) &&
            racePlan.TexturePaths.SequenceEqual(new[] { whiteRaceTexture, blackRaceTexture }, StringComparer.OrdinalIgnoreCase) &&
            raceDrawable?.Data.propMask == 17 &&
            raceDrawable.TexData is
            [
                { texId: 1, distribution: 255 },
                { texId: 2, distribution: 255 }
            ] &&
            File.Exists(Path.Combine(racePlan.TargetDirectory, racePlan.ModelFileName)) &&
            racePlan.TextureFileNames.All(name => File.Exists(Path.Combine(racePlan.TargetDirectory, name))) &&
            File.Exists(repairedRaceCreatureTarget);

        string raceModelTarget = Path.Combine(racePlan.TargetDirectory, racePlan.ModelFileName);
        var raceEntry = new ClothingEntry(
            raceModelTarget,
            new FileInfo(raceModelTarget).Length,
            racePlan.Gender,
            raceComponent,
            racePlan.Pack,
            racePlan.PackDrawableIndex,
            racePlan.TextureFileNames.Count);
        ClothingMetadataUpdateResult heelUpdate = SetHeelHeight(fixtureRoot, raceEntry, 0.9f);
        var creatureMetadata = new RbfFile();
        creatureMetadata.Load(File.ReadAllBytes(heelUpdate.CreatureMetadataPath));
        XDocument creatureXml = XDocument.Parse(RbfXml.GetXml(creatureMetadata));
        bool heelMetadataValid = Math.Abs(GetHeelHeight(fixtureRoot, raceEntry) - 0.9f) < 0.0001f &&
            !heelUpdate.CreatureMetadataPath.Equals(raceCreatureTarget, StringComparison.OrdinalIgnoreCase) &&
            creatureXml.Descendants("Item").Any(item =>
                item.Element("pedCompID")?.Attribute("value")?.Value == "0x6" &&
                item.Element("pedCompVarIndex")?.Attribute("value")?.Value == $"0x{racePlan.PackDrawableIndex:X}" &&
                item.Element("pedCompExpressionIndex")?.Attribute("value")?.Value == "0x4") &&
            File.Exists(Path.Combine(heelUpdate.BackupDirectory, Path.GetFileName(racePlan.YmtPath))) &&
            (GetDrawables(LoadPedFile(racePlan.YmtPath), raceComponent.Slot)?.Length ?? 0) ==
                (GetDrawables(racePlan.PedFile, raceComponent.Slot)?.Length ?? 0) + 1;

        IReadOnlyList<ClothingImportPlan> selectorPlans = CreatePlans(
            sourceRoot,
            Gender.Male,
            ClothingComponents.ByCode["teef"],
            sourceModel,
            [sourceTexture],
            false);
        bool selectorPlansValid = selectorPlans.Count > 0 &&
            selectorPlans.Select(candidate => candidate.Pack).Distinct().Count() == selectorPlans.Count &&
            selectorPlans.Select(candidate => candidate.GlobalIndex).Distinct().Count() == 1;

        string spikedHairModel = Path.Combine(
            sourceRoot,
            "clothing_addon_1",
            "stream",
            GetCollectionName(Gender.Male, 1),
            "berd",
            $"{GetCollectionName(Gender.Male, 1)}^berd_000_u.ydd");
        byte[] convertedHairBytes = BuildDuplicateModel(
            spikedHairModel,
            ClothingComponents.ByCode["hand"],
            "hand_000_u",
            "hand_diff_000_a_uni");
        YddFile sourceHair = LoadYdd(spikedHairModel);
        var convertedHair = new YddFile();
        convertedHair.Load(convertedHairBytes);
        int sourceHairGeometryCount = sourceHair.DrawableDict?.Drawables?.data_items?
            .Sum(drawable => EnumerateModels(drawable).Sum(model => model.Geometries?.Length ?? 0)) ?? 0;
        int suppressedHairGeometryCount = sourceHair.DrawableDict?.Drawables?.data_items?
            .Sum(drawable => CountGeometriesUsingShaders(drawable, FindSuppressedHairShaders(drawable))) ?? 0;
        int convertedHairGeometryCount = convertedHair.DrawableDict?.Drawables?.data_items?
            .Sum(drawable => EnumerateModels(drawable).Sum(model => model.Geometries?.Length ?? 0)) ?? 0;
        ShaderFX[] convertedShaders = convertedHair.DrawableDict?.Drawables?.data_items?
            .SelectMany(drawable => drawable.ShaderGroup?.Shaders?.data_items ?? [])
            .ToArray() ?? [];
        bool hairShaderConversionValid = convertedShaders.Any(shader => shader.Name.Hash == PedHairCutoutAlpha) &&
            convertedShaders.All(shader => shader.Name.Hash != PedHairSpiked) &&
            suppressedHairGeometryCount > 0 &&
            convertedHairGeometryCount == sourceHairGeometryCount - suppressedHairGeometryCount &&
            convertedShaders.Where(shader => shader.Name.Hash == PedHairCutoutAlpha).All(shader =>
                !shader.ParametersList.Hashes.Any(hash => (uint)hash == HairOrderNumber) &&
                shader.ParameterCount == shader.ParametersList.Parameters.Length &&
                shader.ParameterSize == shader.ParametersList.ParametersSize &&
                shader.ParameterDataSize == shader.ParametersList.ParametersDataSize);

        ComponentDefinition duplicateTarget = ClothingComponents.ByCode["hand"];
        ClothingEntry duplicateSource = FindDuplicateSelfTestSource(sourceRoot);
        ClothingImportPlan duplicatePlan = CreateDuplicatePlans(
            fixtureRoot,
            duplicateSource,
            duplicateTarget)[0];
        MCPVDrawblData duplicateTemplate = duplicatePlan.DrawableTemplate!;
        MCComponentInfo? componentInfoTemplate = duplicatePlan.ComponentInfoTemplate;
        int duplicateCountBefore = GetDrawables(duplicatePlan.PedFile, duplicateTarget.Slot)?.Length ?? 0;
        Import(duplicatePlan);
        PedFile duplicatePed = LoadPedFile(duplicatePlan.YmtPath);
        MCPVDrawblData? duplicatedDrawable = GetDrawables(duplicatePed, duplicateTarget.Slot)?.LastOrDefault();
        MCComponentInfo? duplicatedInfo = duplicatePed.VariationInfo?.CompInfos?.FirstOrDefault(info =>
            info.Data.pedXml_compIdx == duplicateTarget.Slot &&
            info.Data.pedXml_drawblIdx == duplicatePlan.PackDrawableIndex);
        string duplicateModelTarget = Path.Combine(duplicatePlan.TargetDirectory, duplicatePlan.ModelFileName);
        string[] duplicateTextureTargets = duplicatePlan.TextureFileNames
            .Select(name => Path.Combine(duplicatePlan.TargetDirectory, name))
            .ToArray();
        string duplicateModelName = GetAssetName(duplicatePlan.ModelFileName);
        string[] duplicateTextureNames = duplicatePlan.TextureFileNames.Select(GetAssetName).ToArray();
        YddFile duplicateYdd = LoadYdd(duplicateModelTarget);
        Drawable? duplicateModel = duplicateYdd.DrawableDict?.Drawables?.data_items?.SingleOrDefault();
        TextureBase[] duplicateDiffuseTextures = duplicateModel == null
            ? []
            : GetDiffuseTextures(duplicateModel).ToArray();
        YtdFile[] duplicateYtds = duplicateTextureTargets.Select(LoadYtd).ToArray();
        var duplicateChecks = new Dictionary<string, bool>
        {
            ["drawable count"] = (GetDrawables(duplicatePed, duplicateTarget.Slot)?.Length ?? 0) == duplicateCountBefore + 1,
            ["drawable reload"] = duplicatedDrawable != null,
            ["prop mask"] = duplicatedDrawable?.Data.propMask == duplicateTemplate.Data.propMask,
            ["alternatives"] = duplicatedDrawable?.Data.numAlternatives == duplicateTemplate.Data.numAlternatives,
            ["reserved flags"] = duplicatedDrawable?.Data.Unused0 == duplicateTemplate.Data.Unused0 &&
                duplicatedDrawable?.Data.Unused1 == duplicateTemplate.Data.Unused1,
            ["cloth flags"] = duplicatedDrawable?.Data.clothData.Equals(duplicateTemplate.Data.clothData) == true,
            ["texture metadata"] = (duplicatedDrawable?.TexData ?? []).SequenceEqual(
                CreateTextureData(duplicatePlan.TexturePaths, duplicatePlan.HasSkin)),
            ["component flags"] = ComponentInfoMatches(componentInfoTemplate, duplicatedInfo),
            ["model naming"] = duplicatePlan.ModelFileName.Contains("^hand_", StringComparison.OrdinalIgnoreCase),
            ["texture naming"] = duplicatePlan.TextureFileNames.All(name => name.Contains("^hand_diff_", StringComparison.OrdinalIgnoreCase)),
            ["internal model name"] = duplicateModel?.Name.Equals(duplicateModelName, StringComparison.OrdinalIgnoreCase) == true,
            ["internal model hash"] = duplicateYdd.DrawableDict?.Hashes is [var modelHash] &&
                modelHash == JenkHash.GenHash(duplicateModelName.ToLowerInvariant()),
            ["diffuse references"] = duplicateDiffuseTextures.Length > 0 && duplicateDiffuseTextures.All(texture =>
                texture.Name.Equals(duplicateTextureNames[0], StringComparison.OrdinalIgnoreCase) &&
                texture.NameHash == JenkHash.GenHash(duplicateTextureNames[0].ToLowerInvariant())),
            ["internal texture names"] = duplicateYtds.Select((ytd, index) =>
                ytd.TextureDict?.Textures?.data_items is [var texture] &&
                texture.Name.Equals(duplicateTextureNames[index], StringComparison.OrdinalIgnoreCase) &&
                texture.NameHash == JenkHash.GenHash(duplicateTextureNames[index].ToLowerInvariant())).All(valid => valid),
            ["texture dictionary hashes"] = duplicateYtds.Select((ytd, index) =>
                ytd.TextureDict?.TextureNameHashes?.data_items is [var textureHash] &&
                textureHash == JenkHash.GenHash(duplicateTextureNames[index].ToLowerInvariant())).All(valid => valid)
        };
        bool duplicateValid = duplicateChecks.Values.All(valid => valid);
        if (!duplicateValid)
        {
            throw new InvalidDataException(
                "Duplicate self-test failed: " +
                string.Join(", ", duplicateChecks.Where(check => !check.Value).Select(check => check.Key)) +
                $". Expected textures: {FormatTextureData(duplicateTemplate.TexData)}; " +
                $"actual: {FormatTextureData(duplicatedDrawable?.TexData)}");
        }

        byte[] modelBeforeReplacement = File.ReadAllBytes(duplicateModelTarget);
        byte[] ymtBeforeReplacement = File.ReadAllBytes(duplicatePlan.YmtPath);
        byte[][] texturesBeforeReplacement = duplicateTextureTargets.Select(File.ReadAllBytes).ToArray();
        var replacementTarget = new ClothingEntry(
            duplicateModelTarget,
            modelBeforeReplacement.Length,
            duplicatePlan.Gender,
            duplicateTarget,
            duplicatePlan.Pack,
            duplicatePlan.PackDrawableIndex,
            duplicateTextureTargets.Length);
        string replacementBackup = ReplaceClothing(
            fixtureRoot,
            replacementTarget,
            raceModel,
            [blackRaceTexture, whiteRaceTexture],
            true);
        string[] replacementTextureTargets = [
            Path.Combine(
                duplicatePlan.TargetDirectory,
                $"{duplicatePlan.CollectionName}^{duplicateTarget.Code}_diff_{duplicatePlan.PackDrawableIndex:000}_a_whi.ytd"),
            Path.Combine(
                duplicatePlan.TargetDirectory,
                $"{duplicatePlan.CollectionName}^{duplicateTarget.Code}_diff_{duplicatePlan.PackDrawableIndex:000}_b_bla.ytd")
        ];
        YddFile replacementYdd = LoadYdd(duplicateModelTarget);
        Drawable? replacementModel = replacementYdd.DrawableDict?.Drawables?.data_items?.SingleOrDefault();
        TextureBase[] replacementDiffuseTextures = replacementModel == null
            ? []
            : GetDiffuseTextures(replacementModel).ToArray();
        PedFile replacementPed = LoadPedFile(duplicatePlan.YmtPath);
        MCPVDrawblData? replacementDrawable = GetDrawables(replacementPed, duplicateTarget.Slot)?
            .ElementAtOrDefault(duplicatePlan.PackDrawableIndex);
        YtdFile[] replacementYtds = replacementTextureTargets.Select(LoadYtd).ToArray();
        string[] remainingReplacementTextures = Directory.EnumerateFiles(duplicatePlan.TargetDirectory, "*.ytd")
            .Where(path => GetAssetName(path).StartsWith(
                $"{duplicateTarget.Code}_diff_{duplicatePlan.PackDrawableIndex:000}_",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool replacementValid = File.ReadAllBytes(Path.Combine(replacementBackup, Path.GetFileName(duplicateModelTarget)))
                .SequenceEqual(modelBeforeReplacement) &&
            File.ReadAllBytes(Path.Combine(replacementBackup, Path.GetFileName(duplicatePlan.YmtPath)))
                .SequenceEqual(ymtBeforeReplacement) &&
            duplicateTextureTargets.Select((path, index) =>
                File.ReadAllBytes(Path.Combine(replacementBackup, Path.GetFileName(path)))
                    .SequenceEqual(texturesBeforeReplacement[index])).All(valid => valid) &&
            replacementModel?.Name.Equals(duplicateModelName, StringComparison.OrdinalIgnoreCase) == true &&
            replacementYdd.DrawableDict?.Hashes is [var replacementHash] &&
            replacementHash == JenkHash.GenHash(duplicateModelName.ToLowerInvariant()) &&
            replacementDiffuseTextures.Length > 0 && replacementDiffuseTextures.All(texture =>
                texture.Name.Equals(GetAssetName(replacementTextureTargets[0]), StringComparison.OrdinalIgnoreCase) &&
                texture.NameHash == JenkHash.GenHash(GetAssetName(replacementTextureTargets[0]).ToLowerInvariant())) &&
            replacementYtds.Select((ytd, index) =>
                ytd.TextureDict?.Textures?.data_items is [var replacementTexture] &&
                replacementTexture.Name.Equals(GetAssetName(replacementTextureTargets[index]), StringComparison.OrdinalIgnoreCase) &&
                replacementTexture.NameHash == JenkHash.GenHash(GetAssetName(replacementTextureTargets[index]).ToLowerInvariant())).All(valid => valid) &&
            remainingReplacementTextures.SequenceEqual(replacementTextureTargets, StringComparer.OrdinalIgnoreCase) &&
            (GetDrawables(replacementPed, duplicateTarget.Slot)?.Length ?? 0) == duplicateCountBefore + 1 &&
            replacementDrawable?.Data.propMask == 17 &&
            replacementDrawable.TexData is
            [
                { texId: 1, distribution: 255 },
                { texId: 2, distribution: 255 }
            ] &&
            (replacementPed.VariationInfo?.CompInfos?.Length ?? 0) ==
                (duplicatePed.VariationInfo?.CompInfos?.Length ?? 0);

        const int propPack = 1;
        ComponentDefinition propComponent = ClothingComponents.ByCode["p_head"];
        string propPedCollection = GetCollectionName(plan.Gender, propPack);
        string propCollection = propPedCollection.Replace("_01_mp_", "_01_p_mp_", StringComparison.Ordinal);
        string propYmtPath = Path.Combine(fixtureRoot, "clothing_addon_1", "stream", propPedCollection + ".ymt");
        string propModel = Path.Combine(
            sourceRoot, "clothing_addon_1", "stream", propCollection, propComponent.Code,
            $"{propCollection}^{propComponent.Code}_000.ydd");
        string fixturePropModel = Path.Combine(fixtureRoot, Path.GetRelativePath(sourceRoot, propModel));
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePropModel)!);
        File.Copy(propModel, fixturePropModel, false);
        MCPedPropMetaData propMetadata = LoadPedFile(propYmtPath).VariationInfo?.PropInfo?.PropMetaData?
            .First(item => item.Data.anchorId == propComponent.Slot && item.Data.propId == 0)
            ?? throw new InvalidDataException("The prop texture self-test target is missing.");
        int propTextureCount = propMetadata.TexData?.Length ?? 0;
        var propEntry = new ClothingEntry(
            fixturePropModel, new FileInfo(fixturePropModel).Length, plan.Gender,
            propComponent, propPack, 0, propTextureCount);
        ClothingTextureImportResult propResult = ImportTexture(fixtureRoot, propEntry, sourceTexture, true);
        var propYtd = new YtdFile();
        propYtd.Load(File.ReadAllBytes(propResult.TexturePath));
        Texture? propTexture = propYtd.TextureDict?.Textures?.data_items?.SingleOrDefault();
        MCPedPropMetaData? rebuiltProp = LoadPedFile(propYmtPath).VariationInfo?.PropInfo?.PropMetaData?
            .FirstOrDefault(item => item.Data.anchorId == propComponent.Slot && item.Data.propId == 0);
        char expectedVariant = (char)('a' + propTextureCount);
        bool propTextureImportValid = propResult.TextureCount == propTextureCount + 1 &&
            Path.GetFileName(propResult.TexturePath).Equals(
                $"{propCollection}^{propComponent.Code}_diff_000_{expectedVariant}.ytd",
                StringComparison.OrdinalIgnoreCase) &&
            propTexture?.Format is TextureFormat.D3DFMT_DXT1 or TextureFormat.D3DFMT_DXT5 &&
            rebuiltProp?.TexData is { } propTextures &&
            propTextures.Length == propTextureCount + 1 &&
            propTextures[^1].texId == propTextureCount &&
            propTextures[^1].distribution == 255;

        string propSourceTexture = Path.Combine(
            Path.GetDirectoryName(propModel)!, $"{propCollection}^{propComponent.Code}_diff_000_a.ytd");
        ClothingImportPlan propPlan = CreatePlan(
            fixtureRoot, plan.Gender, propComponent, propModel, [propSourceTexture], false);
        int propCountBeforeImport = LoadPedFile(propPlan.YmtPath).VariationInfo?.PropInfo?.PropMetaData?.Length ?? 0;
        Import(propPlan);
        PedFile propImportPed = LoadPedFile(propPlan.YmtPath);
        MCPedPropMetaData? importedProp = propImportPed.VariationInfo?.PropInfo?.PropMetaData?
            .FirstOrDefault(item => item.Data.anchorId == propComponent.Slot && item.Data.propId == propPlan.PackDrawableIndex);
        MCAnchorProps? importedAnchor = propImportPed.VariationInfo?.PropInfo?.Anchors?
            .FirstOrDefault(item => (int)item.Data.anchor == propComponent.Slot);
        string propModelTarget = Path.Combine(propPlan.TargetDirectory, propPlan.ModelFileName);
        string propTextureTarget = Path.Combine(propPlan.TargetDirectory, propPlan.TextureFileNames[0]);
        Drawable? importedPropDrawable = LoadYdd(propModelTarget).DrawableDict?.Drawables?.data_items?.SingleOrDefault();
        Texture? importedPropTexture = LoadYtd(propTextureTarget).TextureDict?.Textures?.data_items?.SingleOrDefault();
        string propModelName = GetAssetName(propPlan.ModelFileName);
        string propTextureName = GetAssetName(propPlan.TextureFileNames[0]);
        bool propImportValid =
            (propImportPed.VariationInfo?.PropInfo?.PropMetaData?.Length ?? 0) == propCountBeforeImport + 1 &&
            propImportPed.VariationInfo?.PropInfo?.Data.numAvailProps == propCountBeforeImport + 1 &&
            importedProp?.TexData is [{ texId: 0 }] &&
            importedAnchor?.Props?.LastOrDefault() == 1 &&
            importedPropDrawable?.Name.Equals(propModelName, StringComparison.OrdinalIgnoreCase) == true &&
            importedPropDrawable != null && GetDiffuseTextures(importedPropDrawable)
                .Any(texture => texture.Name.Equals(propTextureName, StringComparison.OrdinalIgnoreCase)) &&
            importedPropTexture?.Name.Equals(propTextureName, StringComparison.OrdinalIgnoreCase) == true;
        if (!propImportValid)
        {
            throw new InvalidDataException(
                $"Prop import verification failed: count {(propImportPed.VariationInfo?.PropInfo?.PropMetaData?.Length ?? 0)}/{propCountBeforeImport + 1}, " +
                $"available {propImportPed.VariationInfo?.PropInfo?.Data.numAvailProps}/{propCountBeforeImport + 1}, " +
                $"metadata {importedProp != null}, anchor tail {importedAnchor?.Props?.LastOrDefault()}, " +
                $"drawable '{importedPropDrawable?.Name}', texture '{importedPropTexture?.Name}', " +
                $"diffuse '{string.Join("|", importedPropDrawable == null ? [] : GetDiffuseTextures(importedPropDrawable).Select(texture => texture.Name))}' expected '{propTextureName}'.");
        }

        return countAfter == countBefore + 1 &&
               countAfterRawImport == countAfter + 1 &&
               selectorPlansValid &&
               raceImportValid &&
               heelMetadataValid &&
               textureImportValid &&
               existingTextureMetadataValid &&
               hairShaderConversionValid &&
               duplicateValid &&
               replacementValid &&
               propTextureImportValid &&
               propImportValid &&
               componentCountsAfter.Where((count, slot) => slot != component.Slot)
                   .SequenceEqual(componentCountsBefore.Where((count, slot) => slot != component.Slot)) &&
               (updatedPed.VariationInfo?.SelectionSets?.Length ?? 0) == selectionCountBefore &&
               (updatedPed.VariationInfo?.CompInfos?.Length ?? 0) == componentInfoCountBefore + 1 &&
               (updatedPed.VariationInfo?.PropInfo?.PropMetaData?.Length ?? 0) == propCountBefore &&
               (updatedPed.VariationInfo?.PropInfo?.Anchors?.Length ?? 0) == anchorCountBefore &&
               addedDrawable?.Data.propMask == 1 &&
               addedDrawable.TexData is [{ texId: 0, distribution: 255 }] &&
               written.Count == 3 &&
               File.ReadAllBytes(modelTarget).SequenceEqual(File.ReadAllBytes(sourceModel)) &&
               File.ReadAllBytes(textureTarget).SequenceEqual(File.ReadAllBytes(sourceTexture)) &&
               drawables.Length == 1 &&
               drawables[0].Name.Equals(sourceDrawableName, StringComparison.OrdinalIgnoreCase) &&
               textures.Length == 1 &&
               textures[0].Name.Equals(sourceTextureName, StringComparison.OrdinalIgnoreCase) &&
               Directory.EnumerateFiles(
                   Path.Combine(fixtureRoot, ".clothing-locator-backups"),
                   "*.ymt",
                   SearchOption.AllDirectories).Any();
    }

    private static ClothingEntry FindDuplicateSelfTestSource(string sourceRoot)
    {
        ComponentDefinition sourceComponent = ClothingComponents.ByCode["berd"];
        for (int pack = 1; pack <= RomanPacks.Length; pack++)
        {
            string collection = GetCollectionName(Gender.Female, pack);
            string ymtPath = Path.Combine(sourceRoot, $"clothing_addon_{pack}", "stream", collection + ".ymt");
            string componentDirectory = Path.Combine(
                sourceRoot,
                $"clothing_addon_{pack}",
                "stream",
                collection,
                sourceComponent.Code);
            if (!File.Exists(ymtPath) || !Directory.Exists(componentDirectory))
            {
                continue;
            }

            MCPVDrawblData[] drawables = GetDrawables(LoadPedFile(ymtPath), sourceComponent.Slot) ?? [];
            for (int index = 0; index < drawables.Length; index++)
            {
                MCPVDrawblData drawable = drawables[index];
                if (drawable.NumAlternatives != 0 || drawable.Data.clothData.ownsCloth != 0)
                {
                    continue;
                }

                string modelPrefix = $"{collection}^{sourceComponent.Code}_{index:000}_";
                string? modelPath = Directory.EnumerateFiles(componentDirectory, "*.ydd")
                    .FirstOrDefault(path => Path.GetFileName(path).StartsWith(modelPrefix, StringComparison.OrdinalIgnoreCase));
                string texturePrefix = $"{collection}^{sourceComponent.Code}_diff_{index:000}_";
                int textureCount = Directory.EnumerateFiles(componentDirectory, "*.ytd")
                    .Count(path => Path.GetFileName(path).StartsWith(texturePrefix, StringComparison.OrdinalIgnoreCase));
                if (modelPath != null && textureCount > 0 && textureCount == (drawable.TexData?.Length ?? 0))
                {
                    return new ClothingEntry(
                        modelPath,
                        new FileInfo(modelPath).Length,
                        Gender.Female,
                        sourceComponent,
                        pack,
                        index);
                }
            }
        }

        throw new InvalidDataException("No safe BERD fixture was found for duplicate testing.");
    }

    private static bool ComponentInfoMatches(MCComponentInfo? expected, MCComponentInfo? actual)
    {
        if (expected == null)
        {
            return actual != null;
        }
        if (actual == null)
        {
            return false;
        }

        CComponentInfo normalized = actual.Data;
        normalized.pedXml_compIdx = expected.Data.pedXml_compIdx;
        normalized.pedXml_drawblIdx = expected.Data.pedXml_drawblIdx;
        return normalized.Equals(expected.Data);
    }

    private static string FormatTextureData(IEnumerable<CPVTextureData>? textures) =>
        string.Join(" ", (textures ?? []).Select(texture =>
            $"[{texture.texId},{texture.distribution},{texture.Unused0}]"));

    internal static bool AssetSelfTest(string modelPath, IReadOnlyList<string> texturePaths)
    {
        bool modelValid;
        if (Path.GetExtension(modelPath).Equals(".ydr", StringComparison.OrdinalIgnoreCase))
        {
            modelValid = LoadYdr(modelPath).Drawable != null;
        }
        else
        {
            modelValid = (LoadYdd(modelPath).DrawableDict?.Drawables?.data_items?.Length ?? 0) == 1;
        }
        return modelValid && texturePaths.All(path =>
            (LoadYtd(path).TextureDict?.Textures?.data_items?.Length ?? 0) == 1);
    }

    internal static string CompatibilityImportTest(
        string sourceRoot,
        string fixtureRoot,
        Gender gender,
        ComponentDefinition component,
        string modelPath,
        IReadOnlyList<string> texturePaths)
    {
        for (int pack = 1; pack <= RomanPacks.Length; pack++)
        {
            string collection = GetCollectionName(gender, pack);
            string sourceYmt = Path.Combine(sourceRoot, $"clothing_addon_{pack}", "stream", collection + ".ymt");
            if (!File.Exists(sourceYmt)) continue;
            string targetYmt = Path.Combine(fixtureRoot, $"clothing_addon_{pack}", "stream", collection + ".ymt");
            Directory.CreateDirectory(Path.GetDirectoryName(targetYmt)!);
            File.Copy(sourceYmt, targetYmt, false);
        }

        ClothingImportPlan plan = CreatePlan(fixtureRoot, gender, component, modelPath, texturePaths, false);
        Import(plan);
        return plan.YmtPath;
    }

    private static byte[] DecompressResource(string path, uint version)
    {
        byte[] data = File.ReadAllBytes(path);
        RpfFile.CreateResourceFileEntry(ref data, version);
        return ResourceBuilder.Decompress(data);
    }

    private static byte[] BuildModel(ClothingImportPlan plan)
    {
        byte[] sourceBytes = File.ReadAllBytes(plan.ModelPath);
        Drawable drawable;
        if (Path.GetExtension(plan.ModelPath).Equals(".ydr", StringComparison.OrdinalIgnoreCase))
        {
            YdrFile ydr = LoadYdr(plan.ModelPath);
            drawable = ydr.Drawable ?? throw new InvalidDataException("The YDR does not contain a drawable.");
        }
        else
        {
            YddFile sourceYdd = LoadYdd(plan.ModelPath);
            Drawable[] drawables = sourceYdd.DrawableDict?.Drawables?.data_items ?? [];
            if (drawables.Length != 1)
            {
                throw new InvalidDataException($"Clothing YDD import requires exactly one drawable; this file contains {drawables.Length}.");
            }
            drawable = drawables[0];
            return HasResourceHeader(sourceBytes) ? sourceBytes : sourceYdd.Save();
        }

        string modelName = string.IsNullOrWhiteSpace(drawable.Name)
            ? $"{plan.Component.Code}_{plan.PackDrawableIndex:000}_{(plan.HasSkin ? "r" : "u")}"
            : drawable.Name;
        var dictionary = new DrawableDictionary
        {
            Hashes = [JenkHash.GenHash(modelName.ToLowerInvariant())],
            Drawables = new ResourcePointerArray64<Drawable> { data_items = [drawable] }
        };
        return new YddFile { DrawableDict = dictionary }.Save();
    }

    private static byte[] BuildDuplicateModel(
        string sourcePath,
        ComponentDefinition targetComponent,
        string targetModelName,
        string targetDiffuseName)
    {
        YddFile ydd;
        DrawableDictionary dictionary;
        if (Path.GetExtension(sourcePath).Equals(".ydr", StringComparison.OrdinalIgnoreCase))
        {
            Drawable drawable = LoadYdr(sourcePath).Drawable ??
                throw new InvalidDataException("The source YDR contains no drawable.");
            dictionary = new DrawableDictionary
            {
                Drawables = new ResourcePointerArray64<Drawable> { data_items = [drawable] }
            };
            ydd = new YddFile { DrawableDict = dictionary };
        }
        else
        {
            ydd = LoadYdd(sourcePath);
            dictionary = ydd.DrawableDict ??
                throw new InvalidDataException("The source YDD has no drawable dictionary.");
        }
        Drawable[] drawables = dictionary.Drawables?.data_items ?? [];
        if (drawables.Length != 1)
        {
            throw new InvalidDataException($"Clothing YDD duplication requires exactly one drawable; this file contains {drawables.Length}.");
        }

        dictionary.Hashes = [JenkHash.GenHash(targetModelName.ToLowerInvariant())];
        bool convertHairShader = targetComponent.Code is not ("berd" or "hair");
        foreach (Drawable drawable in drawables)
        {
            drawable.Name = targetModelName;
            TextureBase[] diffuseTextures = GetDiffuseTextures(drawable).ToArray();
            if (diffuseTextures.Length == 0)
            {
                throw new InvalidDataException("The source drawable has no DiffuseSampler texture reference.");
            }
            foreach (TextureBase texture in diffuseTextures)
            {
                texture.Name = targetDiffuseName;
                texture.NameHash = JenkHash.GenHash(targetDiffuseName.ToLowerInvariant());
            }

            ShaderFX[] shaders = drawable.ShaderGroup?.Shaders?.data_items ?? [];
            HashSet<ushort> suppressedShaderIndexes = convertHairShader
                ? FindSuppressedHairShaders(drawable)
                : [];
            foreach (ShaderFX shader in shaders)
            {
                if (!convertHairShader ||
                    (shader.Name.Hash != PedHairSpiked && shader.FileName.Hash != PedHairSpikedFile))
                {
                    continue;
                }

                ShaderParametersBlock parameters = shader.ParametersList ??
                    throw new InvalidDataException("The ped_hair_spiked shader has no parameter block.");
                int orderIndex = Array.FindIndex(parameters.Hashes, hash => (uint)hash == HairOrderNumber);
                if (orderIndex < 0)
                {
                    throw new InvalidDataException("The ped_hair_spiked shader is missing its OrderNumber parameter.");
                }

                byte removedOffset = parameters.Parameters[orderIndex].Unknown_1h;
                parameters.Parameters = parameters.Parameters.Where((_, index) => index != orderIndex).ToArray();
                parameters.Hashes = parameters.Hashes.Where((_, index) => index != orderIndex).ToArray();
                foreach (ShaderParameter parameter in parameters.Parameters.Where(parameter =>
                    parameter.DataType != 0 && parameter.Unknown_1h > removedOffset))
                {
                    parameter.Unknown_1h--;
                }

                parameters.Count = parameters.Parameters.Length;
                shader.Name = PedHairCutoutAlpha;
                shader.FileName = PedHairCutoutAlphaFile;
                shader.ParameterCount = checked((byte)parameters.Count);
                shader.ParameterSize = parameters.ParametersSize;
                shader.ParameterDataSize = parameters.ParametersDataSize;
                shader.TextureParametersCount = parameters.TextureParamsCount;
            }

            if (suppressedShaderIndexes.Count > 0 && drawable.DrawableModels is not null)
            {
                drawable.DrawableModels.High = RemoveGeometriesUsingShaders(drawable.DrawableModels.High, suppressedShaderIndexes);
                drawable.DrawableModels.Med = RemoveGeometriesUsingShaders(drawable.DrawableModels.Med, suppressedShaderIndexes);
                drawable.DrawableModels.Low = RemoveGeometriesUsingShaders(drawable.DrawableModels.Low, suppressedShaderIndexes);
                drawable.DrawableModels.VLow = RemoveGeometriesUsingShaders(drawable.DrawableModels.VLow, suppressedShaderIndexes);
                drawable.DrawableModels.Extra = RemoveGeometriesUsingShaders(drawable.DrawableModels.Extra, suppressedShaderIndexes);
            }
        }

        return ydd.Save();
    }

    private static IEnumerable<TextureBase> GetDiffuseTextures(Drawable drawable)
    {
        foreach (ShaderFX shader in drawable.ShaderGroup?.Shaders?.data_items ?? [])
        {
            ShaderParametersBlock? parameters = shader.ParametersList;
            if (parameters == null) continue;
            for (int index = 0; index < parameters.Hashes.Length; index++)
            {
                if ((uint)parameters.Hashes[index] == DiffuseSampler &&
                    parameters.Parameters[index].Data is TextureBase texture)
                {
                    yield return texture;
                }
            }
        }
    }

    private static HashSet<ushort> FindSuppressedHairShaders(Drawable drawable)
    {
        var result = new HashSet<ushort>();
        ShaderFX[] shaders = drawable.ShaderGroup?.Shaders?.data_items ?? [];
        for (ushort shaderIndex = 0; shaderIndex < shaders.Length; shaderIndex++)
        {
            ShaderFX shader = shaders[shaderIndex];
            if (shader.Name.Hash != PedHairSpiked && shader.FileName.Hash != PedHairSpikedFile)
            {
                continue;
            }

            ShaderParametersBlock parameters = shader.ParametersList ??
                throw new InvalidDataException("The ped_hair_spiked shader has no parameter block.");
            int orderIndex = Array.FindIndex(parameters.Hashes, hash => (uint)hash == HairOrderNumber);
            if (orderIndex < 0)
            {
                throw new InvalidDataException("The ped_hair_spiked shader is missing its OrderNumber parameter.");
            }
            if (parameters.Parameters[orderIndex].Data is not SharpDX.Vector4 orderNumber)
            {
                throw new InvalidDataException("The ped_hair_spiked OrderNumber parameter has an invalid value.");
            }
            if (orderNumber.X > 0)
            {
                result.Add(shaderIndex);
            }
        }
        return result;
    }

    private static int CountGeometriesUsingShaders(Drawable drawable, HashSet<ushort> shaderIndexes) =>
        EnumerateModels(drawable).Sum(model =>
            (model.ShaderMapping ?? []).Count(shaderIndexes.Contains));

    private static IEnumerable<DrawableModel> EnumerateModels(Drawable drawable)
    {
        if (drawable.DrawableModels is null)
        {
            yield break;
        }

        foreach (DrawableModel[] models in new[]
        {
            drawable.DrawableModels.High,
            drawable.DrawableModels.Med,
            drawable.DrawableModels.Low,
            drawable.DrawableModels.VLow,
            drawable.DrawableModels.Extra
        }.Where(models => models is not null))
        {
            foreach (DrawableModel model in models)
            {
                yield return model;
            }
        }
    }

    private static DrawableModel[]? RemoveGeometriesUsingShaders(
        DrawableModel[]? models,
        HashSet<ushort> shaderIndexes)
    {
        if (models is null)
        {
            return null;
        }

        var keptModels = new List<DrawableModel>(models.Length);
        foreach (DrawableModel model in models)
        {
            DrawableGeometry[] geometries = model.Geometries ?? [];
            ushort[] mappings = model.ShaderMapping ?? [];
            if (geometries.Length != mappings.Length)
            {
                throw new InvalidDataException("A drawable model has mismatched geometry and shader mapping counts.");
            }

            int[] keptIndexes = Enumerable.Range(0, geometries.Length)
                .Where(index => !shaderIndexes.Contains(mappings[index]))
                .ToArray();
            if (keptIndexes.Length == 0)
            {
                continue;
            }

            model.Geometries = keptIndexes.Select(index => geometries[index]).ToArray();
            model.ShaderMapping = keptIndexes.Select(index => mappings[index]).ToArray();
            model.GeometriesCount1 = checked((ushort)keptIndexes.Length);
            model.GeometriesCount2 = model.GeometriesCount1;
            model.GeometriesCount3 = model.GeometriesCount1;
            AABB_s[] bounds = model.BoundsData ?? [];
            bool hasOverallBounds = geometries.Length > 1 && bounds.Length == geometries.Length + 1;
            AABB_s[] keptBounds = keptIndexes
                .Select(index => bounds[hasOverallBounds ? index + 1 : index])
                .ToArray();
            model.BoundsData = keptBounds.Length > 1 && hasOverallBounds
                ? [bounds[0], .. keptBounds]
                : keptBounds;
            keptModels.Add(model);
        }
        return keptModels.Count == 0 ? null : keptModels.ToArray();
    }

    private static byte[] BuildTexture(string sourcePath)
    {
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        YtdFile ytd = LoadYtd(sourcePath);
        Texture[] textures = ytd.TextureDict?.Textures?.data_items ?? [];
        if (textures.Length != 1)
        {
            throw new InvalidDataException($"Each clothing YTD must contain exactly one texture; {Path.GetFileName(sourcePath)} contains {textures.Length}.");
        }

        return HasResourceHeader(sourceBytes) ? sourceBytes : ytd.Save();
    }

    private static byte[] BuildDuplicateTexture(string sourcePath, string targetName)
        => BuildDuplicateTexture(sourcePath, targetName, false, out _);

    private static byte[] BuildDuplicateTexture(
        string sourcePath,
        string targetName,
        bool optimizeCompression,
        out TextureFormat format)
    {
        YtdFile ytd = LoadYtd(sourcePath);
        TextureDictionary dictionary = ytd.TextureDict ??
            throw new InvalidDataException($"Texture dictionary missing from {Path.GetFileName(sourcePath)}.");
        Texture[] textures = dictionary.Textures?.data_items ?? [];
        if (textures.Length != 1)
        {
            throw new InvalidDataException($"Each clothing YTD must contain exactly one texture; {Path.GetFileName(sourcePath)} contains {textures.Length}.");
        }

        Texture texture = textures[0];
        if (optimizeCompression)
        {
            try { texture = OptimizeTexture(texture); }
            catch (NotSupportedException) { /* Preserve uncommon formats instead of failing the import. */ }
        }
        texture.Name = targetName;
        texture.NameHash = JenkHash.GenHash(targetName.ToLowerInvariant());
        textures[0] = texture;
        format = texture.Format;
        dictionary.BuildFromTextureList(textures.ToList());
        return ytd.Save();
    }

    private static Texture OptimizeTexture(Texture texture)
    {
        using var sourceDds = new MemoryStream(DDSIO.GetDDSFile(texture));
        ColorRgba32[] pixels = new BcDecoder().Decode(DdsFile.Load(sourceDds));
        bool usesAlpha = pixels.Any(pixel => pixel.a < 255);
        var rgba = new byte[pixels.Length * 4];
        for (int index = 0; index < pixels.Length; index++)
        {
            rgba[index * 4] = pixels[index].r;
            rgba[index * 4 + 1] = pixels[index].g;
            rgba[index * 4 + 2] = pixels[index].b;
            rgba[index * 4 + 3] = pixels[index].a;
        }

        CompressionFormat compression = usesAlpha ? CompressionFormat.Bc3 : CompressionFormat.Bc1;
        var encoder = new BcEncoder(compression)
        {
            OutputOptions =
            {
                Format = compression,
                GenerateMipMaps = true,
                Quality = CompressionQuality.Balanced,
                DdsPreferDxt10Header = false
            }
        };
        DdsFile dds = encoder.EncodeToDds(rgba, texture.Width, texture.Height, PixelFormat.Rgba32);
        using var output = new MemoryStream();
        dds.Write(output);
        Texture converted = DDSIO.GetTexture(output.ToArray()) ??
            throw new InvalidDataException($"Could not convert texture {texture.Name} to {(usesAlpha ? "DXT5" : "DXT1")}.");
        texture.Format = converted.Format;
        texture.Stride = converted.Stride;
        texture.Levels = converted.Levels;
        texture.Data = converted.Data;
        return texture;
    }

    private static string FormatName(TextureFormat format) => format switch
    {
        TextureFormat.D3DFMT_DXT1 => "DXT1 / OPAQUE",
        TextureFormat.D3DFMT_DXT5 => "DXT5 / ALPHA",
        _ => format.ToString().Replace("D3DFMT_", string.Empty, StringComparison.Ordinal)
    };

    private static byte[] AppendComponentDrawable(
        PedFile ped,
        string collectionName,
        string collectionDirectory,
        int componentId,
        bool hasSkin,
        IReadOnlyList<string> texturePaths,
        MCPVDrawblData? drawableTemplate = null,
        MCComponentInfo? componentInfoTemplate = null) => RebuildComponent(
            ped,
            collectionName,
            collectionDirectory,
            componentId,
            hasSkin,
            texturePaths,
            drawableTemplate,
            componentInfoTemplate,
            null,
            null,
            null,
            null,
            null);

    private static byte[] ReplaceComponentDrawable(
        PedFile ped,
        string collectionName,
        string collectionDirectory,
        int componentId,
        int drawableIndex,
        bool hasSkin,
        IReadOnlyList<string> texturePaths) => RebuildComponent(
            ped,
            collectionName,
            collectionDirectory,
            componentId,
            hasSkin,
            texturePaths,
            null,
            null,
            null,
            null,
            drawableIndex,
            null,
            null);

    private static byte[] AppendComponentTexture(
        PedFile ped,
        string collectionName,
        string collectionDirectory,
        int componentId,
        int drawableIndex,
        string textureSuffix) => RebuildComponent(
            ped,
            collectionName,
            collectionDirectory,
            componentId,
            false,
            [],
            null,
            null,
            drawableIndex,
            textureSuffix,
            null,
            null,
            null);

    private static byte[] AppendPropTexture(
        PedFile ped,
        string collectionName,
        string collectionDirectory,
        int anchorId,
        int propId) => RebuildComponent(
            ped,
            collectionName,
            collectionDirectory,
            0,
            false,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            anchorId,
            propId);

    private static byte[] AppendPropDrawable(
        PedFile ped,
        string collectionName,
        string collectionDirectory,
        int anchorId,
        int propId,
        int textureCount,
        CPedPropMetaData template) => RebuildComponent(
            ped,
            collectionName,
            collectionDirectory,
            0,
            false,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            anchorId,
            propId,
            textureCount,
            template);

    private static byte[] UpdateComponentHeelHeight(
        PedFile ped,
        string collectionName,
        string collectionDirectory,
        int componentId,
        int drawableIndex,
        float heelHeight) => RebuildComponent(
            ped,
            collectionName,
            collectionDirectory,
            componentId,
            false,
            [],
            null,
            null,
            null,
            null,
            null,
            drawableIndex,
            heelHeight);

    private static byte[] RebuildComponent(
        PedFile ped,
        string collectionName,
        string collectionDirectory,
        int componentId,
        bool hasSkin,
        IReadOnlyList<string> texturePaths,
        MCPVDrawblData? drawableTemplate,
        MCComponentInfo? componentInfoTemplate,
        int? textureTargetIndex,
        string? textureSuffix,
        int? replacementTargetIndex,
        int? componentInfoTargetIndex,
        float? heelHeight,
        int? propAnchorId = null,
        int? propTargetIndex = null,
        int? propAppendAnchorId = null,
        int? propAppendId = null,
        int propTextureCount = 0,
        CPedPropMetaData? propTemplate = null)
    {
        MCPedVariationInfo source = ped.VariationInfo ?? throw new InvalidDataException("The target YMT has no ped variation data.");
        MCPVComponentData[] sourceComponents = source.ComponentData3 ?? [];
        byte[] componentIndices = source.ComponentIndices?.ToArray() ?? Enumerable.Repeat((byte)255, 12).ToArray();
        bool updatePropTexture = propAnchorId != null && propTargetIndex != null;
        bool appendProp = propAppendAnchorId != null && propAppendId != null;
        bool changeProp = updatePropTexture || appendProp;
        int targetComponentIndex = changeProp ? -1 : componentIndices[componentId];
        bool createComponent = !changeProp && targetComponentIndex == 255;
        if (createComponent)
        {
            targetComponentIndex = sourceComponents.Length;
            componentIndices[componentId] = checked((byte)targetComponentIndex);
        }

        var mb = new MetaBuilder();
        mb.EnsureBlock(MetaName.CPedVariationInfo);
        var rebuiltComponents = new List<CPVComponentData>();
        int componentCount = sourceComponents.Length + (createComponent ? 1 : 0);
        for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            MCPVComponentData? existingComponent = componentIndex < sourceComponents.Length ? sourceComponents[componentIndex] : null;
            var drawables = new List<CPVDrawblData>();
            if (existingComponent?.DrawblData3 != null)
            {
                for (int drawableIndex = 0; drawableIndex < existingComponent.DrawblData3.Length; drawableIndex++)
                {
                    MCPVDrawblData existingDrawable = existingComponent.DrawblData3[drawableIndex];
                    if (componentIndex == targetComponentIndex && replacementTargetIndex == drawableIndex)
                    {
                        var replacement = new CPVDrawblData { propMask = (byte)(hasSkin ? 17 : 1) };
                        replacement.aTexData = AddTextureDataArray(mb, CreateTextureData(texturePaths, hasSkin));
                        drawables.Add(replacement);
                        continue;
                    }
                    CPVDrawblData data = existingDrawable.Data;
                    int slot = Array.IndexOf(componentIndices, checked((byte)componentIndex));
                    IEnumerable<CPVTextureData> textures = CreateExistingTextureData(
                        collectionDirectory,
                        slot,
                        drawableIndex,
                        existingDrawable);
                    if (componentIndex == targetComponentIndex && textureTargetIndex == drawableIndex)
                    {
                        textures = textures.Append(new CPVTextureData
                        {
                            texId = GetTextureId(textureSuffix!),
                            distribution = 255
                        });
                    }
                    data.aTexData = AddTextureDataArray(mb, textures);
                    drawables.Add(data);
                }
            }

            if (!changeProp && componentIndex == targetComponentIndex && textureTargetIndex == null &&
                replacementTargetIndex == null && componentInfoTargetIndex == null)
            {
                CPVTextureData[] textureData = CreateTextureData(texturePaths, hasSkin);
                CPVDrawblData drawable = drawableTemplate?.Data ?? new CPVDrawblData
                {
                    propMask = (byte)(hasSkin ? 17 : 1)
                };
                drawable.aTexData = AddTextureDataArray(mb, textureData);
                drawables.Add(drawable);
            }

            CPVComponentData componentData = existingComponent?.Data ?? new CPVComponentData();
            componentData.numAvailTex = unchecked((byte)drawables.Sum(drawable => GetArrayLength(drawable.aTexData)));
            componentData.aDrawblData3 = mb.AddItemArrayPtr(MetaName.CPVDrawblData, drawables.ToArray());
            rebuiltComponents.Add(componentData);
        }

        var root = source.Data;
        var availableComponents = new ArrayOfBytes12
        {
            b00 = componentIndices[0],
            b01 = componentIndices[1],
            b02 = componentIndices[2],
            b03 = componentIndices[3],
            b04 = componentIndices[4],
            b05 = componentIndices[5],
            b06 = componentIndices[6],
            b07 = componentIndices[7],
            b08 = componentIndices[8],
            b09 = componentIndices[9],
            b10 = componentIndices[10],
            b11 = componentIndices[11]
        };
        root.availComp = availableComponents;
        root.aComponentData3 = mb.AddItemArrayPtr(MetaName.CPVComponentData, rebuiltComponents.ToArray());
        root.aSelectionSets = mb.AddItemArrayPtr(MetaName.CPedSelectionSet, source.SelectionSets?.Select(item => item.Data).ToArray() ?? []);

        var componentInfos = source.CompInfos?.Select(item => item.Data).ToList() ?? [];
        if (componentInfoTargetIndex != null)
        {
            int infoIndex = componentInfos.FindIndex(info =>
                info.pedXml_compIdx == componentId && info.pedXml_drawblIdx == componentInfoTargetIndex);
            CComponentInfo info = infoIndex >= 0 ? componentInfos[infoIndex] : new CComponentInfo
            {
                pedXml_audioID = JenkHash.GenHash("none"),
                pedXml_audioID2 = JenkHash.GenHash("none"),
                pedXml_compIdx = checked((byte)componentId),
                pedXml_drawblIdx = checked((byte)componentInfoTargetIndex.Value)
            };
            ArrayOfFloats5 expressionMods = info.pedXml_expressionMods;
            expressionMods.f4 = heelHeight ?? 0;
            info.pedXml_expressionMods = expressionMods;
            if (infoIndex >= 0) componentInfos[infoIndex] = info;
            else componentInfos.Add(info);
        }
        else if (replacementTargetIndex != null)
        {
            componentInfos.RemoveAll(info =>
                info.pedXml_compIdx == componentId && info.pedXml_drawblIdx == replacementTargetIndex);
        }
        if (!changeProp && textureTargetIndex == null && componentInfoTargetIndex == null)
        {
            CComponentInfo componentInfo = componentInfoTemplate?.Data ?? new CComponentInfo
            {
                pedXml_audioID = JenkHash.GenHash("none"),
                pedXml_audioID2 = JenkHash.GenHash("none")
            };
            componentInfo.pedXml_compIdx = (byte)componentId;
            componentInfo.pedXml_drawblIdx = checked((byte)(replacementTargetIndex ?? (GetDrawables(ped, componentId)?.Length ?? 0)));
            componentInfos.Add(componentInfo);
        }
        root.compInfos = mb.AddItemArrayPtr(MetaName.CComponentInfo, componentInfos.ToArray());

        CPedPropInfo propInfo = source.PropInfo?.Data ?? new CPedPropInfo();
        var props = new List<CPedPropMetaData>();
        foreach (MCPedPropMetaData wrapper in source.PropInfo?.PropMetaData ?? [])
        {
            CPedPropMetaData data = wrapper.Data;
            IEnumerable<CPedPropTexData> textures = wrapper.TexData ?? [];
            if (updatePropTexture && data.anchorId == propAnchorId && data.propId == propTargetIndex)
            {
                int textureCount = wrapper.TexData?.Length ?? 0;
                textures = textures.Append(new CPedPropTexData
                {
                    texId = checked((byte)textureCount),
                    distribution = 255
                });
            }
            data.texData = mb.AddItemArrayPtr(MetaName.CPedPropTexData, textures.ToArray());
            props.Add(data);
        }
        if (appendProp)
        {
            CPedPropMetaData data = propTemplate ?? new CPedPropMetaData();
            data.anchorId = checked((byte)propAppendAnchorId!.Value);
            data.propId = checked((byte)propAppendId!.Value);
            data.texData = mb.AddItemArrayPtr(MetaName.CPedPropTexData, Enumerable.Range(0, propTextureCount)
                .Select(index => new CPedPropTexData { texId = checked((byte)index), distribution = 255 })
                .ToArray());
            props.Add(data);
        }
        propInfo.numAvailProps = checked((byte)props.Count);
        propInfo.aPropMetaData = mb.AddItemArrayPtr(MetaName.CPedPropMetaData, props.ToArray());

        var anchors = new List<CAnchorProps>();
        foreach (MCAnchorProps wrapper in source.PropInfo?.Anchors ?? [])
        {
            CAnchorProps data = wrapper.Data;
            byte[] values = wrapper.Props ?? [];
            if (appendProp && (int)data.anchor == propAppendAnchorId)
            {
                values = [.. values, checked((byte)propTextureCount)];
            }
            data.props = mb.AddByteArrayPtr(values);
            anchors.Add(data);
        }
        if (appendProp && !anchors.Any(item => (int)item.anchor == propAppendAnchorId))
        {
            anchors.Add(new CAnchorProps
            {
                anchor = (eAnchorPoints)propAppendAnchorId!.Value,
                props = mb.AddByteArrayPtr([checked((byte)propTextureCount)])
            });
        }
        propInfo.aAnchors = mb.AddItemArrayPtr(MetaName.CAnchorProps, anchors.ToArray());
        root.propInfo = propInfo;

        mb.AddItem(MetaName.CPedVariationInfo, root);
        mb.AddStructureInfo(MetaName.CPedVariationInfo);
        mb.AddStructureInfo(MetaName.CPedPropInfo);
        mb.AddStructureInfo(MetaName.CPedPropTexData);
        mb.AddStructureInfo(MetaName.CAnchorProps);
        mb.AddStructureInfo(MetaName.CComponentInfo);
        mb.AddStructureInfo(MetaName.CPVComponentData);
        mb.AddStructureInfo(MetaName.CPVDrawblData);
        mb.AddStructureInfo(MetaName.CPVDrawblData__CPVClothComponentData);
        mb.AddStructureInfo(MetaName.CPVTextureData);
        mb.AddStructureInfo(MetaName.CPedPropMetaData);
        mb.AddEnumInfo(MetaName.ePedVarComp);
        mb.AddEnumInfo(MetaName.eAnchorPoints);
        mb.AddEnumInfo(MetaName.ePropRenderFlags);
        Meta meta = mb.GetMeta();
        meta.Name = string.IsNullOrWhiteSpace(ped.Meta?.Name) ? collectionName : ped.Meta.Name;
        byte[] bytes = ResourceBuilder.Build(meta, 2);
        string verificationXml = MetaXml.GetXml(RpfFile.GetResourceFile<PedFile>(bytes)?.Meta);
        if (!verificationXml.Contains("<CPedVariationInfo", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Generated YMT has an invalid metadata root.");
        }
        return bytes;
    }

    private static Array_Structure AddTextureDataArray(MetaBuilder builder, IEnumerable<CPVTextureData>? textures)
    {
        CPVTextureData[] items = (textures ?? []).ToArray();
        if (items.Length == 0)
        {
            return new Array_Structure();
        }

        int dataLength = items.Length * 3;
        int paddedLength = ((dataLength + 47) / 48) * 48;
        var data = new byte[paddedLength];
        for (int index = 0; index < items.Length; index++)
        {
            data[index * 3] = items[index].texId;
            data[index * 3 + 1] = items[index].distribution;
            data[index * 3 + 2] = items[index].Unused0;
        }

        return new Array_Structure(builder.AddItemArray(MetaName.CPVTextureData, data, items.Length));
    }

    private static CPVTextureData[] CreateExistingTextureData(
        string collectionDirectory,
        int componentId,
        int drawableIndex,
        MCPVDrawblData fallback)
    {
        string componentCode = componentId switch
        {
            0 => "head", 1 => "berd", 2 => "hair", 3 => "uppr", 4 => "lowr", 5 => "hand",
            6 => "feet", 7 => "teef", 8 => "accs", 9 => "task", 10 => "decl", 11 => "jbib",
            _ => throw new InvalidDataException($"Unknown component slot {componentId}.")
        };
        string directory = Path.Combine(collectionDirectory, componentCode);
        string[] paths = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"*^{componentCode}_diff_{drawableIndex:000}_*.ytd").ToArray()
            : [];
        if (paths.Length > 0)
        {
            bool hasSkin = ((fallback.Data.propMask >> 4) & 3) == 1;
            return CreateTextureLayout(paths, hasSkin)
                .Select(item => new CPVTextureData { texId = GetTextureId(item.Suffix), distribution = 255 })
                .ToArray();
        }

        byte textureId = ((fallback.Data.propMask >> 4) & 3) == 1 ? (byte)1 : (byte)0;
        return Enumerable.Range(0, fallback.TexData?.Length ?? 0)
            .Select(_ => new CPVTextureData { texId = textureId, distribution = 255 })
            .ToArray();
    }

    private static PedFile LoadPedFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return RpfFile.GetResourceFile<PedFile>(bytes) ?? throw new InvalidDataException("Unable to read YMT: " + path);
    }

    private static void ValidateAppendPreservesExisting(PedFile before, byte[] candidateBytes, ClothingImportPlan plan)
    {
        PedFile after = RpfFile.GetResourceFile<PedFile>(candidateBytes)
            ?? throw new InvalidDataException("The generated YMT could not be read back. No files were changed.");
        for (int slot = 0; slot < 12; slot++)
        {
            MCPVDrawblData[] oldDrawables = GetDrawables(before, slot) ?? [];
            MCPVDrawblData[] newDrawables = GetDrawables(after, slot) ?? [];
            int expected = oldDrawables.Length + (!plan.Component.IsProp && slot == plan.Component.Slot ? 1 : 0);
            if (newDrawables.Length != expected)
                throw new InvalidDataException($"YMT validation failed for component slot {slot}: expected {expected} drawables, found {newDrawables.Length}. No files were changed.");
            for (int index = 0; index < oldDrawables.Length; index++)
                if (DrawableFingerprint(oldDrawables[index]) != DrawableFingerprint(newDrawables[index]))
                    throw new InvalidDataException($"YMT validation detected a change to existing component slot {slot}, drawable {index:000}. No files were changed.");
        }

        MCPedPropMetaData[] oldProps = before.VariationInfo?.PropInfo?.PropMetaData ?? [];
        MCPedPropMetaData[] newProps = after.VariationInfo?.PropInfo?.PropMetaData ?? [];
        int expectedProps = oldProps.Length + (plan.Component.IsProp ? 1 : 0);
        if (newProps.Length != expectedProps)
            throw new InvalidDataException($"YMT validation failed for props: expected {expectedProps}, found {newProps.Length}. No files were changed.");
        foreach (MCPedPropMetaData oldProp in oldProps)
        {
            MCPedPropMetaData? newProp = newProps.FirstOrDefault(item =>
                item.Data.anchorId == oldProp.Data.anchorId && item.Data.propId == oldProp.Data.propId);
            if (newProp == null || PropFingerprint(oldProp) != PropFingerprint(newProp))
                throw new InvalidDataException($"YMT validation detected a change to existing prop anchor {oldProp.Data.anchorId}, drawable {oldProp.Data.propId:000}. No files were changed.");
        }

        foreach (MCAnchorProps oldAnchor in before.VariationInfo?.PropInfo?.Anchors ?? [])
        {
            MCAnchorProps? newAnchor = (after.VariationInfo?.PropInfo?.Anchors ?? [])
                .FirstOrDefault(item => item.Data.anchor == oldAnchor.Data.anchor);
            if (newAnchor == null || !(oldAnchor.Props ?? []).SequenceEqual((newAnchor.Props ?? []).Take(oldAnchor.Props?.Length ?? 0)))
                throw new InvalidDataException($"YMT validation detected a change to existing prop anchor {(int)oldAnchor.Data.anchor}. No files were changed.");
        }
    }

    private static string DrawableFingerprint(MCPVDrawblData item)
    {
        CPVDrawblData data = item.Data;
        CPVDrawblData__CPVClothComponentData cloth = data.clothData;
        string textures = string.Join(',', (item.TexData ?? []).Select(texture =>
            $"{texture.texId}:{texture.distribution}:{texture.Unused0}"));
        return $"{data.propMask}:{data.numAlternatives}:{data.Unused0}:{data.Unused1}:" +
            $"{cloth.ownsCloth}:{cloth.Unused0}:{cloth.Unused1}:{cloth.Unused2}:{cloth.Unused3}:{cloth.Unused4}:{cloth.Unused5}:{cloth.Unused6}:{textures}";
    }

    private static string PropFingerprint(MCPedPropMetaData item)
    {
        CPedPropMetaData data = item.Data;
        string textures = string.Join(',', (item.TexData ?? []).Select(texture =>
            $"{texture.inclusions}:{texture.exclusions}:{texture.texId}:{texture.inclusionId}:{texture.exclusionId}:{texture.distribution}"));
        return $"{data.audioId.Hash}:{data.expressionMods}:{(int)data.renderFlags}:{data.propFlags}:{data.flags}:" +
            $"{data.anchorId}:{data.propId}:{data.Unused5}:{data.Unused6}:{textures}";
    }

    internal static ClothingModelQuality InspectModel(string path, int textureCount)
    {
        try
        {
            Drawable[] drawables = LoadYdd(path).DrawableDict?.Drawables?.data_items ?? [];
            if (drawables.Length == 0)
            {
                return new ClothingModelQuality("INVALID MODEL", "The YDD contains no drawables.");
            }

            long Count(Func<DrawableModelsBlock, DrawableModel[]?> select) => drawables.Sum(drawable =>
                (select(drawable.DrawableModels ?? new DrawableModelsBlock()) ?? []).Sum(model =>
                    (model.Geometries ?? []).Sum(geometry => (long)geometry.IndicesCount / 3)));
            long highPolygons = Count(models => models.High);
            long mediumPolygons = Count(models => models.Med);
            long lowPolygons = Count(models => models.Low);
            bool high = drawables.All(drawable => drawable.DrawableModels?.High?.Length > 0);
            bool medium = drawables.All(drawable => drawable.DrawableModels?.Med?.Length > 0);
            bool low = drawables.All(drawable => drawable.DrawableModels?.Low?.Length > 0);
            return SummarizeQuality(highPolygons, mediumPolygons, lowPolygons, high, medium, low, textureCount);
        }
        catch (Exception exception)
        {
            return new ClothingModelQuality("READ ERROR", exception.Message);
        }
    }

    private static ClothingModelQuality SummarizeQuality(
        long highPolygons,
        long mediumPolygons,
        long lowPolygons,
        bool high,
        bool medium,
        bool low,
        int textureCount)
    {
        var warnings = new List<string>();
        if (highPolygons > 20_000) warnings.Add("OVER 20K");
        if (!high) warnings.Add("NO HIGH");
        if (!medium) warnings.Add("NO MED");
        if (!low) warnings.Add("NO LOW");
        if (textureCount == 0) warnings.Add("NO YTD");
        return new ClothingModelQuality(
            warnings.Count == 0 ? "OK" : string.Join(" / ", warnings),
            $"HIGH POLYGONS: {highPolygons:N0} (LIMIT 20,000)\n" +
            $"MEDIUM POLYGONS: {(medium ? mediumPolygons.ToString("N0") : "MISSING")}\n" +
            $"LOW POLYGONS: {(low ? lowPolygons.ToString("N0") : "MISSING")}\n" +
            $"MATCHING YTDS: {textureCount}",
            highPolygons,
            medium ? mediumPolygons : null,
            low ? lowPolygons : null);
    }

    internal static bool QualitySelfTest(string? rootPath = null)
    {
        ClothingModelQuality good = SummarizeQuality(19_999, 9_000, 3_000, true, true, true, 1);
        ClothingModelQuality bad = SummarizeQuality(20_001, 0, 0, true, false, false, 0);
        bool summariesValid = good.Summary == "OK" &&
            bad.Summary == "OVER 20K / NO MED / NO LOW / NO YTD" &&
            bad.HighPolygons == 20_001 && good.MediumPolygons == 9_000 && good.LowPolygons == 3_000;
        string testRoot = Path.Combine(Path.GetTempPath(), "blrp-clothing-root-test");
        string testAddon = Path.Combine(testRoot, "clothing_addon_1");
        summariesValid &= ResolveAddonRoot(testRoot, 1).Equals(Path.GetFullPath(testAddon), StringComparison.OrdinalIgnoreCase) &&
            ResolveAddonRoot(testAddon, 1).Equals(Path.GetFullPath(testAddon), StringComparison.OrdinalIgnoreCase);
        if (!summariesValid || string.IsNullOrWhiteSpace(rootPath)) return summariesValid;
        string? model = Directory.EnumerateFiles(rootPath, "*.ydd", SearchOption.AllDirectories).FirstOrDefault();
        return model is not null && InspectModel(model, 1).Summary is not ("READ ERROR" or "INVALID MODEL");
    }

    private static YddFile LoadYdd(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        try
        {
            var file = new YddFile();
            if (HasResourceHeader(data)) file.Load(data);
            else file.Load(data, CreateRawResourceEntry(path, data, 165));
            return file;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"Unable to read model {Path.GetFileName(path)}: {exception.Message}", exception);
        }
    }

    private static YdrFile LoadYdr(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        try
        {
            var file = new YdrFile();
            if (HasResourceHeader(data)) file.Load(data);
            else file.Load(data, CreateRawResourceEntry(path, data, 165));
            return file;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"Unable to read model {Path.GetFileName(path)}: {exception.Message}", exception);
        }
    }

    private static YtdFile LoadYtd(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        try
        {
            var file = new YtdFile();
            if (HasResourceHeader(data)) file.Load(data);
            else file.Load(data, CreateRawResourceEntry(path, data, 13));
            return file;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"Unable to read texture {Path.GetFileName(path)}: {exception.Message}", exception);
        }
    }

    private static bool HasResourceHeader(byte[] data) =>
        data.Length >= 16 && BitConverter.ToUInt32(data, 0) == 0x37435352;

    private static RpfResourceFileEntry CreateRawResourceEntry(string path, byte[] data, uint version)
    {
        const int pageSize = 0x2000;
        int systemSize = Math.Min(pageSize, data.Length);
        while (systemSize < data.Length)
        {
            int maximumSystemOffset = 0;
            for (int offset = 0; offset <= systemSize - sizeof(ulong); offset += sizeof(ulong))
            {
                ulong pointer = BitConverter.ToUInt64(data, offset);
                ulong resourceOffset = pointer - 0x50000000;
                if (pointer >= 0x50000000 && pointer < 0x60000000 && resourceOffset < (ulong)data.Length)
                {
                    maximumSystemOffset = Math.Max(maximumSystemOffset, checked((int)resourceOffset));
                }
            }

            int requiredSize = Math.Min(
                data.Length,
                Math.Max(pageSize, ((maximumSystemOffset + pageSize) / pageSize) * pageSize));
            if (requiredSize <= systemSize) break;
            systemSize = requiredSize;
        }

        return new RpfResourceFileEntry
        {
            Name = Path.GetFileName(path),
            NameLower = Path.GetFileName(path).ToLowerInvariant(),
            SystemFlags = RpfResourceFileEntry.GetFlagsFromSize(systemSize, version >> 4),
            GraphicsFlags = RpfResourceFileEntry.GetFlagsFromSize(data.Length - systemSize, version & 0xF)
        };
    }

    private static MCPVDrawblData[]? GetDrawables(PedFile ped, int componentId)
    {
        MCPedVariationInfo? variation = ped.VariationInfo;
        if (variation?.ComponentIndices == null || componentId < 0 || componentId >= variation.ComponentIndices.Length)
        {
            return null;
        }
        int index = variation.ComponentIndices[componentId];
        return index == 255 || variation.ComponentData3 == null || index >= variation.ComponentData3.Length
            ? null
            : variation.ComponentData3[index].DrawblData3;
    }

    private static string GetCollectionName(Gender gender, int pack)
    {
        string letter = gender == Gender.Male ? "m" : "f";
        return $"mp_{letter}_freemode_01_mp_{letter}_c_addons_{RomanPacks[pack - 1]}";
    }

    private static string GetYmtPath(string rootPath, ClothingEntry target)
    {
        string path = Path.Combine(
            ResolveAddonRoot(rootPath, target.Pack),
            "stream",
            GetCollectionName(target.Gender, target.Pack) + ".ymt");
        return File.Exists(path) ? path : throw new FileNotFoundException("The YMT for the selected model was not found.", path);
    }

    private static string ResolveAddonRoot(string rootPath, int pack)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        string addonName = $"clothing_addon_{pack}";
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(fullRoot)).Equals(addonName, StringComparison.OrdinalIgnoreCase)
            ? fullRoot
            : Path.Combine(fullRoot, addonName);
    }

    private static (string Path, byte[] Bytes) BuildCreatureMetadata(
        string rootPath,
        Gender gender,
        int pack,
        string? updatedYmtPath = null,
        int updatedRelativeIndex = -1,
        float updatedHeelHeight = 0)
    {
        string addonRoot = Path.Combine(rootPath, $"clothing_addon_{pack}");
        string streamRoot = Path.Combine(addonRoot, "stream");
        string collection = GetCollectionName(gender, pack);
        string shopMetaPath = Path.Combine(addonRoot, collection + ".meta");
        if (!File.Exists(shopMetaPath))
        {
            throw new FileNotFoundException("The SHOP_PED_APPAREL metadata for this addon was not found.", shopMetaPath);
        }

        string creatureReference = ReadCreatureReference(shopMetaPath);
        string creaturePath = Path.Combine(streamRoot, creatureReference + ".ymt");
        var heelIndices = new SortedSet<int>();
        foreach (string metaPath in Directory.EnumerateFiles(addonRoot, "*.meta", SearchOption.TopDirectoryOnly))
        {
            string reference;
            try
            {
                reference = ReadCreatureReference(metaPath);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            if (!reference.Equals(creatureReference, StringComparison.OrdinalIgnoreCase)) continue;

            string ymtPath = Path.Combine(streamRoot, Path.GetFileNameWithoutExtension(metaPath) + ".ymt");
            if (!File.Exists(ymtPath)) continue;
            PedFile ped = LoadPedFile(ymtPath);
            foreach (MCComponentInfo info in ped.VariationInfo?.CompInfos ?? [])
            {
                if (info.Data.pedXml_compIdx != 6) continue;
                float height = updatedYmtPath != null &&
                    Path.GetFullPath(ymtPath).Equals(Path.GetFullPath(updatedYmtPath), StringComparison.OrdinalIgnoreCase) &&
                    info.Data.pedXml_drawblIdx == updatedRelativeIndex
                        ? updatedHeelHeight
                        : info.Data.pedXml_expressionMods.f4;
                if (height != 0) heelIndices.Add(info.Data.pedXml_drawblIdx);
            }
        }

        XElement? propExpressions = null;
        string? existingCreaturePath = File.Exists(creaturePath)
            ? creaturePath
            : Directory.EnumerateFiles(streamRoot, "*creaturemetadata*.ymt")
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Contains(
                    gender == Gender.Male ? "mp_m_c_addons" : "mp_f_c_addons",
                    StringComparison.OrdinalIgnoreCase));
        if (existingCreaturePath != null)
        {
            var existing = new RbfFile();
            existing.Load(File.ReadAllBytes(existingCreaturePath));
            propExpressions = XDocument.Parse(RbfXml.GetXml(existing)).Root?.Element("pedPropExpressions");
        }

        var componentExpressions = new XElement("pedCompExpressions",
            heelIndices.Select(index => new XElement("Item",
                new XElement("pedCompID", new XAttribute("value", "0x6")),
                new XElement("pedCompVarIndex", new XAttribute("value", $"0x{index:X}")),
                new XElement("pedCompExpressionIndex", new XAttribute("value", "0x4")),
                new XElement("tracks", new XAttribute("content", "char_array"), 33),
                new XElement("ids", new XAttribute("content", "short_array"), 28462),
                new XElement("types", new XAttribute("content", "char_array"), 2),
                new XElement("components", new XAttribute("content", "char_array"), 1))));
        var document = new XDocument(new XElement("CCreatureMetaData",
            componentExpressions,
            propExpressions == null ? new XElement("pedPropExpressions") : new XElement(propExpressions)));
        var xmlDocument = new XmlDocument();
        using (XmlReader reader = document.CreateReader())
        {
            xmlDocument.Load(reader);
        }
        return (creaturePath, XmlRbf.GetRbf(xmlDocument).Save());
    }

    private static string ReadCreatureReference(string shopMetaPath)
    {
        XElement? element = XDocument.Load(shopMetaPath).Descendants()
            .FirstOrDefault(item => item.Name.LocalName.Equals("creatureMetaData", StringComparison.OrdinalIgnoreCase));
        string value = element?.Value.Trim() ?? string.Empty;
        return value.Length > 0
            ? value
            : throw new InvalidDataException($"No creatureMetaData reference was found in {Path.GetFileName(shopMetaPath)}.");
    }

    private static string GetAssetName(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Split('^')[^1];

    private static IReadOnlyList<TextureLayout> CreateTextureLayout(
        IReadOnlyList<string> texturePaths,
        bool hasSkin)
    {
        var parsed = texturePaths.Select(path =>
        {
            string fullPath = Path.GetFullPath(path);
            Match match = TextureNamePattern.Match(Path.GetFileNameWithoutExtension(fullPath));
            return (Path: fullPath, Match: match);
        }).ToArray();

        if (parsed.All(item => item.Match.Success))
        {
            if (parsed.Length > 26)
            {
                throw new InvalidOperationException("A model can have at most 26 textures (a-z).");
            }

            return parsed
            .OrderBy(item => char.ToLowerInvariant(item.Match.Groups["variant"].Value[0]))
            .ThenBy(item => GetTextureId(item.Match.Groups["suffix"].Value))
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new TextureLayout(
                item.Path,
                (char)('a' + index),
                item.Match.Groups["suffix"].Value.ToLowerInvariant()))
            .ToArray();
        }

        if (texturePaths.Count > 26)
        {
            throw new InvalidOperationException(
                "A model can have at most 26 textures (a-z).");
        }
        return parsed.Select((item, index) => new TextureLayout(
            item.Path,
            (char)('a' + index),
            GetTextureSuffix(item.Path, hasSkin))).ToArray();
    }

    private static string GetTextureSuffix(string path, bool hasSkin)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Match match = TextureNamePattern.Match(name);
        return match.Success ? match.Groups["suffix"].Value.ToLowerInvariant() : hasSkin ? "whi" : "uni";
    }

    private static byte GetTextureId(string suffix) => suffix.ToLowerInvariant() switch
    {
        "uni" => 0,
        "whi" => 1,
        "bla" => 2,
        "chi" => 3,
        "lat" => 4,
        "ara" => 5,
        "kor" => 8,
        "pak" => 10,
        _ => 0
    };

    private static CPVTextureData[] CreateTextureData(IEnumerable<string> texturePaths, bool hasSkin) =>
        texturePaths.Select(path => new CPVTextureData
        {
            texId = GetTextureId(GetTextureSuffix(path, hasSkin)),
            distribution = 255
        }).ToArray();

    private static int GetArrayLength(Array_Structure array) => array.Count1;
}
