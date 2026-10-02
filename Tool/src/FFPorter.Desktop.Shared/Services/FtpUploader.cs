#pragma warning disable SYSLIB0014 // FtpWebRequest is obsolete but is the only FTP client in the framework and is enough here.
using System.Buffers;
using System.IO;
using System.Net;

namespace FFPorter.Desktop.Services;

public sealed record FtpProgress(long Done, long Total, string FileName);

public sealed class FtpUploader
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _remotePath;
    private readonly NetworkCredential _credentials;

    public FtpUploader(string host, int port, string user, string password, string remotePath)
    {
        _host = host.Trim();
        _port = port is > 0 and <= 65535 ? port : 21;
        _remotePath = "/" + remotePath.Replace('\\', '/').Trim('/');
        _credentials = string.IsNullOrWhiteSpace(user)
            ? new NetworkCredential("anonymous", "anonymous")
            : new NetworkCredential(user, password);
    }
    public async Task TestAsync(CancellationToken token)
    {
        await EnsurePathAsync(_remotePath, token);
        string probe = _remotePath + "/.porter-write-test";
        await UploadBytesAsync(probe, "porter"u8.ToArray(), token);
        await DeleteFileAsync(probe, token);
    }

    private async Task UploadBytesAsync(string remote, byte[] data, CancellationToken token)
    {
        try
        {
            FtpWebRequest request = Create(remote, WebRequestMethods.Ftp.UploadFile);
            request.ContentLength = data.Length;
            using var registration = token.Register(() => Abort(request));
            Stream target = await request.GetRequestStreamAsync();
            try
            {
                await target.WriteAsync(data, token);
            }
            finally
            {
                target.Dispose();
            }
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
            _ = response.StatusDescription;
        }
        catch (WebException error)
        {
            throw new IOException($"{Describe(error)} (uploading {remote})", error);
        }
    }

    private async Task DeleteFileAsync(string remote, CancellationToken token)
    {
        try
        {
            FtpWebRequest request = Create(remote, WebRequestMethods.Ftp.DeleteFile);
            using var registration = token.Register(() => Abort(request));
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
            _ = response.StatusDescription;
        }
        catch (WebException)
        {
            
        }
    }

    public async Task UploadAsync(string localRoot, IProgress<FtpProgress>? progress, CancellationToken token)
    {
        string root = Path.GetFullPath(localRoot);
        List<string> files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
        long total = files.Sum(file => new FileInfo(file).Length);

        await EnsurePathAsync(_remotePath, token);
        foreach (string directory in files
            .Select(file => Path.GetRelativePath(root, Path.GetDirectoryName(file)!).Replace('\\', '/'))
            .Where(relative => relative.Length > 0 && relative != ".")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(relative => relative.Count(c => c == '/')).ThenBy(relative => relative, StringComparer.OrdinalIgnoreCase))
        {
            await EnsurePathAsync(_remotePath + "/" + directory, token);
        }

        long done = 0;
        foreach (string file in files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            long length = new FileInfo(file).Length;
            await UploadFileAsync(file, _remotePath + "/" + relative, length, token, chunk =>
            {
                done += chunk;
                progress?.Report(new FtpProgress(done, total, relative));
            });
        }
    }

    private async Task UploadFileAsync(string local, string remote, long length, CancellationToken token, Action<int> onChunk)
    {
        try
        {
            FtpWebRequest request = Create(remote, WebRequestMethods.Ftp.UploadFile);
            request.ContentLength = length;
            using var registration = token.Register(() => Abort(request));
            Stream target = await request.GetRequestStreamAsync();
            try
            {
                await using var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
                try
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(0, 1 << 20), token)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), token);
                        onChunk(read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            finally
            {
                target.Dispose();
            }
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
            _ = response.StatusDescription;
        }
        catch (WebException error)
        {
            throw new IOException($"{Describe(error)} (uploading {remote})", error);
        }
    }

    private async Task EnsurePathAsync(string absolute, CancellationToken token)
    {
        string built = "";
        foreach (string part in absolute.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            built += "/" + part;
            await MakeDirectoryAsync(built, token);
        }
    }

    private async Task MakeDirectoryAsync(string absolute, CancellationToken token)
    {
        FtpWebRequest request = Create(absolute, WebRequestMethods.Ftp.MakeDirectory);
        using var registration = token.Register(() => Abort(request));
        try
        {
            using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
            _ = response.StatusDescription;
        }
        catch (WebException error) when (error.Response is FtpWebResponse { StatusCode: FtpStatusCode.ActionNotTakenFileUnavailable or FtpStatusCode.CommandNotImplemented })
        {
        
        }
        catch (WebException error)
        {
            throw new IOException($"{Describe(error)} (creating {absolute})", error);
        }
    }

    private static string Describe(WebException error) =>
        error.Response is FtpWebResponse response
            ? $"the console answered {(int)response.StatusCode} {response.StatusDescription?.Trim()}"
            : error.Message;

    private FtpWebRequest Create(string path, string method)
    {
        var request = (FtpWebRequest)WebRequest.Create(new Uri($"ftp://{_host}:{_port}{Encode(path)}"));
        request.Method = method;
        request.Credentials = _credentials;
        request.UsePassive = true;
        request.UseBinary = true;
        request.KeepAlive = false;
        request.Timeout = 30_000;
        request.ReadWriteTimeout = 60_000;
        return request;
    }

    private static string Encode(string path) =>
        "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static void Abort(FtpWebRequest request)
    {
        try
        {
            request.Abort();
        }
        catch (Exception)
        {
        }
    }
}
