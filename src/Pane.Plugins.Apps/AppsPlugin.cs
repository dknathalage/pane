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
    MacAppIcons? _icons;

    public Task InitializeAsync(IPluginContext ctx)
    {
        _apps = AppIndexerFactory.Create().Index().ToList();   // cache at init

        // Real macOS app icons: load cached ones now, generate the rest in the
        // background. Icons appear as the cache warms (instant on later runs).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _icons = new MacAppIcons(ctx.DataDirectory);
            var targets = _apps.Select(a => a.LaunchTarget).ToList();
            _icons.LoadCached(targets);                  // instant for already-cached icons
            _ = _icons.GenerateMissingAsync(targets);    // background for the rest
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var a in _apps)
        {
            var target = a.LaunchTarget;
            var icon = _icons?.TryGet(target) ?? "";   // data-URI when ready; UI falls back to a grid glyph
            yield return new PaneResult(a.Name, "Application", icon, 0, () => Launch(target), target);
        }
        await Task.CompletedTask;
    }

    static Task Launch(string target)
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
        using var p = Process.Start(psi);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
