using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pane.Abstractions;

namespace Pane.Plugins.Apps;

public sealed class AppsPlugin : IPlugin
{
    public PluginMetadata Metadata { get; } = new(
        "apps", "Applications", "🚀", "1.0",
        "Launch installed applications",
        new[] { "app", "open", "launch" });

    IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();

    public Task InitializeAsync(IPluginContext ctx)
    {
        _apps = AppIndexerFactory.Create().Index().ToList();   // cache at init
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var a in _apps)
        {
            var target = a.LaunchTarget;
            yield return new PaneResult(a.Name, "Application", "🚀", 0, () => Launch(target), target);
        }
        await Task.CompletedTask;
    }

    static Task Launch(string target)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start(new ProcessStartInfo("open", $"\"{target}\"") { UseShellExecute = false });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{target}\"") { UseShellExecute = false });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
