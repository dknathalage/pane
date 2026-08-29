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
}
