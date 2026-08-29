using System.IO.Compression;

namespace Pane.Core.Plugins;

public sealed class PluginFetcher
{
    readonly HttpClient _http;
    public PluginFetcher(HttpClient http) => _http = http;

    public async Task<string> DownloadAndExtractAsync(string url, CancellationToken ct = default)
    {
        byte[] bytes;
        try
        {
            var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is not PluginFetchException)
        {
            throw new PluginFetchException($"Failed to download plugin from {url}", ex);
        }
        return ExtractToTempDir(bytes);
    }

    public static string ExtractToTempDir(byte[] zipBytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pane-fetch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            if (archive.Entries.Count == 0)
                throw new PluginFetchException("Downloaded archive is empty");
            archive.ExtractToDirectory(dir, overwriteFiles: true);
            return dir;
        }
        catch (InvalidDataException ex)
        {
            TryDelete(dir);
            throw new PluginFetchException("Downloaded file is not a valid zip archive", ex);
        }
        catch
        {
            TryDelete(dir);
            throw;
        }
    }

    static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best-effort */ }
    }
}
