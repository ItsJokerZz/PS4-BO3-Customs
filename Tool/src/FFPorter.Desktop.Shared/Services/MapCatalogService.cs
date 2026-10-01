using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;


public sealed class MapCatalogService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ObservableCollection<CommunityMap> Maps { get; } = [];

    public async Task<int> RefreshAsync(string catalogUrl, Action<string> log, CancellationToken token)
    {
        if (!Uri.TryCreate(catalogUrl.Trim(), UriKind.Absolute, out Uri? catalog) || catalog.Scheme is not ("http" or "https"))
            throw new IOException($"\"{catalogUrl}\" is not a web address. It should point at the catalog.json on your map storage.");

        log($"Loading the map list from {catalog}…");
        using var request = new HttpRequestMessage(HttpMethod.Get, catalog);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
        using HttpResponseMessage response = await Http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(token);
        MapCatalog? parsed = JsonSerializer.Deserialize<MapCatalog>(json, Json);
        if (parsed?.Maps == null)
            throw new IOException("the catalog is not in the expected format");

        Maps.Clear();
        foreach (MapCatalogEntry? entry in parsed.Maps)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Title))
                continue;

            List<string> sources = entry.Parts is { Count: > 0 }
                ? entry.Parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToList()
                : entry.File is { Length: > 0 } file ? [file]
                : entry.Url is { Length: > 0 } url ? [url]
                : [];
            if (sources.Count == 0)
            {
                log($"Skipped {entry.Title}: the catalog has no file to download.");
                continue;
            }

            Maps.Add(new CommunityMap
            {
                Id = entry.Name!.Trim(),
                Title = entry.Title!.Trim(),
                Author = entry.Author?.Trim() ?? "",
                Description = entry.Description?.Trim() ?? "",
                Version = entry.Version?.Trim() ?? "",
                Size = entry.Size,
                Folder = entry.Folder?.Trim() ?? "",
                Downloads = sources.Select(source => Resolve(catalog, source)).ToList(),
                Thumbnail = string.IsNullOrWhiteSpace(entry.Thumbnail) ? null : Resolve(catalog, entry.Thumbnail!),
                Sha256 = entry.Sha256?.Trim().ToLowerInvariant() ?? "",
            });
        }
        log($"{Maps.Count} map{(Maps.Count == 1 ? "" : "s")} available.");
        return Maps.Count;
    }

    public async Task LoadThumbnailAsync(CommunityMap map, CancellationToken token)
    {
        if (map.Thumbnail == null)
            return;
        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(map.Thumbnail, token);
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            map.ThumbnailImage = image;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or NotSupportedException or OperationCanceledException)
        {
            
        }
    }

    private static Uri Resolve(Uri catalog, string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? absolute) ? absolute : new Uri(catalog, value);
}
