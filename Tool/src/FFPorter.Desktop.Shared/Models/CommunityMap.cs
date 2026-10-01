using System.Windows.Media;

namespace FFPorter.Desktop.Models;

public sealed class CommunityMap : Observable
{
    private string _state = RunStates.Ready, _statusText = "Ready", _stageText = "";
    private double _progress;
    private bool _canInstall = true;
    private ImageSource? _thumbnailImage;

    public required string Id { get; init; }

    public required string Title { get; init; }
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "";
    public long Size { get; init; }

    public string Folder { get; init; } = "";

    public required IReadOnlyList<Uri> Downloads { get; init; }

    public Uri? Thumbnail { get; init; }

    public int PartCount => Downloads.Count;

    public string Sha256 { get; init; } = "";

    public string Destination => Folder.Length > 0 ? Folder : Id;

    public string SizeText => Job.SizeText(Size);

    public string VersionLabel => "v" + Version;
    public bool HasVersion => Version.Length > 0;

    public bool HasDescription => Description.Length > 0;

    public string ByLine
    {
        get
        {
            string by = Author.Length > 0 ? $"by {Author}" : "unknown author";
            string parts = PartCount > 1 ? $"  ·  {PartCount} parts" : "";
            return $"{by}  ·  {SizeText}{parts}  ·  usermaps/{Destination}";
        }
    }

    public string State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(IsRunning));
                Raise(nameof(IsIdle));
                Raise(nameof(ShowStatus));
            }
        }
    }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string StageText { get => _stageText; set => Set(ref _stageText, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public bool CanInstall { get => _canInstall; set => Set(ref _canInstall, value); }

    public bool IsRunning => State == RunStates.Running;
    public bool IsIdle => !IsRunning;
    public bool ShowStatus => State != RunStates.Ready;

    public ImageSource? ThumbnailImage { get => _thumbnailImage; set => Set(ref _thumbnailImage, value); }
}
