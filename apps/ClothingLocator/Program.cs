namespace BLRP.ClothingLocator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--batch-previews")
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new PreviewExportDialog(args[1]) { ShowInTaskbar = true });
            return 0;
        }
        if (args.Length == 4 && args[0] == "--preview-export-self-test")
        {
            try { return ClothingPreviewExporter.SelfTest(args[1], args[2], args[3]) ? 0 : 1; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
        if (args.Length >= 4 && args[0] == "--export-previews")
        {
            try
            {
                string[] options = args.Skip(4).Where(arg => arg != "--public-only").ToArray();
                if (options.Length > 2) throw new ArgumentException("Expected optional game build and image size.");
                int build = options.Length > 0 ? int.Parse(options[0]) : BaseGameCatalog.DetectInstallation().GameBuild;
                int size = options.Length > 1 ? int.Parse(options[1]) : 256;
                var progress = new ConsolePreviewProgress();
                using var cancellation = new CancellationTokenSource();
                Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
                ClothingPreviewResult result = ClothingPreviewExporter.RunAsync(args[1], args[2], args[3], build, size,
                    args.Contains("--public-only"), progress, cancellation.Token).GetAwaiter().GetResult();
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
                return result.Failed == 0 ? 0 : 2;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
        if (args.Contains("--git-self-test", StringComparer.OrdinalIgnoreCase))
        {
            string[] values = args.Where(arg => !arg.Equals("--git-self-test", StringComparison.OrdinalIgnoreCase)).ToArray();
            return values.Length == 2 && GitFileHistory.SelfTest(values[0], values[1]) ? 0 : 1;
        }

        if (args.Contains("--lod-preview-self-test", StringComparer.OrdinalIgnoreCase))
        {
            string[] values = args.Where(arg => !arg.Equals("--lod-preview-self-test", StringComparison.OrdinalIgnoreCase)).ToArray();
            try { return values.Length == 2 && LodPreviewScene.SelfTest(values[0], values[1]) ? 0 : 1; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        if (args.Contains("--lod-self-test", StringComparer.OrdinalIgnoreCase))
        {
            string root = args.FirstOrDefault(arg => !arg.Equals("--lod-self-test", StringComparison.OrdinalIgnoreCase))
                ?? @"D:\BadlandsRP_EUP";
            try { return ClothingLodGenerator.SelfTest(root) ? 0 : 1; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        if (args.Contains("--repair-heel-metadata", StringComparer.OrdinalIgnoreCase))
        {
            string[] values = args.Where(arg => !arg.Equals("--repair-heel-metadata", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (values.Length != 3 || !Enum.TryParse(values[1], true, out Gender gender) || !int.TryParse(values[2], out int pack)) return 1;
            try
            {
                Console.WriteLine(ClothingImporter.RepairHeelMetadata(values[0], gender, pack).CreatureMetadataPath);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Contains("--dump-ymt-xml", StringComparer.OrdinalIgnoreCase))
        {
            string[] values = args.Where(arg => !arg.Equals("--dump-ymt-xml", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (values.Length != 2) return 1;
            try
            {
                File.WriteAllText(values[1], ClothingImporter.DumpYmtXml(values[0]));
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Contains("--compat-import-test", StringComparer.OrdinalIgnoreCase))
        {
            string[] values = args.Where(arg => !arg.Equals("--compat-import-test", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (values.Length < 6) return 1;
            try
            {
                Gender gender = Enum.Parse<Gender>(values[2], true);
                ComponentDefinition component = ClothingComponents.ByCode[values[3]];
                string output = ClothingImporter.CompatibilityImportTest(
                    values[0], values[1], gender, component, values[4], values.Skip(5).ToArray());
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Contains("--asset-self-test", StringComparer.OrdinalIgnoreCase))
        {
            string[] values = args.Where(arg => !arg.Equals("--asset-self-test", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (values.Length < 2) return 1;
            try
            {
                return ClothingImporter.AssetSelfTest(values[0], values.Skip(1).ToArray()) ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Contains("--import-self-test", StringComparer.OrdinalIgnoreCase))
        {
            string[] values = args.Where(arg => !arg.Equals("--import-self-test", StringComparison.OrdinalIgnoreCase)).ToArray();
            string sourceRoot = values.ElementAtOrDefault(0) ?? @"D:\BadlandsRP_EUP";
            string fixtureRoot = values.ElementAtOrDefault(1) ?? Path.Combine(
                Path.GetTempPath(),
                "BLRP-Clothing-Importer-Test-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
            try
            {
                return ClothingImporter.SelfTest(sourceRoot, fixtureRoot) ? 0 : 1;
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(fixtureRoot);
                File.WriteAllText(Path.Combine(fixtureRoot, "self-test-error.txt"), exception.ToString());
                return 1;
            }
        }

        if (args.Contains("--base-self-test", StringComparer.OrdinalIgnoreCase))
        {
            string outputPath = args.FirstOrDefault(arg => !arg.Equals("--base-self-test", StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(AppContext.BaseDirectory, "base-self-test-output");
            try
            {
                return BaseGameCatalog.SelfTest(outputPath) ? 0 : 1;
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(outputPath);
                File.WriteAllText(Path.Combine(outputPath, "self-test-error.txt"), exception.ToString());
                return 1;
            }
        }

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            string? rootPath = args.FirstOrDefault(arg => !arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
            return ClothingCatalog.SelfTest(rootPath) &&
                ClothingBlacklist.SelfTest() &&
                BlacklistBundle.SelfTest() &&
                ClothingImporter.QualitySelfTest(rootPath) &&
                BlacklistGroupPicker.SelfTest() && ImportDirectories.SelfTest() &&
                BusinessDirectory.Normalize([" Zebra ", "alpha", "ALPHA"]).SequenceEqual(["alpha", "Zebra"]) &&
                TextureBlacklistDialog.SelfTest() &&
                MainForm.SelfTest() ? 0 : 1;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private sealed class ConsolePreviewProgress : IProgress<ClothingPreviewProgress>
    {
        public void Report(ClothingPreviewProgress value)
        {
            if (value.Completed % 100 == 0 || value.Completed == value.Total)
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(value));
        }
    }
}
