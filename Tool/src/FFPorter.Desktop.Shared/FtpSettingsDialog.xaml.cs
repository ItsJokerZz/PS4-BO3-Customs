using System.IO;
using System.Windows;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;
using FFPorter.Desktop.Theme;

namespace FFPorter.Desktop;

public partial class FtpSettingsDialog : Window
{
    private FtpSettingsDialog(Window? owner, AppSettings settings)
    {
        InitializeComponent();
        ThemeManager.StyleTitleBar(this);
        Title = Edition.Current.Title;
        if (owner != null && new System.Windows.Interop.WindowInteropHelper(owner).Handle != IntPtr.Zero)
            Owner = owner;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

        HostBox.Text = settings.FtpHost;
        PortBox.Text = settings.FtpPort.ToString();
        RemoteBox.Text = settings.FtpRemotePath;
    }

    public static bool Show(Window? owner, AppSettings settings) => new FtpSettingsDialog(owner, settings).ShowDialog() == true;

    private async void TestClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out string host, out int port, out string remote))
            return;
        if (host.Length == 0)
        {
            SetTest("Enter the console's address first.", error: true);
            return;
        }
        TestButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        SetTest($"Connecting to {host}:{port}…", error: false);
        try
        {
            var uploader = new FtpUploader(host, port, "anonymous", "", remote);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await uploader.TestAsync(timeout.Token);
            SetTest($"Connected. {remote} is reachable and writable.", error: false);
            MessageDialog.Inform(this, DialogKind.Info, "Console test passed",
                $"{remote}\n\nReachable and writable, so Install will work.");
        }
        catch (Exception error) when (error is IOException or System.Net.WebException or OperationCanceledException)
        {
            SetTest($"Could not connect: {error.Message}", error: true);
            MessageDialog.Inform(this, DialogKind.Error, "Console test failed", error.Message);
        }
        finally
        {
            TestButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out string host, out int port, out string remote))
            return;
        AppSettings.Current.SetConsole(host, port, "anonymous", "", remote);
        DialogResult = true;
    }

    private bool TryRead(out string host, out int port, out string remote)
    {
        host = HostBox.Text.Trim();
        remote = RemoteBox.Text.Trim();
        if (!int.TryParse(PortBox.Text.Trim(), out port) || port is <= 0 or > 65535)
        {
            SetTest("The port must be a number between 1 and 65535.", error: true);
            return false;
        }
        return true;
    }

    private void SetTest(string message, bool error)
    {
        TestText.Text = message;
        TestText.SetResourceReference(ForegroundProperty, error ? "Status.Error" : "Text.Muted");
    }
}
