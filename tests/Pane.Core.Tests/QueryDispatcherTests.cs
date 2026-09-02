using System.Runtime.CompilerServices;
using Pane.Abstractions;
using Pane.Core.Features.Apps;
using Pane.Core.Features.Calculator;
using Pane.Core.Features.Files;
using Pane.Core.Features.Scripts;
using Pane.Core.Features.VSCode;
using Pane.Core.Query;
using Pane.Core.Settings;
using Xunit;

public class QueryDispatcherTests
{
    sealed class EmptySearcher : IFileSearcher
    {
        public IReadOnlyList<FileHit> Search(string terms, int max, CancellationToken ct)
            => Array.Empty<FileHit>();
    }

    static QueryDispatcher Build() =>
        new QueryDispatcher(
            new FuzzyMatcher(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")),
            new AppsFeature(Path.GetTempPath()),
            new FilesFeature(new EmptySearcher()),
            new CalculatorFeature(),
            new ScriptsFeature(),
            new VSCodeFeature());

    [Fact]
    public async Task Calculator_keyword_prefix_scopes_to_calc()
    {
        var d = Build();
        var results = await d.DispatchAsync("=2+2", CancellationToken.None);
        // Only calculator should respond to "=" prefix and return a result
        Assert.True(results.Count >= 1);
        // All results should come from the calc feature
        Assert.All(results, r => Assert.Equal("calc", r.PluginId));
    }

    [Fact]
    public async Task Empty_query_returns_results_without_throwing()
    {
        var d = Build();
        var results = await d.DispatchAsync("", CancellationToken.None);
        // Should not throw; results may be empty or populated
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Cancellation_is_respected()
    {
        var d = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Should not throw — each feature catches exceptions
        var results = await d.DispatchAsync("hello", cts.Token);
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Features_property_returns_five_features()
    {
        var d = Build();
        Assert.Equal(5, d.Features.Count);
    }

    [Fact]
    public async Task Disabled_feature_is_excluded()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var settings = store.Load();
        settings.DisabledPlugins.Add("calc");
        store.Save(settings);

        var d = new QueryDispatcher(
            new FuzzyMatcher(), store,
            new AppsFeature(Path.GetTempPath()),
            new FilesFeature(new EmptySearcher()),
            new CalculatorFeature(),
            new ScriptsFeature(),
            new VSCodeFeature());

        var results = await d.DispatchAsync("=2+2", CancellationToken.None);
        // calc is disabled, so the "=" prefix finds no active feature — results should be empty
        Assert.Empty(results);
    }
}
