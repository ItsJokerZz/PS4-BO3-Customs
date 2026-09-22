namespace FFPorter.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Core.T7.Harness.T7NativeTasks.Register();
        return AppHost.Run(new T7Edition(), args);
    }
}
