using System.Reflection;
using System.Windows.Forms;

namespace BLRP.PropertyMapPreview;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 5 || !File.Exists(args[0]) || !TryReadVector(args, out float x, out float y, out float z, out float radius))
        {
            MessageBox.Show("This helper is launched by BLRP Property Mapper.", "BLRP Property Mapper 3D Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string codeWalkerPath = Path.Combine(AppContext.BaseDirectory, "CodeWalker.exe");
        if (!File.Exists(codeWalkerPath))
        {
            MessageBox.Show("CodeWalker.exe was not found beside the preview helper.", "3D preview unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Assembly codeWalker = Assembly.LoadFrom(codeWalkerPath);
        Type worldFormType = codeWalker.GetType("CodeWalker.WorldForm", throwOnError: true)!;
        Form world = (Form)Activator.CreateInstance(worldFormType)!;
        world.Text = "BLRP Property Mapper — GTA 3D Preview";
        world.Shown += (_, _) => LoadPreviewWhenReady(worldFormType, world, args[0], x, y, z, radius);
        Application.Run(world);
    }

    private static void LoadPreviewWhenReady(Type worldFormType, Form world, string ymapPath, float x, float y, float z, float radius)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) =>
        {
            try
            {
                object cache = worldFormType.GetProperty("GameFileCache")!.GetValue(world)!;
                bool isReady = (bool)cache.GetType().GetField("IsInited")!.GetValue(cache)!;
                if (!isReady || world.IsDisposed) return;

                timer.Stop();
                worldFormType.GetMethod("ShowProjectForm", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, null);
                object project = worldFormType.GetField("ProjectForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
                project.GetType().GetMethod("OpenFiles")!.Invoke(project, new object[] { new[] { ymapPath } });

                Type vectorType = Assembly.Load("SharpDX.Mathematics").GetType("SharpDX.Vector3", throwOnError: true)!;
                object position = Activator.CreateInstance(vectorType, x, y, z)!;
                object bounds = Activator.CreateInstance(vectorType, radius, radius, radius)!;
                worldFormType.GetMethod("GoToPosition", new[] { vectorType, vectorType })!.Invoke(world, new[] { position, bounds });
            }
            catch (Exception exception)
            {
                timer.Stop();
                MessageBox.Show(exception.GetBaseException().Message, "Could not open 3D preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        timer.Start();
    }

    private static bool TryReadVector(string[] args, out float x, out float y, out float z, out float radius)
    {
        x = y = z = radius = 0;
        return float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
               float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) &&
               float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z) &&
               float.TryParse(args[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius) &&
               radius > 0;
    }
}
