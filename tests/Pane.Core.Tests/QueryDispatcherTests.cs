using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Pane.Core.Contracts;
using Pane.Core.Query;
using Pane.Core.Settings;
using Xunit;

public class QueryDispatcherTests
{
    // A feature under the test's full control: it records what the host pushed
    // into it, so we can assert the host really is the one supplying config.
    sealed class FakeFeature : IPaneFeature
    {
        readonly string[] _titles;

        public FakeFeature(string id, string? keyword = null, int priority = 0, params string[] titles)
        {
            Descriptor = new FeatureDescriptor(id, id, "🧪", keyword, priority, new[] { id });
            _titles = titles;
        }

        public FeatureDescriptor Descriptor { get; }
        public IReadOnlyList<SettingDefinition> Settings { get; set; } =
            new SettingDefinition[] { new IntSetting("limit", "Limit", 10, 1, 100) };

        public FeatureConfig? Applied { get; private set; }
        public FeatureContext? Context { get; private set; }
        public int AvailabilityChecks { get; private set; }
        public FeatureAvailability Availability { get; set; } = FeatureAvailability.Available;
        public bool Throws { get; set; }

        public Task InitializeAsync(FeatureContext ctx, CancellationToken ct)
        {
            Context = ctx;
            return Task.CompletedTask;
        }

        public void ApplyConfig(FeatureConfig config) => Applied = config;

        public FeatureAvailability CheckAvailability()
        {
            AvailabilityChecks++;
            return Availability;
        }

        public async IAsyncEnumerable<PaneResult> QueryAsync(
            PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
        {
            if (Throws) throw new InvalidOperationException("boom");
            foreach (var t in _titles)
                yield return new PaneResult(t, "", "🧪", 0, () => Task.CompletedTask, t);
            await Task.CompletedTask;
        }
    }

    static SettingsStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json"));

    static async Task<QueryDispatcher> BuildAsync(SettingsStore store, params IPaneFeature[] features)
    {
        var d = new QueryDispatcher(new FuzzyMatcher(), store, features);
        await d.InitializeAsync(new FeatureContext(Path.GetTempPath(), Path.GetTempPath()), CancellationToken.None);
        return d;
    }

    [Fact]
    public async Task Results_from_an_enabled_feature_are_returned()
    {
        var d = await BuildAsync(NewStore(), new FakeFeature("alpha", titles: "Widget"));
        var results = await d.DispatchAsync("widget", CancellationToken.None);
        Assert.Equal("Widget", Assert.Single(results).Result.Title);
    }

    [Fact]
    public async Task Feature_disabled_in_settings_is_excluded()
    {
        var store = NewStore();
        store.Save(store.Load().WithFeature("alpha", new JsonObject { ["enabled"] = false }));

        var d = await BuildAsync(store, new FakeFeature("alpha", titles: "Widget"));
        Assert.Empty(await d.DispatchAsync("widget", CancellationToken.None));
    }

    [Fact]
    public async Task Unavailable_feature_is_excluded_even_though_it_is_enabled()
    {
        var vscode = new FakeFeature("vscode", titles: "repo")
        {
            Availability = FeatureAvailability.Unavailable("code binary not found"),
        };
        var d = await BuildAsync(NewStore(), vscode);

        Assert.Empty(await d.DispatchAsync("repo", CancellationToken.None));
        Assert.True(d.Features.Single().Config.Enabled);
    }

    [Fact]
    public async Task Availability_is_surfaced_with_its_reason_for_the_settings_ui()
    {
        var vscode = new FakeFeature("vscode", titles: "repo")
        {
            Availability = FeatureAvailability.Unavailable("code binary not found"),
        };
        var d = await BuildAsync(NewStore(), vscode);

        var view = d.Features.Single();
        Assert.False(view.Availability.IsAvailable);
        Assert.Equal("code binary not found", view.Availability.Reason);
    }

    [Fact]
    public async Task Availability_is_checked_once_per_settings_load_not_once_per_keystroke()
    {
        var f = new FakeFeature("alpha", titles: "Widget");
        var d = await BuildAsync(NewStore(), f);
        var afterInit = f.AvailabilityChecks;

        await d.DispatchAsync("w", CancellationToken.None);
        await d.DispatchAsync("wi", CancellationToken.None);
        Assert.Equal(afterInit, f.AvailabilityChecks);

        d.ReloadSettings();
        Assert.Equal(afterInit + 1, f.AvailabilityChecks);
    }

