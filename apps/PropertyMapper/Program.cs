namespace BLRP.PropertyMapper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            string shared = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "shared", assemblyName.Name + ".dll"));
            return File.Exists(shared) ? System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(shared) : null;
        };
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = PropertyMapDocument.SelfTest() ? 0 : 1;
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.FirstOrDefault(File.Exists)));
    }
}
