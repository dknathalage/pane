using System.Text.Json.Nodes;
using Pane.Core.Contracts;
using Pane.Core.Features.Files;
using Xunit;

public class FilesFeatureTests
{
    // Records how it was called and returns canned hits.
    sealed class FakeSearcher : IFileSearcher
    {
        readonly IReadOnlyList<FileHit> _hits;
        public string? LastTerms;
        public int? LastMax;
        public int Calls;
        public FakeSearcher(params FileHit[] hits) => _hits = hits;
        public bool IsAvailable { get; set; } = true;

        public IReadOnlyList<FileHit> Search(string terms, int max, CancellationToken ct)
        {
            Calls++;
            LastTerms = terms;
            LastMax = max;
            return _hits;
        }
    }

    static PaneQuery Slash(string terms) => new($"/{terms}", "/", terms);
    static PaneQuery Plain(string terms) => new(terms, null, terms);

    // A feature wired to a fake index, configured as the host would configure it.
    // Debounce defaults to 0 here so tests stay fast; individual tests override it.
    static FilesFeature Configured(IFileSearcher searcher, JsonObject? values = null)
    {
        var f = new FilesFeature(searcher);
        values ??= new JsonObject();
        values["debounceMs"] ??= 0;
        f.ApplyConfig(FeatureConfig.Resolve(f.Descriptor, f.Settings, values));
        return f;
    }

    static async Task<List<PaneResult>> Run(FilesFeature feature, PaneQuery q)
    {
        var results = new List<PaneResult>();
        await foreach (var r in feature.QueryAsync(q, CancellationToken.None))
            results.Add(r);
        return results;
    }

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public async Task Searches_a_plain_query_with_no_keyword()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = Configured(searcher);

        var results = await Run(plugin, Plain("report"));

        Assert.Single(results);
        Assert.Equal(1, searcher.Calls);
    }

    [Fact]
    public async Task Plain_query_search_can_be_switched_off()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = Configured(searcher, new JsonObject { ["searchPlainQueries"] = false });

        Assert.Empty(await Run(plugin, Plain("report")));
        Assert.Equal(0, searcher.Calls);
    }

    [Fact]
    public async Task Keyword_query_still_searches_when_plain_query_search_is_off()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = Configured(searcher, new JsonObject { ["searchPlainQueries"] = false });

        Assert.Single(await Run(plugin, Slash("report")));
    }

    [Fact]
    public void Ranks_below_other_features_by_default_so_plain_queries_are_not_flooded()
    {
        var plugin = new FilesFeature(new FakeSearcher());
        var config = FeatureConfig.Defaults(plugin.Descriptor, plugin.Settings);

        Assert.True(config.Priority < 0);
    }

    [Fact]
    public void Is_unavailable_when_the_platform_has_no_search_index()
    {
        var plugin = Configured(new FakeSearcher { IsAvailable = false });

        var availability = plugin.CheckAvailability();
        Assert.False(availability.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(availability.Reason));
    }

    [Fact]
    public void Is_available_when_the_platform_search_index_is_present()
    {
        Assert.True(Configured(new FakeSearcher()).CheckAvailability().IsAvailable);
    }

    [Fact]
    public async Task Minimum_term_length_is_configurable()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "ab.txt"), false));
        var plugin = Configured(searcher, new JsonObject { ["minTermLength"] = 5 });

        Assert.Empty(await Run(plugin, Slash("abcd")));
        Assert.Equal(0, searcher.Calls);
    }

    [Fact]
    public async Task Maximum_result_count_is_configurable()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = Configured(searcher, new JsonObject { ["maxResults"] = 7 });

        await Run(plugin, Slash("report"));

        Assert.Equal(7, searcher.LastMax);
    }

    [Fact]
    public async Task Does_not_search_for_terms_shorter_than_the_minimum()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "ab.txt"), false));
        var plugin = Configured(searcher);

        var results = await Run(plugin, Slash("ab"));

        Assert.Empty(results);
        Assert.Equal(0, searcher.Calls);
    }

    [Fact]
    public async Task Folder_hits_get_a_folder_icon_files_get_a_file_icon()
    {
        var searcher = new FakeSearcher(
            new FileHit(Path.Combine(Home, "Projects"), true),
            new FileHit(Path.Combine(Home, "notes.md"), false));
        var plugin = Configured(searcher);

        var results = await Run(plugin, Slash("proj"));

        Assert.Equal("📁", results[0].Icon);
        Assert.Equal("📄", results[1].Icon);
    }

    [Fact]
    public async Task Title_is_the_leaf_name()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "Docs", "notes.md"), false));
        var plugin = Configured(searcher);

        var results = await Run(plugin, Slash("notes"));

        Assert.Equal("notes.md", results[0].Title);
    }

    [Fact]
    public async Task Subtitle_abbreviates_the_home_directory_as_tilde()
    {
        var full = Path.Combine(Home, "Docs", "notes.md");
        var searcher = new FakeSearcher(new FileHit(full, false));
        var plugin = Configured(searcher);

        var results = await Run(plugin, Slash("notes"));

        Assert.Equal(Path.Combine("~", "Docs"), results[0].Subtitle);
    }

    [Fact]
    public async Task SearchText_is_the_full_path()
    {
        var full = Path.Combine(Home, "Docs", "notes.md");
        var searcher = new FakeSearcher(new FileHit(full, false));
        var plugin = Configured(searcher);

        var results = await Run(plugin, Slash("notes"));

        Assert.Equal(full, results[0].SearchText);
    }

    [Fact]
    public async Task Caps_results_by_passing_a_limit_to_the_searcher()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = Configured(searcher);

        await Run(plugin, Slash("report"));

        Assert.Equal(25, searcher.LastMax);   // the declared default
        Assert.Equal("report", searcher.LastTerms);
    }

    // ── Debounce: the search waits out a quiet period on the query token, so a
    //    newer keystroke (which cancels the token) never reaches the index. ──

    [Fact]
    public async Task Does_not_search_when_cancelled_during_debounce()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = Configured(searcher, new JsonObject { ["debounceMs"] = 50 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in plugin.QueryAsync(Slash("report"), cts.Token)) { }
        });

        Assert.Equal(0, searcher.Calls);
    }

    [Fact]
    public async Task Searches_after_debounce_elapses()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.txt"), false));
        var plugin = Configured(searcher, new JsonObject { ["debounceMs"] = 10 });

        var results = await Run(plugin, Slash("report"));

        Assert.Single(results);
        Assert.Equal(1, searcher.Calls);
    }

    // ── Wildcards: * and ? in the query feed the longest literal run to the
    //    index for candidates; the host glob-matcher does the precise filtering. ──

    [Fact]
    public async Task Wildcard_suffix_feeds_longest_literal_run_to_searcher()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.pdf"), false));
        var plugin = Configured(searcher);

        await Run(plugin, Slash("*.pdf"));

        Assert.Equal(".pdf", searcher.LastTerms);
    }

    [Fact]
    public async Task Wildcard_prefix_feeds_literal_prefix_to_searcher()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "report.pdf"), false));
        var plugin = Configured(searcher);

        await Run(plugin, Slash("report*"));

        Assert.Equal("report", searcher.LastTerms);
    }

    [Fact]
    public async Task Wildcard_without_a_two_char_literal_run_does_not_search()
    {
        var searcher = new FakeSearcher(new FileHit(Path.Combine(Home, "a.pdf"), false));
        var plugin = Configured(searcher);

        var results = await Run(plugin, Slash("*a*"));   // longest literal run "a" is 1 char

        Assert.Empty(results);
        Assert.Equal(0, searcher.Calls);
    }
}
