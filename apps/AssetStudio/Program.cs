namespace BLRP.WeaponSkinTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0].Equals("--optimize-ytd", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                YtdOptimizer.Optimize(args[1], args[2]);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Length is 3 or 4 && args[0].Equals("--render-embedded-inventory", StringComparison.OrdinalIgnoreCase))
        {
            string? replacementPng = args.Length == 4 ? args[2] : null;
            using Bitmap preview = PreviewScene.Load(args[1], null, replacementPng).Render(256, 256, -0.65f, 0.35f, 1f, true);
            InventoryImageExporter.SaveWebp(preview, args[^1]);
            return 0;
        }

        if (args.Length is 3 or 4 && args[0].Equals("--render-embedded-preview", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? replacementPng = args.Length == 4 ? args[2] : null;
                using Bitmap preview = PreviewScene.Load(args[1], null, replacementPng).Render(900, 700, -0.65f, 0.35f, 1f);
                preview.Save(args[^1], System.Drawing.Imaging.ImageFormat.Png);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Length is 4 or 5 && args[0].Equals("--render-preview", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? replacementPng = args.Length == 5 ? args[3] : null;
                string outputPath = args[^1];
                using Bitmap preview = PreviewScene.Load(args[1], args[2], replacementPng).Render(900, 700, -0.65f, 0.35f, 1f);
                preview.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return BundledAssets.SelfTest() && WeaponSkinImporter.SelfTest() && WeaponTextureBuilder.SelfTest() &&
                    WeaponBoneExpander.SelfTest() && InventoryImageExporter.SelfTest() &&
                    MainForm.TextureMatchingSelfTest() ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
