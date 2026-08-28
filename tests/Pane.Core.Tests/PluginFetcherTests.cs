using System.IO.Compression;
using System.Net;
using Pane.Core.Plugins;
using Xunit;

public class PluginFetcherTests
{
    static byte[] MakeZip(params (string name, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var s = e.Open();
                using var w = new StreamWriter(s);
                w.Write(content);
            }
        return ms.ToArray();
    }

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fn(request));
    }

    [Fact]
    public void ExtractToTempDir_writes_entries_to_disk()
    {
        var dir = PluginFetcher.ExtractToTempDir(MakeZip(("a.txt", "hello")));
        try { Assert.Equal("hello", File.ReadAllText(Path.Combine(dir, "a.txt"))); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExtractToTempDir_throws_typed_on_garbage_bytes()
    {
        Assert.Throws<PluginFetchException>(() =>
            PluginFetcher.ExtractToTempDir(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void ExtractToTempDir_throws_typed_on_empty_archive()
    {
        Assert.Throws<PluginFetchException>(() =>
            PluginFetcher.ExtractToTempDir(MakeZip()));
    }

    [Fact]
    public async Task DownloadAndExtractAsync_fetches_then_extracts()
    {
        var bytes = MakeZip(("plugin.txt", "ok"));
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var fetcher = new PluginFetcher(http);

        var dir = await fetcher.DownloadAndExtractAsync("https://example/plugin.zip");
        try { Assert.Equal("ok", File.ReadAllText(Path.Combine(dir, "plugin.txt"))); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DownloadAndExtractAsync_wraps_network_errors()
    {
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var fetcher = new PluginFetcher(http);

        await Assert.ThrowsAsync<PluginFetchException>(() =>
            fetcher.DownloadAndExtractAsync("https://example/missing.zip"));
    }
}
