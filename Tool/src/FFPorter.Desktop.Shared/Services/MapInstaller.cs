using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;

public sealed record InstallProgress(string Stage, string Detail, double Fraction);


public sealed record PreparedMap(string Root, string Source);


public sealed class MapInstaller
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(60) };

    public async Task InstallAsync(CommunityMap map, AppSettings settings, IProgress<InstallProgress> progress, Action<string> log, CancellationToken token)
    {
        PreparedMap prepared = await PrepareAsync(map, progress, log, token);
        try
        {
            string remote = settings.FtpRemotePath + "/" + map.Destination;
            progress.Report(new InstallProgress("Sending to console", settings.FtpTarget, 0.65));
            log($"Uploading {map.Title} to {remote}…");
            var uploader = new FtpUploader(settings.FtpHost, settings.FtpPort, settings.FtpUser, settings.FtpPassword, remote);
            await uploader.UploadAsync(prepared.Source, new Progress<FtpProgress>(report =>
            {
                double fraction = report.Total > 0 ? (double)report.Done / report.Total : 0;
                progress.Report(new InstallProgress("Sending to console", Path.GetFileName(report.FileName), 0.65 + fraction * 0.35));
            }), token);
            log($"Installed {map.Title} to usermaps/{map.Destination}.");
        }
        finally
        {
            TryDelete(prepared.Root);
        }
    }

    public async Task<PreparedMap> PrepareAsync(CommunityMap map, IProgress<InstallProgress> progress, Action<string> log, CancellationToken token)
    {
        string root = Path.Combine(Path.GetTempPath(), "PS4-FF-Porter", Safe(map.Id));
        TryDelete(root);
        Directory.CreateDirectory(root);
        try
        {
            string zip = Path.Combine(root, "download.zip");
            progress.Report(new InstallProgress("Downloading", map.SizeText, 0));
            log(map.PartCount > 1 ? $"Downloading {map.Title} ({map.PartCount} parts)…" : $"Downloading {map.Title}…");
            await DownloadAllAsync(map.Downloads, map.Size, zip, (done, total) =>
            {
                double fraction = total > 0 ? (double)done / total : 0;
                string detail = total > 0 ? $"{Job.SizeText(done)} of {Job.SizeText(total)}" : Job.SizeText(done);
                progress.Report(new InstallProgress("Downloading", detail, Math.Min(fraction, 1) * 0.6));
            }, token);

            if (map.Sha256.Length > 0)
            {
                progress.Report(new InstallProgress("Verifying", "", 0.6));
                string actual = await HashAsync(zip, token);
                if (!string.Equals(actual, map.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("the download did not match the published checksum");
            }

            progress.Report(new InstallProgress("Unpacking", "", 0.62));
            string extract = Path.Combine(root, "files");
            await Task.Run(() => Extract(zip, extract), token);
            return new PreparedMap(root, SourceRoot(extract));
        }
        catch
        {
            TryDelete(root);
            throw;
        }
    }

    private static async Task DownloadAllAsync(IReadOnlyList<Uri> sources, long expectedTotal, string destination, Action<long, long> report, CancellationToken token)
    {
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            long done = 0;
            long total = expectedTotal > 0 ? expectedTotal : -1;
            for (int index = 0; index < sources.Count; index++)
            {
                using HttpResponseMessage response = await Http.GetAsync(sources[index], HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using Stream source = await response.Content.ReadAsStreamAsync(token);
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, 1 << 20), token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    report(done, total);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<string> HashAsync(string file, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Extract(string zip, string destination)
    {
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zip);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue; 
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string SourceRoot(string extract)
    {
        string[] entries = Directory.GetFileSystemEntries(extract);
        return entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : extract;
    }

    private static string Safe(string name)
    {
        foreach (char bad in Path.GetInvalidFileNameChars())
            name = name.Replace(bad, '_');
        return name.Length == 0 ? "map" : name;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
