using Pane.Abstractions;
using Pane.Core.Query;
using Xunit;

public class ResultRankerFeatureDescriptorTests
{
    static readonly FeatureDescriptor Calc =
        new("calc", "Calculator", "🧮", "=", 10, new[] { "calc", "math", "=" });

    [Fact]
    public void EmptyQuery_KeepsResult_ScoredByBaseAndPriority()
    {
        var ranker = new ResultRanker(new FuzzyMatcher());
        var r = new PaneResult("= 4", "Copy", "🧮", 100, () => Task.CompletedTask, "2+2");
        var q = new PaneQuery("=2+2", "=", "2+2");

        var scored = ranker.Rank(q, Calc, new[] { r }).ToList();

        Assert.Single(scored);
        Assert.Equal("calc", scored[0].PluginId);
    }
}

public class ResultRankerTests
{
    readonly ResultRanker _ranker = new(new FuzzyMatcher());
    static FeatureDescriptor Meta(string kw = "") =>
        new("p", "P", "🔌", null, 0, string.IsNullOrEmpty(kw) ? Array.Empty<string>() : new[] { kw });

    static PaneResult R(string title, double baseScore = 0, string? search = null) =>
        new(title, "", "", baseScore, () => Task.CompletedTask, search);

    [Fact]
    public void Drops_results_that_do_not_match_query()
    {
        var q = new PaneQuery("zzz", null, "zzz");
        var ranked = _ranker.Rank(q, Meta(), new[] { R("hello") }).ToList();
        Assert.Empty(ranked);
    }

    [Fact]
    public void Ranks_better_fuzzy_match_higher()
    {
        var q = new PaneQuery("cal", null, "cal");
        var ranked = _ranker.Rank(q, Meta(), new[] { R("critical alarm"), R("calculator") }).ToList();
        Assert.Equal("calculator", ranked[0].Result.Title);
    }

    [Fact]
    public void Empty_query_keeps_all_and_orders_by_base_score()
    {
        var q = new PaneQuery("", null, "");
        var ranked = _ranker.Rank(q, Meta(), new[] { R("a", 1), R("b", 5) }).ToList();
        Assert.Equal("b", ranked[0].Result.Title);
        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Matches_against_search_text_when_title_fails()
    {
        var q = new PaneQuery("repo", null, "repo");
        var ranked = _ranker.Rank(q, Meta(), new[] { R("money", search: "~/repos/money") }).ToList();
        Assert.Single(ranked);
    }

    [Fact]
    public void Token_matching_plugin_keyword_lets_other_token_match_the_item()
    {
        // "tray" hits the title; "code" hits the plugin's keyword → still matches.
        var meta = new FeatureDescriptor("vscode", "VSCode Repos", "📂", null, 0,
            new[] { "code", "repo", "vscode" });
        var q = new PaneQuery("tray code", null, "tray code");
        var ranked = _ranker.Rank(q, meta, new[] { R("cloudtray", search: "/Users/x/repos/cloudtray") }).ToList();
        Assert.Single(ranked);
    }

    [Fact]
    public void Drops_when_a_token_matches_neither_item_nor_plugin()
    {
        var q = new PaneQuery("tray zzz", null, "tray zzz");
        var ranked = _ranker.Rank(q, Meta(), new[] { R("cloudtray") }).ToList();
        Assert.Empty(ranked);
    }

    [Fact]
    public void Wildcard_query_keeps_only_glob_matching_titles()
    {
        // End-to-end of "/*.pdf": the ranker keeps names ending in .pdf and drops
        // the rest, via the matcher's glob support.
        var q = new PaneQuery("/*.pdf", "/", "*.pdf");
        var ranked = _ranker.Rank(q, Meta(),
            new[] { R("report.pdf"), R("notes.txt"), R("budget.pdf") }).ToList();
        Assert.Equal(new[] { "budget.pdf", "report.pdf" }, ranked.Select(r => r.Result.Title).OrderBy(t => t));
    }
}
