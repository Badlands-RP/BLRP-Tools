namespace BLRP.NavMesh;

internal sealed record IssueGroup(string Title, string NextStep, string[] Messages)
{
    public override string ToString() => $"{Title} ({Messages.Length})";

    internal static IssueGroup[] From(IEnumerable<string> messages) => messages.Where(m => !string.IsNullOrWhiteSpace(m))
        .Distinct().GroupBy(Category).OrderBy(g => g.Key).Select(group =>
        {
            var (title, action) = group.Key switch
            {
                0 => ("Protected models", "The map author has protected these files. This preview leaves their collision out. A complete build needs readable collision from the author or reviewed replacement collision; changing the preview settings will not unlock the files."),
                1 => ("Missing map or game objects", "Some objects are supplied by another resource or GTA DLC. Add the owning resource, or open Advanced settings → Game sources to include the correct DLC. The preview only contains geometry the tool could read."),
                2 => ("Collision shapes need support", "These collision shapes cannot be extracted by this version of the tool. A complete build needs supported replacement collision or additional extractor support. The remaining geometry can still be previewed."),
                3 => ("Game files need verification", "Your installed game files are available for previews. Before exporting, check that the selected sources match the server build, including DLC/update overrides. Hashes and source verification are under Advanced settings → Game sources. Auto-detection does not verify a game build."),
                4 => ("Surrounding navigation needs attention", "Open Export to prepare the original navigation. Missing tiles, special navigation data or disconnected entrances must be resolved before creating a server resource."),
                _ => ("Other checks", "Review the details below. The complete report and input snapshot are available through Open results.")
            };
            return new IssueGroup(title, action, group.ToArray());
        }).ToArray();

    private static int Category(string message)
    {
        if (message.StartsWith("Escrow-protected") || message.StartsWith("Unreadable native asset")) return 0;
        if (message.StartsWith("Missing owning YTYP") || message.StartsWith("Missing collision/drawable") || message.StartsWith("Cannot locate object definition")) return 1;
        if (message.StartsWith("Unsupported") || message.StartsWith("Fragment physics")) return 2;
        if (message.StartsWith("Archive ") || message.StartsWith("Game archive") || message.StartsWith("Game source") || message.Contains("target build remains unverified")) return 3;
        if (message.Contains("baseline", StringComparison.OrdinalIgnoreCase) || message.Contains("navigation point", StringComparison.OrdinalIgnoreCase) || message.Contains("does not connect", StringComparison.OrdinalIgnoreCase)) return 4;
        return 5;
    }
}
