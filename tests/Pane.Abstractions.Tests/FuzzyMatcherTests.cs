using Pane.Abstractions;
using Xunit;

public class FuzzyMatcherTests
{
    readonly IFuzzyMatcher _m = new FuzzyMatcher();

    [Fact]
    public void Non_subsequence_does_not_match()
    {
        Assert.False(_m.TryMatch("xyz", "hello world", out _, out _));
    }

    [Fact]
    public void Subsequence_matches_and_reports_positions()
    {
        Assert.True(_m.TryMatch("hlo", "hello", out var score, out var pos));
        Assert.True(score > 0);
        Assert.Equal(new[] { 0, 2, 4 }, pos); // h e l l o -> h(0) l(2) o(4)
    }

    [Fact]
    public void Prefix_beats_scattered_subsequence()
    {
        _m.TryMatch("cal", "calculator", out var prefix, out _);
        _m.TryMatch("cal", "critical alarm", out var scattered, out _);
        Assert.True(prefix > scattered);
    }

    [Fact]
    public void Consecutive_beats_gapped()
    {
        _m.TryMatch("ap", "apple", out var consecutive, out _);
        _m.TryMatch("ap", "a-b-c-p", out var gapped, out _);
        Assert.True(consecutive > gapped);
    }

    [Fact]
    public void Word_boundary_match_scores_higher()
    {
        _m.TryMatch("os", "open settings", out var boundary, out _);   // o(open) s(settings)
        _m.TryMatch("os", "chooser", out var inside, out _);           // o..s inside one word
        Assert.True(boundary > inside);
    }

    [Fact]
    public void Empty_query_matches_with_zero_score()
    {
        Assert.True(_m.TryMatch("", "anything", out var score, out var pos));
        Assert.Equal(0, score);
        Assert.Empty(pos);
    }

    [Fact]
    public void Match_is_case_insensitive()
    {
        Assert.True(_m.TryMatch("HW", "hello world", out _, out var pos));
        Assert.Equal(new[] { 0, 6 }, pos);
    }
}
