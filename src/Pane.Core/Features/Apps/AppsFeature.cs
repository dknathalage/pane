using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.Apps;

public sealed class AppsFeature : IPaneFeature
{
    const string DirsKey = "dirs";

    public FeatureDescriptor Descriptor { get; } = new(
        "apps", "Applications", "🚀", null, 0, new[] { "app", "open", "launch" });

    public IReadOnlyList<SettingDefinition> Settings { get; }

    readonly IAppIndexer _indexer;
    IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();
    IReadOnlyList<string> _dirs = Array.Empty<string>();
    MacAppIcons? _icons;
    string _home = "";
    string _dataDir = "";

    public AppsFeature() : this(AppIndexerFactory.Create()) { }

    /// <summary>Test seam: index with a specific backend rather than the platform one.</summary>
    public AppsFeature(IAppIndexer indexer)
    {
        _indexer = indexer;
        Settings = new SettingDefinition[]
        {
            new PathsSetting(DirsKey, "Application folders", indexer.DefaultDirectories,
                "One folder per line."),
        };
    }

    public Task InitializeAsync(FeatureContext ctx, CancellationToken ct)
    {
        _home = ctx.HomeDirectory;
        _dataDir = Path.Combine(ctx.DataDirectory, "apps");
        return Task.CompletedTask;
    }

    public void ApplyConfig(FeatureConfig config)
    {
        _dirs = config.GetPaths(DirsKey).Select(Expand).ToList();
        Reindex();
    }

    public FeatureAvailability CheckAvailability() =>
        _apps.Count > 0
            ? FeatureAvailability.Available
            : FeatureAvailability.Unavailable(
                $"no applications found in: {string.Join(", ", _dirs)}");

    void Reindex()
    {
        _apps = _indexer.Index(_dirs).ToList();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || _dataDir.Length == 0) return;

        Directory.CreateDirectory(_dataDir);
        _icons ??= new MacAppIcons(_dataDir);
        var targets = _apps.Select(a => a.LaunchTarget).ToList();
        _icons.LoadCached(targets);
        _ = _icons.GenerateMissingAsync(targets);
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

    string Expand(string path) =>
        path == "~" ? _home
        : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(_home, path[2..])
        : path;

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
        try { using var p = Process.Start(psi); } catch { /* best-effort */ }
        return Task.CompletedTask;
    }
}
