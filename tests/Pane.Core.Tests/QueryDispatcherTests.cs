using System.Runtime.CompilerServices;
using Pane.Abstractions;
using Pane.Core.Query;
using Xunit;

public class QueryDispatcherTests
{
    sealed class FakePlugin : IPlugin
    {
        readonly string[] _titles;
        readonly bool _throws;
        public FakePlugin(PluginMetadata meta, bool throws, params string[] titles)
        { Metadata = meta; _throws = throws; _titles = titles; }
        public PluginMetadata Metadata { get; }
        public Task InitializeAsync(IPluginContext ctx) => Task.CompletedTask;
        public async IAsyncEnumerable<PaneResult> QueryAsync(PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
        {
            if (_throws) throw new InvalidOperationException("kaboom");
            foreach (var t in _titles) { yield return new PaneResult(t, "", "", 1, () => Task.CompletedTask); }
            await Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    static PluginMetadata Meta(string id, string? keyword = null) =>
        new(id, id, "🔌", "1", "d", Array.Empty<string>(), keyword);

    [Fact]
    public async Task Aggregates_results_from_all_plugins()
    {
        var d = new QueryDispatcher(new FuzzyMatcher());
        var plugins = new (PluginMetadata, IPlugin)[]
        {
            (Meta("a"), new FakePlugin(Meta("a"), false, "apple")),
            (Meta("b"), new FakePlugin(Meta("b"), false, "apricot")),
        };
        var res = await d.DispatchAsync("ap", plugins, CancellationToken.None);
        Assert.Equal(2, res.Count);
    }

    [Fact]
    public async Task Throwing_plugin_is_isolated()
    {
        var d = new QueryDispatcher(new FuzzyMatcher());
        var plugins = new (PluginMetadata, IPlugin)[]
        {
            (Meta("a"), new FakePlugin(Meta("a"), true)),               // throws
            (Meta("b"), new FakePlugin(Meta("b"), false, "apple")),
        };
        var res = await d.DispatchAsync("ap", plugins, CancellationToken.None);
        Assert.Single(res);
        Assert.Equal("apple", res[0].Result.Title);
    }

    [Fact]
    public async Task Keyword_prefix_scopes_to_one_plugin()
    {
        var d = new QueryDispatcher(new FuzzyMatcher());
        var plugins = new (PluginMetadata, IPlugin)[]
        {
            (Meta("scripts", ">"), new FakePlugin(Meta("scripts", ">"), false, "build")),
            (Meta("apps"), new FakePlugin(Meta("apps"), false, "browser")),
        };
        var res = await d.DispatchAsync("> bu", plugins, CancellationToken.None);
        Assert.Single(res);
        Assert.Equal("build", res[0].Result.Title);
    }
}
