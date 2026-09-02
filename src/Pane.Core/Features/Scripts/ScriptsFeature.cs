using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.Scripts;

public sealed class ScriptsFeature
{
    public FeatureDescriptor Descriptor { get; } = new(
        "scripts", "Scripts", "📜", ">", 0, new[] { "script", "run", "sh" });

    static readonly string[] Dirs =
    {
        Path.Combine(Home, ".config", "pane", "scripts"),
        Path.Combine(Home, ".config", "sol", "scripts"),   // Sol compatibility
    };
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public Task InitializeAsync() => Task.CompletedTask;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var dir in Dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.sh"))
            {
                ct.ThrowIfCancellationRequested();
                var content = await File.ReadAllTextAsync(file, ct);
                var (name, icon) = ScriptHeader.Parse(content);
                if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(file);
                var path = file;
                yield return new PaneResult(name, "Script", icon, 0, () => RunScript(path), path);
            }
        }
    }

    static Task RunScript(string path)
    {
        var psi = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        using var p = Process.Start(psi);
        return Task.CompletedTask;
    }

}
