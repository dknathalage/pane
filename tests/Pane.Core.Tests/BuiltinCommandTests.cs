using System.Collections.Generic;
using Pane.Abstractions;
using Pane.Core.Query;
using Xunit;

public class BuiltinCommandTests
{
    readonly FuzzyMatcher _matcher = new();
    static readonly BuiltinCommand Settings =
        new("settings", "Settings", "Manage plugins & marketplaces",
            new[] { "settings", "preferences", "config" });

    [Fact]
    public void Empty_query_includes_command_ranked_low()
    {
        var score = Settings.Score("", _matcher, out var hi);
        Assert.NotNull(score);
        Assert.True(score < 1.0);          // sits at the bottom, below real results
        Assert.Empty(hi);
    }

    [Fact]
    public void Whitespace_query_is_treated_as_empty()
    {
        Assert.NotNull(Settings.Score("   ", _matcher, out _));
    }

    [Fact]
    public void Matching_query_ranks_high_and_returns_title_highlights()
    {
        var score = Settings.Score("sett", _matcher, out var hi);
        Assert.NotNull(score);
        Assert.True(score > 1.0);
        Assert.NotEmpty(hi);               // highlights the matched chars in "Settings"
    }

    [Fact]
    public void Matches_on_a_keyword_synonym()
    {
        Assert.NotNull(Settings.Score("pref", _matcher, out _));
    }

    [Fact]
    public void Unrelated_query_omits_the_command()
    {
        Assert.Null(Settings.Score("zzzxq", _matcher, out _));
    }
}
