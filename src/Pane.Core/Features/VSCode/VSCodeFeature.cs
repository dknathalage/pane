using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.VSCode;

public sealed class VSCodeFeature : IPaneFeature
{
    const string RepoDirsKey = "repoDirs";
    const string EditorKey = "editor";

    public FeatureDescriptor Descriptor { get; } = new(
        "vscode", "VSCode Repos", "📂", null, 0, new[] { "code", "repo", "vscode" });

    public IReadOnlyList<SettingDefinition> Settings { get; } = new SettingDefinition[]
    {
        new PathsSetting(RepoDirsKey, "Repository folders", new[] { "~/repos" },
            "One folder per line. Each immediate subfolder is offered as a repo."),
        new TextSetting(EditorKey, "Editor command", "code",
            "Command or absolute path used to open a repo."),
    };

    // Common install locations, tried before falling back to PATH.
    static readonly string[] WellKnownDirs = { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" };

    string _home = "";
    IReadOnlyList<string> _repoDirs = Array.Empty<string>();
    string _editor = "code";

    public Task InitializeAsync(FeatureContext ctx, CancellationToken ct)
    {
        _home = ctx.HomeDirectory;
        return Task.CompletedTask;
    }

    public void ApplyConfig(FeatureConfig config)
    {
        _repoDirs = config.GetPaths(RepoDirsKey).Select(Expand).ToList();
        _editor = config.GetText(EditorKey);
    }

    public FeatureAvailability CheckAvailability()
    {
        if (ResolveEditor(_editor) is null)
            return FeatureAvailability.Unavailable($"'{_editor}' was not found — set the editor command in settings.");

        if (!_repoDirs.Any(Directory.Exists))
            return FeatureAvailability.Unavailable(
                $"no repository folder exists: {string.Join(", ", _repoDirs)}");

        return FeatureAvailability.Available;
    }

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var dir in _repoDirs)
        {
            foreach (var name in RepoScanner.Scan(dir))
            {
                ct.ThrowIfCancellationRequested();
                var full = Path.Combine(dir, name);
                yield return new PaneResult(name, full, "📂", 0, () => Open(full), full);
            }
        }
        await Task.CompletedTask;
    }

    string Expand(string path) =>
        path == "~" ? _home
        : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(_home, path[2..])
        : path;

    /// <summary>The editor's absolute path, or null when it isn't installed here.</summary>
    static string? ResolveEditor(string command)
    {
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(command) ? command : null;

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return WellKnownDirs.Concat(pathDirs)
            .Select(d => Path.Combine(d, command))
            .FirstOrDefault(File.Exists);
    }

    Task Open(string path)
    {
        var editor = ResolveEditor(_editor);
        if (editor is null) return Task.CompletedTask;

        var psi = new ProcessStartInfo(editor) { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        try { using var p = Process.Start(psi); } catch { /* best-effort */ }
        return Task.CompletedTask;
    }
}
