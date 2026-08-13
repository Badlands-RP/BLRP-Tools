using System.Text;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace Badlands.LiveryTool;

internal sealed record LiveryApplyRequest(
    string RepoRoot,
    string VehicleDataFolder,
    string InputImagePath,
    string TemplateYftPath,
    string LiveryFilePrefix,
    int LiveryNumber,
    string VehicleModel,
    int LiverySlot,
    string ModShopLabel,
    string DisplayName,
    string PermissionSuffix,
    int? BlacklistSlot,
    string BlacklistComment,
    bool UpdateModkitMasterList,
    string ModkitMasterListPath,
    string ModkitEntry,
    bool CreateBackups);

internal sealed record LiveryApplyResult(IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Messages);

internal sealed class LiveryWorkflow
{
    private readonly LiveryImageConverter imageConverter = new();

    public LiveryApplyResult Apply(LiveryApplyRequest request)
    {
        Validate(request);

        var changed = new List<string>();
        var messages = new List<string>();
        var repoRoot = Path.GetFullPath(request.RepoRoot);
        var streamDir = Path.Combine(repoRoot, Paths.LiveryStreamRelativePath);
        Directory.CreateDirectory(streamDir);

        var liveryModelName = $"{request.LiveryFilePrefix}{request.LiveryNumber}";
        var ddsPath = Path.Combine(Path.GetTempPath(), "BadlandsLiveryTool", $"{liveryModelName}.dds");
        var yftPath = Path.Combine(streamDir, $"{liveryModelName}.yft");

        var conversion = imageConverter.ConvertToDxt5Dds(request.InputImagePath, ddsPath);
        messages.Add($"Converted image to DXT5 DDS ({conversion.Width}x{conversion.Height}, FourCC {conversion.FourCc}).");

        if (!string.IsNullOrWhiteSpace(request.TemplateYftPath))
        {
            CreateYftFromTemplate(request.TemplateYftPath, yftPath, ddsPath);
            changed.Add(yftPath);
            messages.Add($"Created livery YFT: {yftPath}");
        }
        else
        {
            var sidecarDdsPath = Path.Combine(streamDir, $"{liveryModelName}.dds");
            File.Copy(ddsPath, sidecarDdsPath, overwrite: true);
            changed.Add(sidecarDdsPath);
            messages.Add("No template YFT was selected; wrote the DXT5 DDS beside the stream files instead.");
        }

        var vehicleDataFolder = ResolveVehicleDataFolder(repoRoot, request.VehicleDataFolder);
        var carcolsPath = Path.Combine(vehicleDataFolder, "carcols.meta");
        PatchCarcols(carcolsPath, request.ModShopLabel, liveryModelName, request.CreateBackups);
        changed.Add(carcolsPath);

        var luaPath = Path.Combine(repoRoot, "resources", "lscustoms", "client", "blrp_custom_liveries.lua");
        PatchCustomLiveries(luaPath, request);
        changed.Add(luaPath);

        if (request.UpdateModkitMasterList)
        {
            var modkitPath = ResolveModkitMasterListPath(repoRoot, request.ModkitMasterListPath);
            PatchModkitMasterList(modkitPath, request.ModkitEntry, request.CreateBackups);
            changed.Add(modkitPath);
            messages.Add($"Updated modkit master list with: {request.ModkitEntry.Trim()}");
        }

        return new LiveryApplyResult(changed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), messages);
    }

    private static void Validate(LiveryApplyRequest request)
    {
        if (!Directory.Exists(request.RepoRoot))
        {
            throw new DirectoryNotFoundException($"Repo root was not found: {request.RepoRoot}");
        }

        if (!File.Exists(request.InputImagePath))
        {
            throw new FileNotFoundException("Input image was not found.", request.InputImagePath);
        }

        if (!string.IsNullOrWhiteSpace(request.TemplateYftPath) && !File.Exists(request.TemplateYftPath))
        {
            throw new FileNotFoundException("Template YFT was not found.", request.TemplateYftPath);
        }

        if (string.IsNullOrWhiteSpace(request.LiveryFilePrefix))
        {
            throw new InvalidOperationException("Livery prefix is required.");
        }

        if (request.LiveryNumber < 0)
        {
            throw new InvalidOperationException("Livery number must be zero or greater.");
        }

        if (string.IsNullOrWhiteSpace(request.VehicleModel))
        {
            throw new InvalidOperationException("Vehicle model/hash is required.");
        }

        if (request.LiverySlot < 0)
        {
            throw new InvalidOperationException("Livery slot must be zero or greater.");
        }

        if (string.IsNullOrWhiteSpace(request.ModShopLabel))
        {
            throw new InvalidOperationException("Mod shop label is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidOperationException("Display name is required.");
        }

        if (request.UpdateModkitMasterList && string.IsNullOrWhiteSpace(request.ModkitEntry))
        {
            throw new InvalidOperationException("Modkit entry is required when updating the modkit master list.");
        }
    }

    private static string ResolveVehicleDataFolder(string repoRoot, string vehicleDataFolder)
    {
        if (string.IsNullOrWhiteSpace(vehicleDataFolder))
        {
            return Path.Combine(repoRoot, "resources", "addons", "data", "custom_vehicle_liverys");
        }

        return Path.GetFullPath(vehicleDataFolder);
    }

    private static string ResolveModkitMasterListPath(string repoRoot, string modkitMasterListPath)
    {
        if (string.IsNullOrWhiteSpace(modkitMasterListPath))
        {
            return Paths.GetDefaultModkitMasterListPath(repoRoot);
        }

        return Path.GetFullPath(modkitMasterListPath);
    }

    private static void CreateYftFromTemplate(string templateYftPath, string outputYftPath, string replacementDdsPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BadlandsLiveryTool", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var yft = new YftFile();
            yft.Load(File.ReadAllBytes(templateYftPath));

            var xml = YftXml.GetXml(yft, tempDir);
            var extractedDdsFiles = Directory.GetFiles(tempDir, "*.dds", SearchOption.TopDirectoryOnly);
            if (extractedDdsFiles.Length == 0)
            {
                throw new InvalidOperationException("Template YFT did not export any embedded DDS textures.");
            }

            var targetDds = extractedDdsFiles
                .OrderByDescending(path => new FileInfo(path).Length)
                .First();
            File.Copy(replacementDdsPath, targetDds, overwrite: true);

            var rebuilt = XmlYft.GetYft(xml, tempDir);
            Directory.CreateDirectory(Path.GetDirectoryName(outputYftPath) ?? ".");
            File.WriteAllBytes(outputYftPath, rebuilt.Save());
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void PatchCarcols(string carcolsPath, string modShopLabel, string liveryModelName, bool createBackups)
    {
        if (!File.Exists(carcolsPath))
        {
            throw new FileNotFoundException("carcols.meta was not found.", carcolsPath);
        }

        var text = File.ReadAllText(carcolsPath);
        if (text.Contains($"<modelName>{liveryModelName}</modelName>", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var item = $"""
                <Item>
                  <modelName>{EscapeXml(liveryModelName)}</modelName>
                  <modShopLabel>{EscapeXml(modShopLabel)}</modShopLabel>
                  <linkedModels />
                  <turnOffBones />
                  <type>VMT_LIVERY_MOD</type>
                  <bone>chassis</bone>
                  <collisionBone>chassis</collisionBone>
                  <cameraPos>VMCP_DEFAULT</cameraPos>
                  <audioApply value="1.000000" />
                  <weight value="20" />
                  <turnOffExtra value="false" />
                  <disableBonnetCamera value="false" />
                  <allowBonnetSlide value="true" />
                </Item>

        """;

        var visibleModsClose = FindVisibleModsInsertPoint(text, liveryModelName);
        if (visibleModsClose < 0)
        {
            throw new InvalidOperationException("Could not find a <visibleMods> block in carcols.meta.");
        }

        Backup(carcolsPath, createBackups);
        File.WriteAllText(carcolsPath, text.Insert(visibleModsClose, item));
    }

    private static int FindVisibleModsInsertPoint(string text, string liveryModelName)
    {
        var prefix = Regex.Replace(liveryModelName, @"\d+$", string.Empty);
        var prefixMatch = Regex.Match(
            text,
            $@"<modelName>{Regex.Escape(prefix)}\d+</modelName>",
            RegexOptions.IgnoreCase);

        if (prefixMatch.Success)
        {
            var closeAfterPrefix = text.IndexOf("</visibleMods>", prefixMatch.Index, StringComparison.OrdinalIgnoreCase);
            if (closeAfterPrefix >= 0)
            {
                return closeAfterPrefix;
            }
        }

        return text.IndexOf("</visibleMods>", StringComparison.OrdinalIgnoreCase);
    }

    private static void PatchCustomLiveries(string luaPath, LiveryApplyRequest request)
    {
        if (!File.Exists(luaPath))
        {
            throw new FileNotFoundException("blrp_custom_liveries.lua was not found.", luaPath);
        }

        var text = File.ReadAllText(luaPath);
        var updated = AddTextEntry(text, request.ModShopLabel, request.DisplayName);
        updated = UpsertCustomLivery(updated, request.VehicleModel, request.LiverySlot, BuildLiveryPermissionValue(request.DisplayName, request.PermissionSuffix), null);

        if (request.BlacklistSlot is not null)
        {
            var comment = string.IsNullOrWhiteSpace(request.BlacklistComment) ? null : request.BlacklistComment.Trim();
            updated = UpsertCustomLivery(updated, request.VehicleModel, request.BlacklistSlot.Value, "blacklisted", comment);
        }

        if (!string.Equals(text, updated, StringComparison.Ordinal))
        {
            Backup(luaPath, request.CreateBackups);
            File.WriteAllText(luaPath, updated);
        }
    }

    private static void PatchModkitMasterList(string modkitMasterListPath, string modkitEntry, bool createBackups)
    {
        modkitEntry = modkitEntry.Trim();

        if (!File.Exists(modkitMasterListPath))
        {
            throw new FileNotFoundException("Modkit master list was not found.", modkitMasterListPath);
        }

        var lines = File.ReadAllLines(modkitMasterListPath).ToList();
        if (lines.Any(line => string.Equals(line.Trim(), modkitEntry, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Backup(modkitMasterListPath, createBackups);

        var text = File.ReadAllText(modkitMasterListPath);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal))
        {
            text += newline;
        }

        text += modkitEntry + newline;
        File.WriteAllText(modkitMasterListPath, text);
    }

    private static string AddTextEntry(string text, string label, string displayName)
    {
        var line = $"AddTextEntry('{EscapeLua(label)}', '{EscapeLua(displayName)}')";
        if (Regex.IsMatch(text, $@"AddTextEntry\(['""]{Regex.Escape(label)}['""]\s*,", RegexOptions.IgnoreCase))
        {
            return Regex.Replace(
                text,
                $@"AddTextEntry\(['""]{Regex.Escape(label)}['""]\s*,\s*['""].*?['""]\)",
                line,
                RegexOptions.IgnoreCase);
        }

        var insertIndex = FindAddTextEntryInsertPoint(text);
        if (insertIndex < 0)
        {
            throw new InvalidOperationException("Could not find custom_liveries table in blrp_custom_liveries.lua.");
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return text.Insert(insertIndex, line + newline);
    }

    private static int FindAddTextEntryInsertPoint(string text)
    {
        var matches = Regex.Matches(text, @"(?m)^\s*AddTextEntry\(.+\)\s*(?:--.*)?$");
        if (matches.Count > 0)
        {
            var last = matches[^1];
            var lineEnd = text.IndexOf('\n', last.Index + last.Length);
            return lineEnd < 0 ? text.Length : lineEnd + 1;
        }

        return text.IndexOf("custom_liveries =", StringComparison.OrdinalIgnoreCase);
    }

    private static string UpsertCustomLivery(string text, string vehicleModel, int slot, string value, string? comment)
    {
        var vehiclePattern = $@"(?ms)^  \[`{Regex.Escape(vehicleModel)}`\] = \{{(?<body>.*?)^  \}},";
        var match = Regex.Match(text, vehiclePattern);
        var slotLine = BuildCustomLiveryLine(slot, value, comment);

        if (!match.Success)
        {
            var tableStart = text.IndexOf("custom_liveries =", StringComparison.OrdinalIgnoreCase);
            var firstEntry = text.IndexOf("  [`", tableStart, StringComparison.OrdinalIgnoreCase);
            if (firstEntry < 0)
            {
                throw new InvalidOperationException("Could not find an insertion point in custom_liveries.");
            }

            var newBlock = $"  [`{vehicleModel}`] = {{{Environment.NewLine}{slotLine}{Environment.NewLine}  }},{Environment.NewLine}{Environment.NewLine}";
            return text.Insert(firstEntry, newBlock);
        }

        var block = match.Value;
        var slotPattern = $@"(?m)^    \[{slot}\] = .*$";
        if (Regex.IsMatch(block, slotPattern))
        {
            block = Regex.Replace(block, slotPattern, slotLine);
        }
        else
        {
            var closeIndex = block.LastIndexOf("  },", StringComparison.Ordinal);
            block = block.Insert(closeIndex, slotLine + Environment.NewLine);
        }

        return text[..match.Index] + block + text[(match.Index + match.Length)..];
    }

    private static string BuildCustomLiveryLine(int slot, string value, string? comment)
    {
        var line = $"    [{slot}] = '{EscapeLua(value)}'";
        if (!string.IsNullOrWhiteSpace(comment))
        {
            line += $", -- {comment.Trim()}";
        }
        else
        {
            line += ",";
        }

        return line;
    }

    private static string BuildLiveryPermissionValue(string displayName, string permissionSuffix)
    {
        permissionSuffix = permissionSuffix.Trim();
        if (string.IsNullOrWhiteSpace(permissionSuffix))
        {
            return displayName.Trim();
        }

        permissionSuffix = permissionSuffix.TrimStart(':');
        return $"{displayName.Trim()}:{permissionSuffix}";
    }

    private static void Backup(string path, bool createBackups)
    {
        if (!createBackups)
        {
            return;
        }

        var backupPath = $"{path}.{DateTime.Now:yyyyMMddHHmmss}.bak";
        File.Copy(path, backupPath, overwrite: false);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary extraction folders are best-effort cleanup only.
        }
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string EscapeLua(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    }
}
