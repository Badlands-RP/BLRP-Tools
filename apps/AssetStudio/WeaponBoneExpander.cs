using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace BLRP.WeaponSkinTool;

internal sealed record WeaponBonePlan(
    string BoneName,
    string? ModelPath,
    byte[]? OriginalBytes,
    byte[]? UpdatedBytes,
    IReadOnlyList<string> AddedBones)
{
    public bool Changed => UpdatedBytes is not null;
}

internal static class WeaponBoneExpander
{
    public static string BoneForSkin(int skinIndex)
    {
        int group = (skinIndex - 1) / 12;
        if (group == 0) return "Gun_Root";
        if (group > 26) throw new InvalidDataException("Weapon skin groups beyond WAPSkinZ are not supported.");
        return "WAPSkin" + (char)('A' + group - 1);
    }

    public static WeaponBonePlan Plan(string streamDirectory, string modelPrefix, int skinIndex)
    {
        string targetBone = BoneForSkin(skinIndex);
        if (targetBone == "Gun_Root") return new WeaponBonePlan(targetBone, null, null, null, []);
        string modelPath = FindBaseModel(streamDirectory, modelPrefix);
        byte[] original = File.ReadAllBytes(modelPath);
        byte[] updated = EnsureBone(original, targetBone, out string[] added);
        return new WeaponBonePlan(targetBone, modelPath, original, added.Length == 0 ? null : updated, added);
    }

    public static byte[] EnsureBone(byte[] modelBytes, string targetBone, out string[] addedBones)
    {
        Match target = Regex.Match(targetBone, "^WAPSkin(?<letter>[A-Z])$", RegexOptions.CultureInvariant);
        if (!target.Success) throw new InvalidDataException($"Unsupported weapon skin bone name: {targetBone}");
        var ydr = new YdrFile();
        ydr.Load(modelBytes);
        Skeleton skeleton = ydr.Drawable?.Skeleton ?? throw new InvalidDataException("The base weapon YDR has no skeleton.");
        Bone[] bones = skeleton.Bones?.Items ?? throw new InvalidDataException("The base weapon YDR has no bones.");
        Bone root = bones.FirstOrDefault(bone => IsBone(bone, "Gun_Root"))
            ?? throw new InvalidDataException("The base weapon skeleton has no Gun_Root bone.");
        var skinBones = Enumerable.Range('A', 26)
            .Select(letter => (Letter: (char)letter, Bone: bones.FirstOrDefault(bone => IsBone(bone, "WAPSkin" + (char)letter))))
            .Where(item => item.Bone is not null)
            .Select(item => (item.Letter, Bone: item.Bone!))
            .ToArray();
        if (skinBones.Length == 0 || skinBones[0].Letter != 'A')
            throw new InvalidDataException("The base weapon skeleton needs an existing WAPSkinA bone as its expansion template.");

        char targetLetter = target.Groups["letter"].Value[0];
        char highestLetter = skinBones.Max(item => item.Letter);
        for (char letter = 'A'; letter <= highestLetter; letter++)
        {
            if (!skinBones.Any(item => item.Letter == letter))
                throw new InvalidDataException("The base weapon has a gap in its WAPSkin bone sequence.");
        }
        if (highestLetter >= targetLetter)
        {
            addedBones = [];
            return modelBytes;
        }

        var added = new List<string>();
        var expanded = bones.ToList();
        Bone template = skinBones[^1].Bone;
        for (char letter = (char)(highestLetter + 1); letter <= targetLetter; letter++)
        {
            string name = "WAPSkin" + letter;
            short index = checked((short)expanded.Count);
            template.NextSiblingIndex = index;
            Bone bone = CreateSkinBone(template, root, index, name);
            expanded.Add(bone);
            template = bone;
            added.Add(name);
        }

        skeleton.Bones.Items = expanded.ToArray();
        skeleton.BuildIndices();
        skeleton.AssignBoneParents();
        skeleton.BuildBoneTags();
        skeleton.BuildTransformations();
        skeleton.BuildBonesMap();
        byte[] result = ydr.Save();
        Verify(result, bones.Length, targetBone, added.Count);
        addedBones = added.ToArray();
        return result;
    }

