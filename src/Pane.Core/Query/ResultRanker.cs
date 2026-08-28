using Pane.Abstractions;

namespace Pane.Core.Query;

public sealed class ResultRanker
{
    const double W_Fuzzy = 1.0;
    const double W_Base = 0.5;
    readonly IFuzzyMatcher _matcher;
    public ResultRanker(IFuzzyMatcher matcher) => _matcher = matcher;

    public IEnumerable<ScoredResult> Rank(PaneQuery q, PluginMetadata meta, IEnumerable<PaneResult> results)
    {
        var routingBias = RoutingBias(q, meta);
        var scored = new List<ScoredResult>();
        foreach (var r in results)
        {
            bool titleMatch = _matcher.TryMatch(q.Terms, r.Title, out var fuzzy, out var pos);
            IReadOnlyList<int> highlights = titleMatch ? pos : Array.Empty<int>();

            if (!titleMatch && !string.IsNullOrEmpty(q.Terms))
            {
                var alt = r.SearchText ?? "";
                if (!_matcher.TryMatch(q.Terms, alt, out fuzzy, out _)) continue;   // drop non-matches
                highlights = Array.Empty<int>();
            }

            double final = fuzzy * W_Fuzzy + r.BaseScore * W_Base + routingBias + meta.Priority;
            scored.Add(new ScoredResult(r, final, highlights, meta.Id));
        }
        return scored.OrderByDescending(s => s.Score);
    }

    double RoutingBias(PaneQuery q, PluginMetadata meta)
    {
        if (string.IsNullOrEmpty(q.Terms)) return 0;
        double best = 0;
        foreach (var token in meta.Keywords.Append(meta.Name))
            if (_matcher.TryMatch(q.Terms, token, out var s, out _))
                best = Math.Max(best, s);
        return best * 0.5;
    }
}
