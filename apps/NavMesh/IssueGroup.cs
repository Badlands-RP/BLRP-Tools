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
                1 => ("Missing map or game objects", "Add the owning resource or matching GTA DLC to include these objects. For a test build, enable Allow warnings under Export: unreadable objects are skipped and remain listed in the report. NPC routes may cross the omitted props."),
                2 => ("Collision shapes need support", "These collision shapes cannot be extracted by this version of the tool. A complete build needs supported replacement collision or additional extractor support. The remaining geometry can still be previewed."),
                3 => ("Game files need verification", "These are source/hash checks, not a count of broken game files. Use Calculate hashes and verify the source selection under Advanced settings → Game sources for a complete build, or enable Allow warnings under Export for a test build. File hash mismatches still stop the build."),
                4 => ("Surrounding navigation needs attention", "Test builds retain original points and special-route polygons, and report disconnected entrances for in-game review. Missing tiles, changed hashes and invalid references still stop a build. See Export for original navigation settings."),
                5 => ("Conflicting resource assets", "Selected resources supply different versions of the same asset or definition. The preview uses the first selected copy. Resolve the active version for a complete build, or enable Allow warnings under Export to use that same selection in a test resource."),
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
        if (message.StartsWith("Original navigation") || message.Contains("baseline", StringComparison.OrdinalIgnoreCase) || message.Contains("navigation point", StringComparison.OrdinalIgnoreCase) || message.Contains("does not connect", StringComparison.OrdinalIgnoreCase)) return 4;
        if (message.StartsWith("Conflicting resource") || message.StartsWith("Conflicting archetype")) return 5;
        return 6;
    }
}
