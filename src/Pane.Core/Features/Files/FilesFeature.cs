using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.Files;

/// <summary>
/// Searches the OS's native file index. It answers plain queries out of the box —
/// the "/" keyword is an accelerator that shows files only, not a requirement — and
/// carries a negative default priority so file hits sit below apps rather than
/// flooding an ordinary query.
/// </summary>
public sealed class FilesFeature : IPaneFeature
{
    const string SearchPlainQueriesKey = "searchPlainQueries";
    const string MinTermLengthKey = "minTermLength";
    const string MaxResultsKey = "maxResults";
    const string DebounceKey = "debounceMs";

    public FeatureDescriptor Descriptor { get; } = new(
        "files", "Files", "📁", "/", -20, new[] { "file", "folder", "find" });

    public IReadOnlyList<SettingDefinition> Settings { get; } = new SettingDefinition[]
    {
        new BoolSetting(SearchPlainQueriesKey, "Search without the keyword", Default: true,
            "Off means files appear only when the query starts with the keyword."),
        new IntSetting(MinTermLengthKey, "Minimum characters", 3, 1, 10,
            "Shorter queries are ignored so the index isn't hit on every keystroke."),
        new IntSetting(MaxResultsKey, "Maximum results", 25, 1, 200),
        new IntSetting(DebounceKey, "Debounce (ms)", 150, 0, 1000,
            "Quiet period before a query reaches the index."),
    };

    IFileSearcher? _searcher;
    bool _searchPlainQueries = true;
    int _minTermLength = 3;
    int _maxResults = 25;
    TimeSpan _debounce = TimeSpan.FromMilliseconds(150);

    public FilesFeature() { }

    /// <summary>Test seam: inject a searcher instead of resolving the platform one.</summary>
    public FilesFeature(IFileSearcher searcher) => _searcher = searcher;

    public Task InitializeAsync(FeatureContext ctx, CancellationToken ct)
    {
        _searcher ??= FileSearcherFactory.Create();
        return Task.CompletedTask;
    }

    public void ApplyConfig(FeatureConfig config)
    {
        _searchPlainQueries = config.GetBool(SearchPlainQueriesKey);
        _minTermLength = config.GetInt(MinTermLengthKey);
        _maxResults = config.GetInt(MaxResultsKey);
        _debounce = TimeSpan.FromMilliseconds(config.GetInt(DebounceKey));
    }

    public FeatureAvailability CheckAvailability() =>
        _searcher?.IsAvailable == true
            ? FeatureAvailability.Available
            : FeatureAvailability.Unavailable("no file search index is available on this system.");

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        // The keyword narrows to files only; without it we still search, unless
        // the user has switched plain-query searching off.
        if (q.Keyword != Descriptor.Keyword && !_searchPlainQueries) yield break;

        var terms = q.Terms.Trim();
        if (terms.Length < _minTermLength || _searcher is null) yield break;

        // Wildcard queries feed the longest literal run to the index for
        // candidates; the host glob-matcher does the precise name filtering.
        var seed = LongestLiteralRun(terms);
        if (seed.Length < _minTermLength) yield break;

        // Debounce: wait out a quiet period before hitting the index. A newer
        // keystroke cancels this token, so the stale query never spawns a search.
        if (_debounce > TimeSpan.Zero)
            await Task.Delay(_debounce, ct);

        foreach (var hit in _searcher.Search(seed, _maxResults, ct))
        {
            var path = hit.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;   // root paths, etc.

            var icon = hit.IsDirectory ? "📁" : "📄";
            var subtitle = Abbreviate(Path.GetDirectoryName(path));
            var full = hit.FullPath;
            yield return new PaneResult(name, subtitle, icon, 0, () => Open(full), full);
        }
    }

    // The longest maximal run of non-wildcard characters — the most selective
    // literal to hand the index. For a plain query this is the whole string.
    static string LongestLiteralRun(string terms)
    {
        var best = "";
        for (int i = 0; i < terms.Length;)
        {
            if (terms[i] is '*' or '?') { i++; continue; }
            int start = i;
            while (i < terms.Length && terms[i] is not ('*' or '?')) i++;
            if (i - start > best.Length) best = terms[start..i];
        }
        return best;
    }

    // Replace the home-directory prefix with "~" to keep subtitles short.
    static string Abbreviate(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (dir == home) return "~";
        if (dir.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return "~" + dir[home.Length..];
        return dir;
    }

    static Task Open(string target)
    {
        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add(target);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            psi = new ProcessStartInfo(target) { UseShellExecute = true };
        }
        else
        {
            psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(target);
        }
        try { using var p = Process.Start(psi); } catch { /* best-effort */ }
        return Task.CompletedTask;
    }
}
