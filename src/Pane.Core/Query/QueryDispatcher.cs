using Pane.Core.Contracts;
using Pane.Core.Settings;

namespace Pane.Core.Query;

public sealed class QueryDispatcher
{
    static readonly TimeSpan PerFeatureTimeout = TimeSpan.FromSeconds(2);

    readonly ResultRanker _ranker;
    readonly SettingsStore _settings;
    readonly IReadOnlyList<IPaneFeature> _features;
    IReadOnlyList<FeatureView> _views = Array.Empty<FeatureView>();

    public QueryDispatcher(IFuzzyMatcher matcher, SettingsStore settings, IEnumerable<IPaneFeature> features)
    {
        _ranker = new ResultRanker(matcher);
        _settings = settings;
        _features = features.ToList();
    }

    public IReadOnlyList<FeatureView> Features
    {
        get
        {
            EnsureLoaded();
            return _views;
        }
    }

    // Config is resolved lazily so a host that calls InitializeAsync first does a
    // single pass — features only know their context (home, data dir) after init,
    // and re-resolving before that would index against the wrong paths.
    void EnsureLoaded()
    {
        if (_views.Count != _features.Count) ReloadSettings();
    }

    public async Task InitializeAsync(FeatureContext ctx, CancellationToken ct)
    {
        foreach (var f in _features)
        {
            try { await f.InitializeAsync(ctx, ct); }
            catch (Exception ex) { Console.Error.WriteLine($"pane: {f.Descriptor.Id} failed to initialize: {ex.Message}"); }
        }
        ReloadSettings();
    }

    /// <summary>
    /// Re-resolves every feature's config from disk, pushes it in, and re-checks
    /// availability. Called at startup and whenever settings are saved — never per
    /// keystroke, so an availability probe may touch the filesystem.
    /// </summary>
    public void ReloadSettings()
    {
        var settings = _settings.Load();
        var views = new List<FeatureView>(_features.Count);

        foreach (var f in _features)
        {
            var config = FeatureConfig.Resolve(f.Descriptor, f.Settings, settings.FeatureValues(f.Descriptor.Id));
            f.ApplyConfig(config);

            FeatureAvailability availability;
            try { availability = f.CheckAvailability(); }
            catch (Exception ex) { availability = FeatureAvailability.Unavailable(ex.Message); }

            views.Add(new FeatureView(f.Descriptor, f.Settings, config, availability));
        }

        _views = views;
    }

    public async Task<IReadOnlyList<ScoredResult>> DispatchAsync(string rawText, CancellationToken ct)
    {
        EnsureLoaded();

        var active = _features
            .Zip(_views, (feature, view) => (feature, view))
            .Where(x => x.view.IsActive)
            .ToList();

        var keyword = ParsePrefix(rawText, active.Select(x => x.view), out var terms);
        var targets = keyword is null
            ? active
            : active.Where(x => x.view.Config.Keyword == keyword).ToList();

        var query = new PaneQuery(rawText, keyword, terms);
        var perFeature = await Task.WhenAll(targets.Select(x => CollectAsync(x.feature, x.view, query, ct)));
        return perFeature.SelectMany(x => x).OrderByDescending(s => s.Score).ToList();
    }

    async Task<IReadOnlyList<ScoredResult>> CollectAsync(
        IPaneFeature feature, FeatureView view, PaneQuery query, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PerFeatureTimeout);
        var raw = new List<PaneResult>();
        try
        {
            await foreach (var r in feature.QueryAsync(query, cts.Token).WithCancellation(cts.Token))
                raw.Add(r);
        }
        catch { return Array.Empty<ScoredResult>(); }
        return _ranker.Rank(query, view.Descriptor, view.Config.Priority, raw).ToList();
    }

    // Keywords are accelerators, not gates: a leading keyword narrows the query to
    // one feature, and every feature answers plain queries either way.
    static string? ParsePrefix(string rawText, IEnumerable<FeatureView> active, out string terms)
    {
        var text = rawText.TrimStart();
        foreach (var v in active)
        {
            var kw = v.Config.Keyword;
            if (!string.IsNullOrEmpty(kw) && text.StartsWith(kw, StringComparison.Ordinal))
            {
                terms = text[kw.Length..].TrimStart();
                return kw;
            }
        }
        terms = rawText.Trim();
        return null;
    }
}
