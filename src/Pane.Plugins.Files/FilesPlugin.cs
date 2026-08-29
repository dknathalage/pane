using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pane.Abstractions;

namespace Pane.Plugins.Files;

/// <summary>
/// Searches the OS's native file index for files and folders. Keyword-activated
/// with "/" (e.g. "/report") so it never floods the launcher on ordinary queries.
/// </summary>
public sealed class FilesPlugin : IPlugin
{
    const string ActivationKeyword = "/";
    const int MinTermLength = 2;
    const int MaxResults = 50;

    public PluginMetadata Metadata { get; } = new(
        "files", "Files", "📁", "1.0.0",
        "Search files and folders",
        new[] { "file", "folder", "find" }, ActivationKeyword);

    IFileSearcher? _searcher;

    /// <summary>Production entry point; the real searcher is resolved at init.</summary>
    public FilesPlugin() { }

    /// <summary>Test seam: inject a searcher directly.</summary>
    public FilesPlugin(IFileSearcher searcher) => _searcher = searcher;

    public Task InitializeAsync(IPluginContext ctx)
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

        foreach (var hit in _searcher.Search(terms, MaxResults, ct))
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
