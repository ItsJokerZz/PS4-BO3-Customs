using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using FFPorter.Core.Common.Native;

namespace FFPorter.Desktop;

public static class AppHost
{
    public const string BackendVerb = "--backend";

    public static int Run(Edition edition, string[] args)
    {
        Environment.SetEnvironmentVariable(NativeProcess.HostVariable, Environment.ProcessPath);
        Edition.Use(edition);
        if (args.Length > 0 && (args[0] == BackendVerb || args[0] == NativeChild.Verb))
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            using var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            return args[0] == NativeChild.Verb
                ? NativeChild.Main(args[1..], output, error)
                : edition.RunBackend(args[1..], output, error);
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, edition.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        Theme.ThemeManager.Initialize(app);
        var window = new MainWindow();
        if (args.Length > 0)
            window.AddPaths(args);
        return app.Run(window);
    }
}
