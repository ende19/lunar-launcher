namespace LunarLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        LauncherConfig config;
        try { config = LauncherConfig.Load(); }
        catch (LauncherException error)
        {
            MessageBox.Show(error.Message, "Lunar Roleplay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(new MainForm(config));
    }
}
