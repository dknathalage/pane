using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pane.Abstractions;

namespace Pane.Core.Features.VSCode;

public sealed class VSCodeFeature
{
    public FeatureDescriptor Descriptor { get; } = new(
        "vscode", "VSCode Repos", "📂", null, 0, new[] { "code", "repo", "vscode" });

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string ReposDir => Path.Combine(Home, "repos");
    static readonly string[] CodePaths =
    {
        "/opt/homebrew/bin/code", "/usr/local/bin/code", "/usr/bin/code", "code"
    };

    public Task InitializeAsync() => Task.CompletedTask;

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

}
