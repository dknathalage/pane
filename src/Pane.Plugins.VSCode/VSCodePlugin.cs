using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pane.Abstractions;

namespace Pane.Plugins.VSCode;

public sealed class VSCodePlugin : IPlugin
{
    public PluginMetadata Metadata { get; } = new(
        "vscode", "VSCode Repos", "📂", "1.0.0",
        "Open a repo from ~/repos in VSCode",
        new[] { "code", "repo", "vscode" });

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string ReposDir => Path.Combine(Home, "repos");
    static readonly string[] CodePaths =
    {
        "/opt/homebrew/bin/code", "/usr/local/bin/code", "/usr/bin/code", "code"
    };

    public Task InitializeAsync(IPluginContext ctx) => Task.CompletedTask;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var name in RepoScanner.Scan(ReposDir))
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(ReposDir, name);
            yield return new PaneResult(name, full, "📂", 0, () => Open(full), full);
        }
        await Task.CompletedTask;
    }

    static Task Open(string path)
    {
        var code = CodePaths.FirstOrDefault(File.Exists) ?? "code";
        var psi = new ProcessStartInfo(code) { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        using var p = Process.Start(psi);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
