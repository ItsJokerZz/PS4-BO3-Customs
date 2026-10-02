using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

namespace FFPorter.Desktop.Controls;

public partial class BrowsePanel : UserControl
{
    private readonly MapCatalogService _store = new();
    private readonly ICollectionView _view;

    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _installCts;
    private CommunityMap? _current;
    private bool _loaded;
    private bool _active;
    private bool _refreshing;
    private string _emptyMessage = "Loading the map list…";

    public BrowsePanel()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_store.Maps);
        _view.Filter = Matches;
        MapList.ItemsSource = _view;
        UpdateTarget();
        ShowEmpty("Loading the map list…");
        // Activate() can be called from the main window's constructor, before WPF installs its
        // SynchronizationContext. Deferring the first load until Loaded keeps the await continuations
        // on the UI thread.
        Loaded += (_, _) => StartInitialLoad();
    }

    /// <summary>Where progress and problems are written; the main window wires this to its activity log.</summary>
    public Action<string>? Log { get; set; }

    private static AppSettings Settings => AppSettings.Current;

    /// <summary>Called when the browse section is shown; loads the catalog the first time.</summary>
    public void Activate()
    {
        _active = true;
        UpdateTarget();
        StartInitialLoad();
    }

    private void StartInitialLoad()
    {
        if (_active && IsLoaded && !_loaded && !_refreshing)
            _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_current != null || _refreshing)
            return;
        string url = Settings.CatalogUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowEmpty("No map list is set yet.\n\nOpen Console settings and paste the URL of the catalog.json on your map storage.");
            return;
        }

        _refreshing = true;
        RefreshButton.IsEnabled = false;
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        _emptyMessage = "Loading the map list…";
        ShowEmpty(_emptyMessage);
        try
        {
            int count = await _store.RefreshAsync(url, Write, _refreshCts.Token);
            _loaded = true;
            _emptyMessage = count == 0
                ? "No maps are published yet. Check back later."
                : "No maps match your search.";
            UpdateEmpty();
            if (count > 0)
            {
                StatusText.Text = $"{count} map{(count == 1 ? "" : "s")} available.";
                _ = LoadThumbnailsAsync(_refreshCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            _emptyMessage = $"Could not load the map list.\n\n{error.Message}";
            ShowEmpty(_emptyMessage);
            Write($"Could not load the map list: {error.Message}");
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task LoadThumbnailsAsync(CancellationToken token)
    {
        foreach (CommunityMap map in _store.Maps.ToArray())
        {
            if (token.IsCancellationRequested)
                return;
            await _store.LoadThumbnailAsync(map, token);
        }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateEmpty();
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void ConsoleClick(object sender, RoutedEventArgs e)
    {
        if (FtpSettingsDialog.Show(Window.GetWindow(this), Settings))
        {
            UpdateTarget();
            Write($"Console set to {Settings.FtpTarget}.");
        }
        if (!_loaded)
            _ = RefreshAsync();
    }

    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        if (_current != null || (sender as FrameworkElement)?.DataContext is not CommunityMap map)
            return;
        if (!EnsureConsole())
            return;

        _current = map;
        foreach (CommunityMap other in _store.Maps)
            other.CanInstall = false;
        map.State = RunStates.Running;
        map.Progress = 0;
        map.StageText = "Starting";
        map.StatusText = "Starting";
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();

        var progress = new Progress<InstallProgress>(report =>
        {
            map.Progress = report.Fraction;
            map.StageText = report.Stage;
            map.StatusText = report.Detail.Length > 0 ? report.Detail : report.Stage;
            StatusText.Text = $"{map.Title} · {report.Stage}{(report.Detail.Length > 0 ? " · " + report.Detail : "")}";
        });

        try
        {
            var installer = new MapInstaller();
            await Task.Run(() => installer.InstallAsync(map, Settings, progress, Write, _installCts.Token));
            map.State = RunStates.Done;
            map.Progress = 1;
            map.StageText = "";
            map.StatusText = "Installed";
            StatusText.Text = $"{map.Title} installed to usermaps/{map.Destination}.";
        }
        catch (Exception error)
        {
            if (_installCts.IsCancellationRequested)
            {
                map.State = RunStates.Cancelled;
                map.StatusText = "Cancelled";
                map.StageText = "";
                StatusText.Text = $"{map.Title} was cancelled.";
            }
            else
            {
                map.State = RunStates.Failed;
                map.StatusText = "Failed";
                map.StageText = "";
                StatusText.Text = $"{map.Title} could not be installed.";
                Write($"{map.Title} failed: {error.Message}");
                MessageDialog.Inform(Window.GetWindow(this), DialogKind.Error, $"Could not install {map.Title}", error.Message);
            }
        }
        finally
        {
            map.CanInstall = true;
            _current = null;
            _installCts?.Dispose();
            _installCts = null;
            UpdateEmpty();
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        StatusText.Text = $"Cancelling {_current.Title}…";
        _installCts?.Cancel();
    }

    private bool EnsureConsole()
    {
        if (Settings.HasConsole)
            return true;
        StatusText.Text = "Set up your console first.";
        if (FtpSettingsDialog.Show(Window.GetWindow(this), Settings))
        {
            UpdateTarget();
            return Settings.HasConsole;
        }
        return false;
    }

    private bool Matches(object item)
    {
        if (item is not CommunityMap map)
            return false;
        string query = SearchBox.Text.Trim();
        return query.Length == 0
            || map.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || map.Author.Contains(query, StringComparison.OrdinalIgnoreCase)
            || map.Id.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateTarget() => TargetText.Text = Settings.HasConsole ? Settings.FtpTarget : "console not set — open Console settings";

    private void UpdateEmpty()
    {
        CountText.Text = _view.Cast<object>().Count().ToString();
        if (_current != null || _view.Cast<object>().Any())
            Empty.Visibility = Visibility.Collapsed;
        else
            ShowEmpty(_store.Maps.Count == 0 ? _emptyMessage : "No maps match your search.");
    }

    private void ShowEmpty(string message)
    {
        EmptyText.Text = message;
        Empty.Visibility = Visibility.Visible;
    }

    private void Write(string line) => Log?.Invoke(line);
}
