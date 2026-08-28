using Pane.Abstractions;
using Pane.Core.Query;
using Xunit;

public class ResultRankerTests
{
    readonly ResultRanker _ranker = new(new FuzzyMatcher());
    static PluginMetadata Meta(string kw = "") =>
        new("p", "P", "🔌", "1", "d", string.IsNullOrEmpty(kw) ? Array.Empty<string>() : new[] { kw });

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
}