    private static string FindBaseModel(string streamDirectory, string modelPrefix)
    {
        Regex numberedSkin = new($"^{Regex.Escape(modelPrefix)}_\\d+$", RegexOptions.IgnoreCase);
        string[] candidates = Directory.EnumerateFiles(streamDirectory, "*.ydr", SearchOption.TopDirectoryOnly)
            .Where(path => !numberedSkin.IsMatch(Path.GetFileNameWithoutExtension(path)))
            .Where(HasWeaponSkinSkeleton)
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new FileNotFoundException("Could not find a base weapon YDR with Gun_Root and WAPSkinA bones in the configured stream directory."),
            _ => throw new InvalidDataException("Multiple possible base weapon YDRs were found; keep only this weapon's base model in its configured stream directory.")
        };
    }

    private static bool HasWeaponSkinSkeleton(string path)
    {
        try
        {
            var ydr = new YdrFile();
            ydr.Load(File.ReadAllBytes(path));
            Bone[] bones = ydr.Drawable?.Skeleton?.Bones?.Items ?? [];
            return bones.Any(bone => IsBone(bone, "Gun_Root")) &&
                bones.Any(bone => IsBone(bone, "WAPSkinA"));
        }
        catch { return false; }
    }

    private static void Verify(byte[] modelBytes, int previousCount, string targetBone, int addedCount)
    {
        var reloaded = new YdrFile();
        reloaded.Load(modelBytes);
        Bone[] bones = reloaded.Drawable?.Skeleton?.Bones?.Items ?? [];
        if (bones.Length != previousCount + addedCount ||
            !bones.Any(bone => IsBone(bone, targetBone)) ||
            bones.Select(bone => bone.Index).Distinct().Count() != bones.Length ||
            bones.Any(bone => bone.Index != bone.Index2))
            throw new InvalidDataException("The expanded weapon skeleton failed reload verification.");
    }

    public static bool SelfTest()
    {
        string template = BundledAssets.BatTemplate(".ydr");
        var ydr = new YdrFile();
        ydr.Load(File.ReadAllBytes(template));
        Skeleton skeleton = ydr.Drawable!.Skeleton!;
        var initial = skeleton.Bones.Items.ToList();
        Bone root = initial.Single(bone => IsBone(bone, "Gun_Root"));
        initial.Add(CreateSkinBone(root, root, checked((short)initial.Count), "WAPSkinA"));
        skeleton.Bones.Items = initial.ToArray();
        skeleton.BuildIndices();
        skeleton.AssignBoneParents();
        skeleton.BuildBoneTags();
        skeleton.BuildTransformations();
        skeleton.BuildBonesMap();
        byte[] after = EnsureBone(ydr.Save(), "WAPSkinE", out string[] added);
        var verified = new YdrFile();
        verified.Load(after);
        Bone[] actual = verified.Drawable!.Skeleton!.Bones.Items;
        Bone[] skins = Enumerable.Range('A', 5)
            .Select(letter => actual.Single(bone => IsBone(bone, "WAPSkin" + (char)letter)))
            .ToArray();
        return added.SequenceEqual(["WAPSkinB", "WAPSkinC", "WAPSkinD", "WAPSkinE"]) &&
            skins.All(bone => bone.ParentIndex == actual.Single(root => IsBone(root, "Gun_Root")).Index) &&
            skins.Take(4).Select(bone => bone.NextSiblingIndex).SequenceEqual(skins.Skip(1).Select(bone => bone.Index)) &&
            skins[^1].NextSiblingIndex == -1;
    }

    private static bool IsBone(Bone bone, string name) =>
        bone.Tag == Bone.CalculateBoneHash(name) || string.Equals(bone.Name, name, StringComparison.OrdinalIgnoreCase);

    private static Bone CreateSkinBone(Bone template, Bone root, short index, string name) => new()
    {
        Name = name,
        Tag = Bone.CalculateBoneHash(name),
        Index = index,
        Index2 = index,
        ParentIndex = root.Index,
        NextSiblingIndex = -1,
        Flags = template.Flags,
        Rotation = template.Rotation,
        Translation = template.Translation,
        Scale = template.Scale,
        Unknown_1Ch = template.Unknown_1Ch,
        Unknown_2Ch = template.Unknown_2Ch,
        Unknown_34h = template.Unknown_34h,
        Unknown_48h = template.Unknown_48h,
        TransformUnk = template.TransformUnk
    };
}
