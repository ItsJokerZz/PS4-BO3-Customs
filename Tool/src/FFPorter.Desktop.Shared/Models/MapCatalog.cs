namespace FFPorter.Desktop.Models;

internal sealed class MapCatalog
{
    public string? Name { get; set; }
    public List<MapCatalogEntry> Maps { get; set; } = [];
}

internal sealed class MapCatalogEntry
{
    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public long Size { get; set; }
    public string? Folder { get; set; }
    public string? File { get; set; }
    public string? Url { get; set; }
    public List<string>? Parts { get; set; }

    public string? Thumbnail { get; set; }
    public string? Sha256 { get; set; }
}
