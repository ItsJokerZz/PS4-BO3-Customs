using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using FFPorter.Core;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;
using FFPorter.Desktop.Theme;
using Microsoft.Win32;

namespace FFPorter.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Job> _jobs = [];
    private readonly BackendProcess _backend = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _running, _stop;
    private Job? _current, _shown;
    private int _index, _count;
    private DateTime _started;
    private string _stage = "", _detail = "";

    private static AppSettings Settings => AppSettings.Current;
    private static Edition Edition => Edition.Current;

    public MainWindow()
    {
        InitializeComponent();
        Title = TitleText.Text = Edition.Title;
        SubtitleText.Text = Edition.Subtitle;
        EmptyQueueText.Text = $"Maps, mods, weapons and zones from {Edition.GameName}: .ff files or whole folders.";
        ThemeManager.StyleTitleBar(this);
        JobList.ItemsSource = _jobs;
        _jobs.CollectionChanged += (_, _) => RefreshQueue();
        ShowOutput();
        ShowGameFolder();
        Loaded += (_, _) => { AskForGameFolder(); PrepareTools(); };
        DragOver += (_, e) =>
        {
            e.Effects = !_running && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                AddPaths(paths);
            e.Handled = true;
        };
        Closing += WindowClosing;
        _clock.Tick += (_, _) => ShowProgress();
        RefreshQueue();
    }


    public void AddPaths(IEnumerable<string> paths)
    {
        if (_running)
            return;
        List<Job> found = JobScanner.Scan(paths, Log.Append);
        Job? last = null;
        foreach (Job job in found)
        {
            if (_jobs.Any(j => string.Equals(j.MainFile, job.MainFile, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (_jobs.Any(j => string.Equals(j.Name, job.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Append($"Skipped {job.MainFile}: another {job.Name} is already queued, and both would convert into the same folder.");
                continue;
            }
            LoadPreviousReport(job);
            _jobs.Add(job);
            last = job;
        }
        if (last != null)
            JobList.SelectedItem = last;
        else if (found.Count == 0)
            SetStatus("Nothing to convert there", $"only {Edition.GameName} PC fastfiles convert; the log says what was skipped");
    }

    private static void LoadPreviousReport(Job job)
    {
        string report = job.Name + ".fidelity.json";
        if ((FidelityProtocol.Load(Path.Combine(FFPorter.Core.Workspace.ReportDirectory, report))
            ?? FidelityProtocol.Load(Path.Combine(Settings.OutputFor(job), report))
            ?? FidelityProtocol.Load(Path.Combine(Settings.Output, job.Name, report))) is not { } snapshot)
            return;
        job.Report = FidelityView.From(snapshot);
        job.StatusText = snapshot.State == FidelityStates.Done ? "Converted earlier" : "Last run failed";
    }

    private void RefreshQueue()
    {
        CountText.Text = _jobs.Count.ToString();
        EmptyQueue.Visibility = _jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        JobList.Visibility = _jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConvertButton.IsEnabled = !_running && _jobs.Count > 0;
        RemoveButton.IsEnabled = ClearButton.IsEnabled = !_running && _jobs.Count > 0;
        if (!_running)
        {
            Progress.Value = 0;
            SetStatus(_jobs.Count == 0 ? "Drop something to convert" : $"{_jobs.Count} ready to convert", "");
        }
        ShowPanel();
    }

    private void AddFilesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Fastfiles|*.ff", Multiselect = true, Title = $"Add {Edition.GameName} fastfiles" };
        if (dialog.ShowDialog(this) == true)
            AddPaths(dialog.FileNames);
    }

    private void AddFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder (a usermap, a mod, a zone folder, or a folder of them)", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            AddPaths(dialog.FolderNames);
    }

    private void RemoveClick(object sender, RoutedEventArgs e) => RemoveSelected();

    private void RemoveSelected()
    {
        if (_running)
            return;
        foreach (Job job in JobList.SelectedItems.Cast<Job>().ToArray())
            _jobs.Remove(job);
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        if (!_running)
            _jobs.Clear();
    }

    private void JobListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
    }

    private void JobSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPanel();


    private void ShowPanel()
    {
        Job? job = JobList.SelectedItems.Count == 1 ? JobList.SelectedItem as Job : null;
        job ??= _running ? _current : null;
        if (!ReferenceEquals(job, _shown))
        {
            if (_shown != null)
                _shown.PropertyChanged -= ShownJobChanged;
            if (job != null)
                job.PropertyChanged += ShownJobChanged;
            _shown = job;
        }
        BindingOperations.ClearBinding(Fidelity, DataContextProperty);
        if (job?.Report != null)
        {
            Fidelity.SetBinding(DataContextProperty, new Binding(nameof(Job.Report)) { Source = job });
        }
        else
        {
            Fidelity.DataContext = null;
            Fidelity.EmptyMessage = job == null
                ? "Convert something to see how faithfully each part of it carries over to PS4. The scores fill in live while it converts."
                : $"{job.Name} has not been converted yet. Its port fidelity fills in live while it converts.";
        }
    }

    private void ShownJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Job.Report))
            ShowPanel();
    }


    private static List<string> Command(Job job, string workspace)
    {
        List<string> command = [job.Codename, "convert", job.MainFile, "-o", Settings.OutputFor(job), "--force", "--progress", "--root", workspace];
        if (Settings.GameFolder is { Length: > 0 } game && Directory.Exists(game))
            command.AddRange([Edition.GameFolderOption, game]);
        return command;
    }

    private async void ConvertClick(object sender, RoutedEventArgs e) => await ConvertAll();

    private async Task ConvertAll()
    {
        if (_running || _jobs.Count == 0)
            return;
        string workspace = Settings.Workspace;
        try
        {
            Directory.CreateDirectory(Settings.Output);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus("Cannot use the output folder", error.Message);
            return;
        }

        Job[] queue = [.. _jobs];
        _stop = false;
        _count = queue.Length;
        SetBusy(true);
        Log.Clear();
        foreach (Job job in queue)
        {
            job.State = RunStates.Queued;
            job.StatusText = "Queued";
            job.Progress = 0;
        }
        int passed = 0, failed = 0;
        _started = DateTime.Now;
        _clock.Start();
        try
        {
            try
            {
                Log.StartFile(Path.Combine(Workspace.Locate(workspace).Join("analysis/gui_logs"), $"convert-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Log.Append($"The log is not saved to disk: {error.Message}");
            }
            for (_index = 0; _index < queue.Length && !_stop; _index++)
            {
                Job job = queue[_index];
                _current = job;
                job.State = RunStates.Running;
                job.StatusText = "Converting";
                job.StageText = "Starting";
                job.Report = new FidelityView();
                JobList.SelectedItem = job;
                JobList.ScrollIntoView(job);
                ShowPanel();
                _stage = $"Converting {job.Name}";
                _detail = "starting";
                ShowProgress();
                Log.Append($"\n[{DateTime.Now:T}] {job.GameName} {job.Kind.ToLowerInvariant()} {job.Name}: {job.MainFile}");

                var errors = new List<string>();
                int code;
                try
                {
                    code = await _backend.RunAsync(Command(job, workspace), workspace, line => OnOutput(job, line), line =>
                    {
                        errors.Add(line);
                        Log.Append(line);
                    });
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
                {
                    code = -1;
                    errors.Add(error.Message);
                    Log.Append(error.Message);
                }
                if (Finish(job, code, errors))
                    passed++;
                else if (!_stop)
                    failed++;
            }
            foreach (Job job in queue.Where(j => j.State == RunStates.Queued))
            {
                job.State = RunStates.Ready;
                job.StatusText = "Not run";
            }
            string summary = $"{passed} converted" + (failed > 0 ? $" · {failed} failed" : "");
            SetStatus(_stop ? "Cancelled" : failed > 0 ? "Finished with problems" : "Finished", $"{summary} · {Elapsed(DateTime.Now - _started)}");
            Log.Append(_stop ? $"Cancelled · {summary}" : summary);
            if (!_stop)
                Progress.Value = 1;
            PercentText.Text = "";
        }
        finally
        {
            Log.StopFile();
            _clock.Stop();
            _current = null;
            SetBusy(false);
            ShowPanel();
        }
    }

    private void OnOutput(Job job, string line)
    {
        if (FidelityProtocol.TryParse(line, out FidelitySnapshot? snapshot) && snapshot != null)
        {
            job.Report ??= new FidelityView();
            job.Report.Update(snapshot);
            job.Progress = snapshot.Progress;
            job.StageText = snapshot.Stage;
            _stage = snapshot.Stage;
            _detail = snapshot.Detail;
            ShowProgress();
            return;
        }
        Log.Append(line);
    }

    private bool Finish(Job job, int code, List<string> errors)
    {
        if (_stop)
        {
            job.State = RunStates.Cancelled;
            job.StatusText = "Cancelled";
            if (job.Report is { IsRunning: true } report)
                report.Interrupt(FidelityStates.Cancelled, "Cancelled before the conversion finished. The output folder may hold a partial conversion.");
            return false;
        }
        if (code == 0)
        {
            job.State = RunStates.Done;
            job.StatusText = "Converted";
            job.Progress = 1;
            return true;
        }
        job.State = RunStates.Failed;
        IEnumerable<string> reasons = errors.Where(e => !string.IsNullOrWhiteSpace(e)).TakeLast(3);
        job.StatusText = errors.Any(e => e.Contains("needs parts this converter cannot port yet", StringComparison.Ordinal)) ? "Not supported yet" : $"Failed ({code})";
        LogToggle.IsChecked = true;
        if (job.Report == null || job.Report.IsRunning)
        {
            job.Report ??= new FidelityView();
            job.Report.Interrupt(FidelityStates.Failed, "The converter stopped before it finished. Its last lines are below; the log has the rest.", reasons);
        }
        return false;
    }

    private void ShowProgress()
    {
        if (!_running)
            return;
        double itemProgress = _current?.Progress ?? 0;
        double overall = _count == 0 ? 0 : Math.Clamp((_index + itemProgress) / _count, 0, 1);
        string position = _count > 1 ? $"{_index + 1} of {_count} · " : "";
        SetStatus(_stage.Length > 0 ? _stage : "Converting", $"{position}{_current?.Name}{(_detail.Length > 0 ? " · " + _detail : "")}");
        Progress.Value = overall;
        PercentText.Text = $"{overall:P0} · {Elapsed(DateTime.Now - _started)}";
    }

    private void SetStatus(string status, string detail)
    {
        StatusText.Text = status;
        DetailText.Text = detail.Length > 0 ? "   " + detail : "";
        if (!_running)
            PercentText.Text = "";
        Progress.Visibility = _running || Progress.Value > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetBusy(bool busy)
    {
        _running = busy;
        AddFilesButton.IsEnabled = AddFolderButton.IsEnabled = OutputButton.IsEnabled = GameFolderButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = ClearButton.IsEnabled = !busy && _jobs.Count > 0;
        ConvertButton.IsEnabled = !busy && _jobs.Count > 0;
        ConvertButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => StopRun();

    private void StopRun()
    {
        if (!_running)
            return;
        _stop = true;
        _backend.Cancel();
        SetStatus("Cancelling", "waiting for the converter to stop");
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            StopRun();
        }
    }


    private void ShowGameFolder()
    {
        string? folder = Settings.GameFolder;
        GameFolderLabel.Text = Edition.GameFolderName;
        GameFolderText.Text = folder == null ? "not set" : ShortPath(folder);
        GameFolderButton.ToolTip = folder == null
            ? $"Set the {Edition.GameFolderName}. {Edition.GameFolderHint}"
            : $"{folder}\n\nClick to choose another folder.";
    }

    private void AskForGameFolder()
    {
        if (!Settings.FirstRun || Settings.GameFolder != null)
            return;
        MessageBoxResult answer = MessageBox.Show(this,
            $"{Edition.Title} needs your {Edition.GameName} files to convert with.\n\n{Edition.GameFolderHint}\n\nChoose that folder now?",
            Edition.Title, MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes)
            ChooseGameFolder();
        else
            SetStatus($"{Edition.GameFolderName} not set", "the Game files button in the header sets it");
    }

    private void PrepareTools() =>
        Task.Run(() => Edition.PrepareTools(line => Dispatcher.Invoke(() => Log.Append(line))));

    private void ChooseGameFolderClick(object sender, RoutedEventArgs e) => ChooseGameFolder();

    private void ChooseGameFolder()
    {
        if (_running)
            return;
        while (true)
        {
            var dialog = new OpenFolderDialog { Title = $"Choose the {Edition.GameFolderName}" };
            if (Settings.GameFolder is { } current && Directory.Exists(current))
                dialog.InitialDirectory = current;
            if (dialog.ShowDialog(this) != true)
                return;
            if (Edition.TryGameFolder(dialog.FolderName, out string? folder) && folder != null)
            {
                Settings.SetGameFolder(folder);
                ShowGameFolder();
                SetStatus($"{Edition.GameFolderName} set", folder);
                return;
            }
            if (MessageBox.Show(this, $"{dialog.FolderName}\n\nis not the {Edition.GameFolderName}. {Edition.GameFolderHint}\n\nTry again?",
                Edition.Title, MessageBoxButton.RetryCancel, MessageBoxImage.Warning) != MessageBoxResult.Retry)
            {
                return;
            }
        }
    }

    private void ShowOutput()
    {
        OutputLabel.Text = Settings.OutputOverride == null ? "Output (default)" : "Output";
        OutputText.Text = ShortPath(Settings.Output);
        OutputButton.ToolTip = $"Converted files go to {Path.Combine(Settings.Output, Edition.Codename, "<name>")}. "
            + (Settings.OutputOverride == null ? "Click to pick another folder." : $"Click to pick another folder; right-click to go back to {AppSettings.DefaultOutput}.");
        DefaultOutputItem.IsEnabled = Settings.OutputOverride != null;
        DefaultOutputItem.Header = $"Use the default folder ({AppSettings.DefaultOutput})";
    }

    private static string ShortPath(string path)
    {
        string[] parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return path.Length <= 48 || parts.Length <= 3 ? path : $"…{Path.DirectorySeparatorChar}{string.Join(Path.DirectorySeparatorChar, parts[^2..])}";
    }

    private void ChooseOutputClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = $"Where converted files go (a {Edition.Codename} folder is made inside)" };
        if (Directory.Exists(Settings.Output))
            dialog.InitialDirectory = Settings.Output;
        if (dialog.ShowDialog(this) != true)
            return;
        Settings.SetOutput(dialog.FolderName);
        ShowOutput();
        ReloadEarlierReports();
    }

    private void ReloadEarlierReports()
    {
        foreach (Job job in _jobs.Where(j => j.State == RunStates.Ready))
        {
            job.Report = null;
            job.StatusText = "Ready";
            LoadPreviousReport(job);
        }
        ShowPanel();
    }

    private void DefaultOutputClick(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;
        Settings.SetOutput(null);
        ShowOutput();
        ReloadEarlierReports();
    }

    private void OpenOutputClick(object sender, RoutedEventArgs e)
    {
        string folder = Settings.Output;
        if (JobList.SelectedItem is Job job)
        {
            if (Directory.Exists(Settings.OutputFor(job)))
                folder = Settings.OutputFor(job);
            else if (Directory.Exists(Path.Combine(folder, job.Codename)))
                folder = Path.Combine(folder, job.Codename);
        }
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        else if (!_running)
            SetStatus("Nothing converted yet", $"{folder} is created by the first conversion");
    }

    private void LogToggled(object sender, RoutedEventArgs e) => Log.Visibility = LogToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private static string Elapsed(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds:00}s" : $"{time.Seconds}s";
}
