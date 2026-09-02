using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pane.Abstractions;

namespace Pane.Core.Features.Apps;

public sealed class AppsFeature
{
    public FeatureDescriptor Descriptor { get; } = new(
        "apps", "Applications", "🚀", null, 0, new[] { "app", "open", "launch" });

    IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();
    MacAppIcons? _icons;

    readonly string _dataDir;
    public AppsFeature(string dataDirectory) => _dataDir = dataDirectory;

    public Task InitializeAsync()
    {
        _apps = AppIndexerFactory.Create().Index().ToList();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Directory.CreateDirectory(_dataDir);
            _icons = new MacAppIcons(_dataDir);
            var targets = _apps.Select(a => a.LaunchTarget).ToList();
            _icons.LoadCached(targets);
            _ = _icons.GenerateMissingAsync(targets);
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

}
