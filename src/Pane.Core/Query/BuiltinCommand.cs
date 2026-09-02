using Pane.Core.Contracts;

namespace Pane.Core.Query;

/// <summary>
/// A built-in launcher action surfaced as a search result rather than coming from
/// a plugin (e.g. "Settings"). Ranks itself against a query so it can be merged
/// into the plugin results and ordered by score.
/// </summary>
public sealed record BuiltinCommand(
    string Id,
    string Title,
    string Subtitle,
    IReadOnlyList<string> Keywords)
{
    // On an empty query the command is shown but parked at the very bottom, below
    // any real result (whose empty-query score is BaseScore*0.5 + Priority).
    const double EmptyQueryScore = 0.01;

    /// <summary>
    /// Score for including this command given <paramref name="query"/>, or null to
    /// omit it. On a non-empty query it matches the title and keyword synonyms;
    /// <paramref name="titleHighlights"/> carries the matched title positions.
    /// </summary>
    public double? Score(string query, IFuzzyMatcher matcher, out IReadOnlyList<int> titleHighlights)
    {
        titleHighlights = Array.Empty<int>();

        var terms = (query ?? "").Trim();
        if (terms.Length == 0)
            return EmptyQueryScore;

        double best = 0;
        if (matcher.TryMatch(terms, Title, out var tScore, out var tPos) && tScore > best)
        {
            best = tScore;
            titleHighlights = tPos;
        }

        foreach (var kw in Keywords)
            if (matcher.TryMatch(terms, kw, out var kScore, out _) && kScore > best)
                best = kScore;

        return best > 0 ? best : null;
    }
}
