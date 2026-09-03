using Pane.Core.Contracts;

namespace Pane.Core.Query;

public sealed class ResultRanker
{
    const double W_Fuzzy = 1.0;
    const double W_Plugin = 0.5;
    const double W_Base = 0.5;
    readonly IFuzzyMatcher _matcher;
    public ResultRanker(IFuzzyMatcher matcher) => _matcher = matcher;

    public IEnumerable<ScoredResult> Rank(
        PaneQuery q, FeatureDescriptor meta, int priority, IEnumerable<PaneResult> results)
    {
        var tokens = (q.Terms ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scored = new List<ScoredResult>();

        foreach (var r in results)
        {
            // Empty query: keep everything, ranked by BaseScore.
            if (tokens.Length == 0)
            {
                scored.Add(new ScoredResult(r, r.BaseScore * W_Base + priority, Array.Empty<int>(), meta.Id));
                continue;
            }

            double itemScore = 0, pluginScore = 0;
            var titleHighlights = new SortedSet<int>();
            bool allMatched = true;

            // Every token must match SOMETHING — the item (title/path) OR the
            // plugin (name/keywords). So "tray code" matches the cloudtray repo:
            // "tray" hits the title, "code" hits the VSCode plugin's keyword.
            foreach (var token in tokens)
            {
                double item = 0;
                if (_matcher.TryMatch(token, r.Title, out var tScore, out var tPos))
                {
                    item = tScore;
                    foreach (var p in tPos) titleHighlights.Add(p);
                }
                else if (_matcher.TryMatch(token, r.SearchText ?? "", out var sScore, out _))
                {
                    item = sScore;
                }

                double plugin = PluginTokenScore(token, meta);

                if (item <= 0 && plugin <= 0) { allMatched = false; break; }

                itemScore += item;
                pluginScore += plugin;
            }

            if (!allMatched) continue;

            double final = itemScore * W_Fuzzy + pluginScore * W_Plugin + r.BaseScore * W_Base + priority;
            scored.Add(new ScoredResult(r, final, titleHighlights.ToArray(), meta.Id));
        }

        return scored.OrderByDescending(s => s.Score);
    }

    // Best fuzzy score of a token against the plugin's name or any keyword.
    double PluginTokenScore(string token, FeatureDescriptor meta)
    {
        double best = 0;
        if (_matcher.TryMatch(token, meta.Name, out var n, out _)) best = Math.Max(best, n);
        foreach (var kw in meta.Keywords)
            if (_matcher.TryMatch(token, kw, out var s, out _)) best = Math.Max(best, s);
        return best;
    }
}
