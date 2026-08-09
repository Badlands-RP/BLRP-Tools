namespace BLRP.ClothingLocator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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
            return ClothingCatalog.SelfTest(rootPath) && MainForm.SelfTest() ? 0 : 1;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
