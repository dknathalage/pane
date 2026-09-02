using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.Files;

/// <summary>
/// Searches the OS's native file index for files and folders. Keyword-activated
/// with "/" (e.g. "/report") so it never floods the launcher on ordinary queries.
/// </summary>
public sealed class FilesFeature
{
    const string ActivationKeyword = "/";
    const int MinTermLength = 2;
    const int MaxResults = 50;
    static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(150);

    public FeatureDescriptor Descriptor { get; } = new(
        "files", "Files", "📁", "/", 0, new[] { "file", "folder", "find" });

    IFileSearcher? _searcher;
    readonly TimeSpan _debounce;

    /// <summary>Production entry point; the real searcher is resolved at init.</summary>
    public FilesFeature() => _debounce = DefaultDebounce;

    /// <summary>Test seam: inject a searcher; no debounce so tests stay fast.</summary>
    public FilesFeature(IFileSearcher searcher) : this(searcher, TimeSpan.Zero) { }

    /// <summary>Test seam: inject a searcher and an explicit debounce delay.</summary>
    public FilesFeature(IFileSearcher searcher, TimeSpan debounce)
    {
        _searcher = searcher;
        _debounce = debounce;
    }

    public Task InitializeAsync()
    {
        _searcher ??= FileSearcherFactory.Create();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        // Only search when explicitly invoked with the "/" keyword.
        if (q.Keyword != ActivationKeyword) yield break;

        var terms = q.Terms.Trim();
        if (terms.Length < MinTermLength || _searcher is null) yield break;

        // Wildcard queries feed the longest literal run to the index for
        // candidates; the host glob-matcher does the precise name filtering.
        var seed = LongestLiteralRun(terms);
        if (seed.Length < MinTermLength) yield break;

        // Debounce: wait out a quiet period before hitting the index. A newer
        // keystroke cancels this token, so the stale query never spawns a search.
        if (_debounce > TimeSpan.Zero)
            await Task.Delay(_debounce, ct);

        foreach (var hit in _searcher.Search(seed, MaxResults, ct))
        {
            var path = hit.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;   // root paths, etc.

            var icon = hit.IsDirectory ? "📁" : "📄";
            var subtitle = Abbreviate(Path.GetDirectoryName(path));
            var full = hit.FullPath;
            yield return new PaneResult(name, subtitle, icon, 0, () => Open(full), full);
        }
        await Task.CompletedTask;
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
