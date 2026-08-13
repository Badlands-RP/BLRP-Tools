using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

namespace BLRP.ToolsHub;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 5 && args[0] == "--apply-update")
        {
            ApplyUpdate(int.Parse(args[1]), args[2], args[3], args[4]);
            return;
        }
        if (args.Length == 2 && args[0] == "--run-tool")
        {
            RunTool(args[1]);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void RunTool(string assemblyPath)
    {
        try
        {
            string fullAssemblyPath = Path.GetFullPath(assemblyPath, AppContext.BaseDirectory);
            var context = new ToolContext(fullAssemblyPath, Path.Combine(AppContext.BaseDirectory, "shared"));
            Assembly assembly = context.LoadFromAssemblyPath(fullAssemblyPath);
            MethodInfo entry = assembly.EntryPoint ?? throw new InvalidDataException("The selected tool has no entry point.");
            object? result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [Array.Empty<string>()]);
            if (result is Task task) task.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            MessageBox.Show("The tool could not be started.\n\n" + (exception.InnerException?.Message ?? exception.Message),
                "BLRP Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ApplyUpdate(int processId, string zipPath, string installDirectory, string launcherPath)
    {
        try
        {
            try { Process.GetProcessById(processId).WaitForExit(60_000); }
            catch (ArgumentException) { }

            string extracted = Path.Combine(Path.GetTempPath(), "BLRP-Tools-Update-" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(zipPath, extracted);
            string[] roots = Directory.GetDirectories(extracted);
            string source = roots.Length == 1 && Directory.GetFiles(extracted).Length == 0 ? roots[0] : extracted;
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(installDirectory, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
            Process.Start(new ProcessStartInfo(launcherPath) { UseShellExecute = true, WorkingDirectory = installDirectory });
        }
        catch (Exception exception)
        {
            MessageBox.Show("The BLRP Tools update failed.\n\n" + exception.Message,
                "BLRP Tools Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed class ToolContext(string assemblyPath, string sharedDirectory) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is not null && File.Exists(resolved)) return LoadFromAssemblyPath(resolved);
            string shared = Path.Combine(sharedDirectory, assemblyName.Name + ".dll");
            return File.Exists(shared) ? LoadFromAssemblyPath(shared) : null;
        }

        protected override nint LoadUnmanagedDll(string name)
        {
            string? resolved = _resolver.ResolveUnmanagedDllToPath(name);
            return resolved is null ? 0 : LoadUnmanagedDllFromPath(resolved);
        }
    }
}
