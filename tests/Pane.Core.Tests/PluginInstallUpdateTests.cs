using System.IO.Compression;
using System.Net;
using Pane.Core.Plugins;
using Xunit;

public class PluginInstallUpdateTests
{
    static string FixtureDir(string name)
    {
        var config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name, "bin", config, "net10.0"));
    }

    static byte[] ZipDir(string dir)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var f in Directory.GetFiles(dir))
            {
                var e = zip.CreateEntry(Path.GetFileName(f));
                using var es = e.Open();
                using var fs = File.OpenRead(f);
                fs.CopyTo(es);
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

    static PluginManager MgrServing(string dataRoot, byte[] zipBytes)
    {
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) }));
        return new PluginManager(dataRoot, store: null, fetcher: new PluginFetcher(http));
    }

    static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pane-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task InstallFromUrl_installs_and_loads_the_plugin()
    {
        var root = NewRoot();
        var zip = ZipDir(FixtureDir("TestPlugin"));
        var mgr = MgrServing(root, zip);

        var entry = await mgr.InstallFromUrlAsync("https://example/testplugin.zip");

        Assert.Equal("test", entry.Metadata.Id);
        Assert.Equal(PluginState.Enabled, entry.State);
        Assert.Single(mgr.Active());
        // Copied under plugins/<id>/
        Assert.True(File.Exists(Path.Combine(root, "plugins", "test", "TestPlugin.dll")));
    }
}
