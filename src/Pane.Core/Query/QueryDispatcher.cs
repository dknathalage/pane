using Pane.Core.Contracts;
using Pane.Core.Features.Apps;
using Pane.Core.Features.Calculator;
using Pane.Core.Features.Files;
using Pane.Core.Features.Scripts;
using Pane.Core.Features.VSCode;
using Pane.Core.Settings;

namespace Pane.Core.Query;

public sealed class QueryDispatcher
{
    static readonly TimeSpan PerFeatureTimeout = TimeSpan.FromSeconds(2);
    readonly ResultRanker _ranker;
    readonly SettingsStore _settings;
    readonly (FeatureDescriptor desc,
              Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>> query)[] _features;
    HashSet<string> _disabled;

    public QueryDispatcher(
        IFuzzyMatcher matcher, SettingsStore settings,
        AppsFeature apps, FilesFeature files, CalculatorFeature calc,
        ScriptsFeature scripts, VSCodeFeature vscode)
    {
        _ranker = new ResultRanker(matcher);
        _settings = settings;
        _disabled = settings.Load().DisabledPlugins;
        _features = new (FeatureDescriptor, Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>>)[]
        {
            (apps.Descriptor,   apps.QueryAsync),
            (files.Descriptor,  files.QueryAsync),
            (calc.Descriptor,   calc.QueryAsync),
            (scripts.Descriptor, scripts.QueryAsync),
            (vscode.Descriptor, vscode.QueryAsync),
        };
    }

    public IReadOnlyList<FeatureDescriptor> Features => _features.Select(f => f.desc).ToList();

    public void ReloadSettings() => _disabled = _settings.Load().DisabledPlugins;

    public async Task<IReadOnlyList<ScoredResult>> DispatchAsync(string rawText, CancellationToken ct)
    {
        var active = _features.Where(f => !_disabled.Contains(f.desc.Id)).ToList();
        var (keyword, terms) = ParsePrefix(rawText, active);
        var targets = keyword is null
            ? active
            : active.Where(f => f.desc.Keyword == keyword).ToList();

        var query = new PaneQuery(rawText, keyword, terms);
        var perFeature = await Task.WhenAll(targets.Select(f => CollectAsync(f, query, ct)));
        return perFeature.SelectMany(x => x).OrderByDescending(s => s.Score).ToList();
    }

    async Task<IReadOnlyList<ScoredResult>> CollectAsync(
        (FeatureDescriptor desc, Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>> query) f,
        PaneQuery query, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PerFeatureTimeout);
        var raw = new List<PaneResult>();
        try
        {
            await foreach (var r in f.query(query, cts.Token).WithCancellation(cts.Token))
                raw.Add(r);
        }
        catch { return Array.Empty<ScoredResult>(); }
        return _ranker.Rank(query, f.desc, raw).ToList();
    }

    static (string? keyword, string terms) ParsePrefix(
        string rawText,
        IReadOnlyList<(FeatureDescriptor desc, Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>> query)> features)
    {
        var text = rawText.TrimStart();
        foreach (var f in features)
        {
            var kw = f.desc.Keyword;
            if (!string.IsNullOrEmpty(kw) && text.StartsWith(kw))
                return (kw, text[kw.Length..].TrimStart());
        }
        return (null, rawText.Trim());
    }
}
