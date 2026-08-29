using System.Net;
using Pane.Core.Marketplace;
using Pane.Core.Plugins;
using Xunit;

public class MarketplaceServiceTests
{
    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(_fn(r));
    }

    const string Catalog = """
    { "name": "Pane Official", "plugins": [
      { "id": "com.pane.apps", "name": "Apps", "version": "2.0.0",
        "source": { "type": "url", "url": "https://x/Apps.zip" } } ] }
    """;

    static (MarketplaceService svc, string cache) NewService(
        Func<HttpRequestMessage, HttpResponseMessage> handler, string dataRoot)
    {
        var http = new HttpClient(new StubHandler(handler));
        var config = new MarketplaceConfigStore(
            Path.Combine(dataRoot, "marketplaces.json"), "Pane Official",
            "https://example.com/marketplace.json");   // direct-json default for the test
        var installed = new InstalledStore(Path.Combine(dataRoot, "installed.json"));
        var plugins = new PluginManager(dataRoot);     // empty — nothing installed
        var cache = Path.Combine(dataRoot, "marketplace-cache");
        return (new MarketplaceService(http, config, installed, plugins, cache), cache);
    }

    static string NewRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), $"mksvc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(r);
        return r;
    }

    [Fact]
    public async Task Aggregates_catalog_and_marks_available_when_not_installed()
    {
        var root = NewRoot();
        var (svc, _) = NewService(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalog) }, root);

        var entries = await svc.GetCatalogAsync();

        var e = Assert.Single(entries);
        Assert.Equal("com.pane.apps", e.Plugin.Id);
        Assert.Equal(MarketplaceItemState.Available, e.State);
    }

    [Fact]
    public async Task Falls_back_to_cache_when_source_unreachable()
    {
        var root = NewRoot();
        // First call succeeds and caches.
        var (svc1, _) = NewService(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalog) }, root);
        await svc1.GetCatalogAsync();

        // Second service over the same dataRoot, network now failing.
        var (svc2, _) = NewService(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError), root);
        var entries = await svc2.GetCatalogAsync();

        Assert.Single(entries);   // served from cache, not empty
    }

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
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            foreach (var f in Directory.GetFiles(dir))
            {
                var e = zip.CreateEntry(Path.GetFileName(f));
                using var es = e.Open();
                using var fs = File.OpenRead(f);
                fs.CopyTo(es);
            }
        return ms.ToArray();
    }

    // Helpers shared by the version-state tests below.
    static (MarketplaceService svc, PluginManager plugins) NewServiceWithPlugin(
        string root, string catalogVersion)
    {
        var zip = ZipDir(FixtureDir("TestPlugin"));   // id "test", version "1.0"
        var http = new HttpClient(new StubHandler(req =>
        {
            // Catalog request returns the catalog JSON; zip request returns the plugin.
            if (req.RequestUri!.AbsolutePath.EndsWith(".zip"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
            var catalog = $$"""
            { "name": "Pane Official", "plugins": [
              { "id": "test", "name": "Test", "version": "{{catalogVersion}}",
                "source": { "type": "url", "url": "https://x/test.zip" } } ] }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalog) };
        }));

        var config = new MarketplaceConfigStore(
            Path.Combine(root, "marketplaces.json"), "Pane Official",
            "https://example.com/marketplace.json");
        var installed = new InstalledStore(Path.Combine(root, "installed.json"));
        var fetcher = new PluginFetcher(http);
        var plugins = new PluginManager(root, store: null, fetcher: fetcher);
        var svc = new MarketplaceService(http, config, installed, plugins, Path.Combine(root, "cache"));
        return (svc, plugins);
    }

    [Fact]
    public async Task Installed_at_same_version_as_catalog_shows_Installed_not_UpdateAvailable()
    {
        // This guards FIX 1: when the installed version string equals the catalog version string,
        // the marketplace entry must be Installed, not UpdateAvailable.
        var root = NewRoot();
        var (svc, plugins) = NewServiceWithPlugin(root, "1.0");   // catalog: "1.0", fixture: "1.0"

        // First, install the test plugin via the service so PluginManager knows about it.
        var installEntry = new MarketplaceEntry(
            new MarketplacePlugin("test", "Test", null, "🧪", "1.0", null, null, null,
                new PluginSource("url", "https://x/test.zip", null)),
            "Pane Official", MarketplaceItemState.Available, null);
        await svc.InstallAsync(installEntry);

        // Now fetch the catalog and assert state is Installed.
        var entries = await svc.GetCatalogAsync();
        var e = Assert.Single(entries);
        Assert.Equal("test", e.Plugin.Id);
        Assert.Equal(MarketplaceItemState.Installed, e.State);
    }

    [Fact]
    public async Task Installed_at_lower_version_than_catalog_shows_UpdateAvailable()
    {
        // Guards the other direction: catalog "2.0" > installed "1.0" must be UpdateAvailable.
        var root = NewRoot();
        var (svc, plugins) = NewServiceWithPlugin(root, "2.0");   // catalog: "2.0", fixture: "1.0"

        var installEntry = new MarketplaceEntry(
            new MarketplacePlugin("test", "Test", null, "🧪", "1.0", null, null, null,
                new PluginSource("url", "https://x/test.zip", null)),
            "Pane Official", MarketplaceItemState.Available, null);
        await svc.InstallAsync(installEntry);

        var entries = await svc.GetCatalogAsync();
        var e = Assert.Single(entries);
        Assert.Equal("test", e.Plugin.Id);
        Assert.Equal(MarketplaceItemState.UpdateAvailable, e.State);
    }

    [Fact]
    public async Task Install_installs_plugin_and_records_provenance()
    {
        var root = NewRoot();
        var zip = ZipDir(FixtureDir("TestPlugin"));      // id "test", version "1.0"
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var config = new MarketplaceConfigStore(
            Path.Combine(root, "marketplaces.json"), "Pane Official", "https://example.com/marketplace.json");
        var installed = new InstalledStore(Path.Combine(root, "installed.json"));
        var fetcher = new PluginFetcher(http);
        var plugins = new PluginManager(root, store: null, fetcher: fetcher);
        var svc = new MarketplaceService(http, config, installed, plugins, Path.Combine(root, "cache"));

        var entry = new MarketplaceEntry(
            new MarketplacePlugin("test", "Test", null, "🧪", "1.0", null, null, null,
                new PluginSource("url", "https://x/test.zip", null)),
            "Pane Official", MarketplaceItemState.Available, null);

        await svc.InstallAsync(entry);

        Assert.Contains(plugins.List(), e => e.Metadata.Id == "test" && e.State == PluginState.Enabled);
        var prov = installed.Get("test");
        Assert.NotNull(prov);
        Assert.Equal("Pane Official", prov!.Marketplace);
        Assert.Equal("https://x/test.zip", prov.SourceUrl);
        Assert.Equal("1.0", prov.Version);
    }
}
