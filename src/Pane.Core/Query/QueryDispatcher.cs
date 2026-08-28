using Pane.Abstractions;

namespace Pane.Core.Query;

public sealed class QueryDispatcher
{
    static readonly TimeSpan PerPluginTimeout = TimeSpan.FromSeconds(2);
    readonly ResultRanker _ranker;
    readonly IFuzzyMatcher _matcher;

    public QueryDispatcher(IFuzzyMatcher matcher)
    {
        _matcher = matcher;
        _ranker = new ResultRanker(matcher);
    }

    public async Task<IReadOnlyList<ScoredResult>> DispatchAsync(
        string rawText,
        IReadOnlyList<(PluginMetadata meta, IPlugin plugin)> plugins,
        CancellationToken ct)
    {
        var (keyword, terms) = ParsePrefix(rawText, plugins);
        var targets = keyword is null
            ? plugins
            : plugins.Where(p => p.meta.Keyword == keyword).ToList();

        var query = new PaneQuery(rawText, keyword, terms);
        var tasks = targets.Select(p => CollectAsync(p, query, ct));
        var perPlugin = await Task.WhenAll(tasks);
        return perPlugin.SelectMany(x => x)
                        .OrderByDescending(s => s.Score)
                        .ToList();
    }

    async Task<IReadOnlyList<ScoredResult>> CollectAsync(
        (PluginMetadata meta, IPlugin plugin) p, PaneQuery query, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PerPluginTimeout);
        var raw = new List<PaneResult>();
        try
        {
            await foreach (var r in p.plugin.QueryAsync(query, cts.Token).WithCancellation(cts.Token))
                raw.Add(r);
        }
        catch
        {
            // Silent isolation: a throwing/timing-out plugin contributes nothing.
            return Array.Empty<ScoredResult>();
        }
        return _ranker.Rank(query, p.meta, raw).ToList();
    }

    static (string? keyword, string terms) ParsePrefix(
        string rawText, IReadOnlyList<(PluginMetadata meta, IPlugin plugin)> plugins)
    {
        var text = rawText.TrimStart();
        foreach (var p in plugins)
        {
            var kw = p.meta.Keyword;
            if (!string.IsNullOrEmpty(kw) && text.StartsWith(kw))
                return (kw, text[kw.Length..].TrimStart());
        }
        return (null, rawText.Trim());
    }
}
