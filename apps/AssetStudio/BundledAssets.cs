namespace BLRP.WeaponSkinTool;

internal static class BundledAssets
{
    private static readonly string ToolDirectory = Path.GetDirectoryName(typeof(BundledAssets).Assembly.Location)!;

    public static string BatTemplate(string extension = "") => Path.Combine(
        ToolDirectory,
        "assets",
        "bat-template",
        "w_me_bat_bl_template" + extension);

    public static string CupTemplate() => Path.Combine(
        ToolDirectory,
        "assets",
        "cup-template",
        "prop_coffeecup_template.ydr");

    internal static bool SelfTest() => File.Exists(BatTemplate(".ydr")) &&
        File.Exists(BatTemplate(".ytd")) &&
        File.Exists(CupTemplate());
}
