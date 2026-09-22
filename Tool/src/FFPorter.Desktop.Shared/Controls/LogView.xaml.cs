using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FFPorter.Desktop.Controls;

public partial class LogView : UserControl
{
    private const int KeepCharacters = 400_000;

    private readonly StringBuilder _pending = new();
    private readonly DispatcherTimer _flush;
    private StreamWriter? _file;
    private string? _lastFile;

    public LogView()
    {
        InitializeComponent();
        _flush = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _flush.Tick += (_, _) => Flush();
    }

    public string SaveName { get; set; } = "converter.log";

    public void Append(string line)
    {
        _file?.WriteLine(line);
        _pending.Append(line).Append('\n');
        if (!_flush.IsEnabled)
            _flush.Start();
    }

    public void StartFile(string path)
    {
        StopFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _file = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        _lastFile = path;
        Where.Text = path;
        Where.ToolTip = path;
    }

    public void StopFile()
    {
        _file?.Dispose();
        _file = null;
    }

    public void Clear()
    {
        _pending.Clear();
        Text.Clear();
    }

    private void Flush()
    {
        _flush.Stop();
        if (_pending.Length == 0)
            return;
        bool atEnd = Text.VerticalOffset + Text.ViewportHeight >= Text.ExtentHeight - 24;
        if (Text.Text.Length + _pending.Length > KeepCharacters)
        {
            string all = Text.Text + _pending;
            Text.Text = all[^(KeepCharacters / 2)..];
        }
        else
        {
            Text.AppendText(_pending.ToString());
        }
        _pending.Clear();
        if (atEnd)
            Text.ScrollToEnd();
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        Flush();
        try
        {
            Clipboard.SetText(Text.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        Flush();
        var dialog = new SaveFileDialog { Filter = "Log|*.log", FileName = SaveName };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            if (_lastFile != null && File.Exists(_lastFile) && _file == null)
            {
                if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(_lastFile), StringComparison.OrdinalIgnoreCase))
                    File.Copy(_lastFile, dialog.FileName, true);
            }
            else
            {
                File.WriteAllText(dialog.FileName, Text.Text);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this)!, error.Message, "Save log", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearClick(object sender, RoutedEventArgs e) => Clear();
}
