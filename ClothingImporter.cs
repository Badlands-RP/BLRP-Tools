using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

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
    bool CopyRawAssets = false)
{
    public int CountAfterImport => ExistingCount + 1;
    public int RemainingSlots => ClothingImporter.MaxDrawablesPerType - CountAfterImport;
}

internal sealed record ClothingTextureImportResult(string TexturePath, int TextureCount);

internal static class ClothingImporter
{
    public const int MaxDrawablesPerType = 128;

    internal static string DumpYmtXml(string path) => MetaXml.GetXml(LoadPedFile(path).Meta);

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
        if (component.IsProp)
        {
            throw new NotSupportedException("Component models are supported; prop import is not available yet.");
        }
        if (!File.Exists(modelPath) || !new[] { ".ydd", ".ydr" }.Contains(Path.GetExtension(modelPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Select a valid .ydd or .ydr model.", modelPath);
        }
        if (texturePaths.Count < 1 || texturePaths.Any(path => !File.Exists(path) || !Path.GetExtension(path).Equals(".ytd", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Select one or more valid .ytd texture files.");
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
        byte[] modelBytes = plan.CopyRawAssets
            ? BuildDuplicateModel(plan.ModelPath, plan.Component, modelAssetName, textureAssetNames[0])
            : BuildModel(plan);
        byte[][] textureBytes = plan.TexturePaths
            .Select((path, index) => plan.CopyRawAssets
                ? BuildDuplicateTexture(path, textureAssetNames[index])
                : BuildTexture(path))
            .ToArray();
        byte[] ymtBytes = AppendComponentDrawable(
            plan.PedFile,
            plan.CollectionName,
            plan.Component.Slot,
            plan.HasSkin,
            plan.TexturePaths,
            plan.DrawableTemplate,
            plan.ComponentInfoTemplate);

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

        var written = new List<string>();
        try
        {
            File.WriteAllBytes(modelTarget, modelBytes);
            written.Add(modelTarget);
            for (int index = 0; index < textureTargets.Length; index++)
            {
                File.WriteAllBytes(textureTargets[index], textureBytes[index]);
                written.Add(textureTargets[index]);
            }

            string temporaryYmt = plan.YmtPath + ".blrp-importing";
            File.WriteAllBytes(temporaryYmt, ymtBytes);
            File.Move(temporaryYmt, plan.YmtPath, true);
            written.Add(plan.YmtPath);
            return written;
        }
        catch
        {
            foreach (string path in written.Where(path => !path.Equals(plan.YmtPath, StringComparison.OrdinalIgnoreCase)))
            {
                if (File.Exists(path)) File.Delete(path);
            }
            throw;
        }
    }

    public static ClothingTextureImportResult ImportTexture(
        string rootPath,
        ClothingEntry target,
        string sourceTexturePath)
    {
        if (target.Component.IsProp)
        {
            throw new NotSupportedException("Prop texture import is not supported yet.");
        }
        string fullRoot = Path.GetFullPath(rootPath);
        string fullTargetModel = Path.GetFullPath(target.FilePath);
        if (!fullTargetModel.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected model is outside the active EUP directory.");
        }
        if (!File.Exists(fullTargetModel))
        {
            throw new FileNotFoundException("The target clothing model was not found.", fullTargetModel);
        }
        if (!File.Exists(sourceTexturePath) || !Path.GetExtension(sourceTexturePath).Equals(".ytd", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Select a valid .ytd texture file.", sourceTexturePath);
        }

        string collection = GetCollectionName(target.Gender, target.Pack);
        string ymtPath = Path.Combine(fullRoot, $"clothing_addon_{target.Pack}", "stream", collection + ".ymt");
        PedFile ped = LoadPedFile(ymtPath);
        MCPVDrawblData drawable = GetDrawables(ped, target.Component.Slot)?
            .ElementAtOrDefault(target.RelativeIndex)
            ?? throw new InvalidDataException("The target model has no matching YMT drawable entry.");
        int textureCount = drawable.TexData?.Length ?? 0;
        if (textureCount >= 26)
        {
            throw new InvalidOperationException("This drawable already has the maximum 26 textures.");
        }

        bool hasSkin = ((drawable.Data.propMask >> 4) & 3) == 1 ||
            Path.GetFileNameWithoutExtension(fullTargetModel).EndsWith("_r", StringComparison.OrdinalIgnoreCase);
        string suffix = GetTextureSuffix(sourceTexturePath, hasSkin);
        char variant = (char)('a' + textureCount);
        string assetName = $"{target.Component.Code}_diff_{target.RelativeIndex:000}_{variant}_{suffix}";
        string fileName = $"{collection}^{assetName}.ytd";
        string targetDirectory = Path.GetDirectoryName(fullTargetModel)!;
        string textureTarget = Path.Combine(targetDirectory, fileName);
        if (File.Exists(textureTarget))
        {
            throw new IOException("Import target already exists: " + textureTarget);
        }

        byte[] textureBytes = BuildDuplicateTexture(sourceTexturePath, assetName);
        byte[] ymtBytes = AppendComponentTexture(
            ped,
            collection,
            target.Component.Slot,
            target.RelativeIndex,
            suffix);

        string backupRoot = Path.Combine(fullRoot, ".clothing-locator-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backupRoot);
        File.Copy(ymtPath, Path.Combine(backupRoot, Path.GetFileName(ymtPath)), false);
        try
        {
            File.WriteAllBytes(textureTarget, textureBytes);
            string temporaryYmt = ymtPath + ".blrp-importing";
            File.WriteAllBytes(temporaryYmt, ymtBytes);
            File.Move(temporaryYmt, ymtPath, true);
            return new ClothingTextureImportResult(textureTarget, textureCount + 1);
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
        ClothingTextureImportResult textureImport = ImportTexture(fixtureRoot, importedEntry, sourceTexture);
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
        Import(racePlan);
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
            racePlan.TextureFileNames.All(name => File.Exists(Path.Combine(racePlan.TargetDirectory, name)));

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
                texture.NameHash == JenkHash.GenHash(duplicateTextureNames[index].ToLowerInvariant())).All(valid => valid)
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

        return countAfter == countBefore + 1 &&
               countAfterRawImport == countAfter + 1 &&
               selectorPlansValid &&
               raceImportValid &&
               textureImportValid &&
               hairShaderConversionValid &&
               duplicateValid &&
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
        YddFile ydd = LoadYdd(sourcePath);
        DrawableDictionary dictionary = ydd.DrawableDict ??
            throw new InvalidDataException("The source YDD has no drawable dictionary.");
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
    {
        YtdFile ytd = LoadYtd(sourcePath);
        TextureDictionary dictionary = ytd.TextureDict ??
            throw new InvalidDataException($"Texture dictionary missing from {Path.GetFileName(sourcePath)}.");
        Texture[] textures = dictionary.Textures?.data_items ?? [];
        if (textures.Length != 1)
        {
            throw new InvalidDataException($"Each clothing YTD must contain exactly one texture; {Path.GetFileName(sourcePath)} contains {textures.Length}.");
        }

        textures[0].Name = targetName;
        return ytd.Save();
    }

    private static byte[] AppendComponentDrawable(
        PedFile ped,
        string collectionName,
        int componentId,
        bool hasSkin,
        IReadOnlyList<string> texturePaths,
        MCPVDrawblData? drawableTemplate = null,
        MCComponentInfo? componentInfoTemplate = null) => RebuildComponent(
            ped,
            collectionName,
            componentId,
            hasSkin,
            texturePaths,
            drawableTemplate,
            componentInfoTemplate,
            null,
            null);

    private static byte[] AppendComponentTexture(
        PedFile ped,
        string collectionName,
        int componentId,
        int drawableIndex,
        string textureSuffix) => RebuildComponent(
            ped,
            collectionName,
            componentId,
            false,
            [],
            null,
            null,
            drawableIndex,
            textureSuffix);

    private static byte[] RebuildComponent(
        PedFile ped,
        string collectionName,
        int componentId,
        bool hasSkin,
        IReadOnlyList<string> texturePaths,
        MCPVDrawblData? drawableTemplate,
        MCComponentInfo? componentInfoTemplate,
        int? textureTargetIndex,
        string? textureSuffix)
    {
        MCPedVariationInfo source = ped.VariationInfo ?? throw new InvalidDataException("The target YMT has no ped variation data.");
        MCPVComponentData[] sourceComponents = source.ComponentData3 ?? [];
        byte[] componentIndices = source.ComponentIndices?.ToArray() ?? Enumerable.Repeat((byte)255, 12).ToArray();
        int targetComponentIndex = componentIndices[componentId];
        bool createComponent = targetComponentIndex == 255;
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
                    CPVDrawblData data = existingDrawable.Data;
                    IEnumerable<CPVTextureData> textures = existingDrawable.TexData ?? [];
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

            if (componentIndex == targetComponentIndex && textureTargetIndex == null)
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
        if (textureTargetIndex == null)
        {
            CComponentInfo componentInfo = componentInfoTemplate?.Data ?? new CComponentInfo
            {
                pedXml_audioID = JenkHash.GenHash("none"),
                pedXml_audioID2 = JenkHash.GenHash("none")
            };
            componentInfo.pedXml_compIdx = (byte)componentId;
            componentInfo.pedXml_drawblIdx = (byte)((GetDrawables(ped, componentId)?.Length) ?? 0);
            componentInfos.Add(componentInfo);
        }
        root.compInfos = mb.AddItemArrayPtr(MetaName.CComponentInfo, componentInfos.ToArray());

        CPedPropInfo propInfo = source.PropInfo?.Data ?? new CPedPropInfo();
        var props = new List<CPedPropMetaData>();
        foreach (MCPedPropMetaData wrapper in source.PropInfo?.PropMetaData ?? [])
        {
            CPedPropMetaData data = wrapper.Data;
            data.texData = mb.AddItemArrayPtr(MetaName.CPedPropTexData, wrapper.TexData ?? []);
            props.Add(data);
        }
        propInfo.aPropMetaData = mb.AddItemArrayPtr(MetaName.CPedPropMetaData, props.ToArray());

        var anchors = new List<CAnchorProps>();
        foreach (MCAnchorProps wrapper in source.PropInfo?.Anchors ?? [])
        {
            CAnchorProps data = wrapper.Data;
            data.props = mb.AddByteArrayPtr(wrapper.Props ?? []);
            anchors.Add(data);
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

    private static PedFile LoadPedFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return RpfFile.GetResourceFile<PedFile>(bytes) ?? throw new InvalidDataException("Unable to read YMT: " + path);
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