    [Fact]
    public async Task Host_pushes_the_resolved_config_into_the_feature()
    {
        var store = NewStore();
        store.Save(store.Load().WithFeature("alpha", new JsonObject { ["limit"] = 3 }));

        var f = new FakeFeature("alpha", titles: "Widget");
        await BuildAsync(store, f);

        Assert.Equal(3, f.Applied!.GetInt("limit"));
    }

    [Fact]
    public async Task Reloading_settings_pushes_the_new_config_into_the_feature()
    {
        var store = NewStore();
        var f = new FakeFeature("alpha", titles: "Widget");
        var d = await BuildAsync(store, f);
        Assert.Equal(10, f.Applied!.GetInt("limit"));

        store.Save(store.Load().WithFeature("alpha", new JsonObject { ["limit"] = 42 }));
        d.ReloadSettings();

        Assert.Equal(42, f.Applied!.GetInt("limit"));
    }

    [Fact]
    public async Task Host_supplies_the_feature_context_at_initialization()
    {
        var f = new FakeFeature("alpha", titles: "Widget");
        var d = new QueryDispatcher(new FuzzyMatcher(), NewStore(), new[] { f });
        await d.InitializeAsync(new FeatureContext("/data", "/home"), CancellationToken.None);

        Assert.Equal("/data", f.Context!.DataDirectory);
        Assert.Equal("/home", f.Context!.HomeDirectory);
    }

    [Fact]
    public async Task Keyword_prefix_scopes_the_query_to_that_feature()
    {
        var d = await BuildAsync(NewStore(),
            new FakeFeature("alpha", keyword: "=", titles: "Alpha"),
            new FakeFeature("beta", titles: "Beta"));

        var results = await d.DispatchAsync("=alpha", CancellationToken.None);
        Assert.All(results, r => Assert.Equal("alpha", r.PluginId));
    }

    [Fact]
    public async Task Feature_without_its_prefix_still_answers_a_plain_query()
    {
        var d = await BuildAsync(NewStore(), new FakeFeature("alpha", keyword: "=", titles: "Alpha"));
        var results = await d.DispatchAsync("alpha", CancellationToken.None);
        Assert.Single(results);
    }

    [Fact]
    public async Task Keyword_can_be_overridden_in_settings()
    {
        var store = NewStore();
        store.Save(store.Load().WithFeature("alpha", new JsonObject { ["keyword"] = "!" }));

        var d = await BuildAsync(store,
            new FakeFeature("alpha", keyword: "=", titles: "Alpha"),
            new FakeFeature("beta", titles: "Beta"));

        var results = await d.DispatchAsync("!alpha", CancellationToken.None);
        Assert.All(results, r => Assert.Equal("alpha", r.PluginId));
    }

    [Fact]
    public async Task Cleared_keyword_no_longer_scopes_the_query()
    {
        var store = NewStore();
        store.Save(store.Load().WithFeature("alpha", new JsonObject { ["keyword"] = "" }));

        var d = await BuildAsync(store, new FakeFeature("alpha", keyword: "=", titles: "Alpha"));

        // With the keyword live, "=" would be stripped and "alpha" would match.
        // Cleared, "=alpha" is the literal search text and matches nothing.
        Assert.Empty(await d.DispatchAsync("=alpha", CancellationToken.None));
    }

    [Fact]
    public async Task Priority_from_settings_reorders_results()
    {
        var store = NewStore();
        store.Save(store.Load().WithFeature("beta", new JsonObject { ["priority"] = 50 }));

        var d = await BuildAsync(store,
            new FakeFeature("alpha", titles: "Match"),
            new FakeFeature("beta", titles: "Match"));

        var results = await d.DispatchAsync("match", CancellationToken.None);
        Assert.Equal("beta", results.First().PluginId);
    }

    [Fact]
    public async Task A_throwing_feature_does_not_take_down_the_others()
    {
        var d = await BuildAsync(NewStore(),
            new FakeFeature("bad", titles: "Match") { Throws = true },
            new FakeFeature("good", titles: "Match"));

        var results = await d.DispatchAsync("match", CancellationToken.None);
        Assert.Equal("good", Assert.Single(results).PluginId);
    }

    [Fact]
    public async Task Features_view_exposes_the_schema_the_settings_ui_renders()
    {
        var d = await BuildAsync(NewStore(), new FakeFeature("alpha", titles: "Widget"));
        Assert.Contains(d.Features.Single().Settings, s => s.Key == "limit");
    }
}
