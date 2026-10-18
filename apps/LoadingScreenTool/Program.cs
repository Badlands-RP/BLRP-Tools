namespace BLRP.LoadingScreenTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            Environment.ExitCode = WorkflowSelfTest.Run() ? 0 : 1;
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
