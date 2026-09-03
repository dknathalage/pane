using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.Scripts;

public sealed class ScriptsFeature : IPaneFeature
{
    const string DirsKey = "dirs";
    const string ExtensionsKey = "extensions";
    const string ShellKey = "shell";

    public FeatureDescriptor Descriptor { get; } = new(
        "scripts", "Scripts", "📜", ">", 0, new[] { "script", "run", "sh" });

    public IReadOnlyList<SettingDefinition> Settings { get; } = new SettingDefinition[]
    {
        new PathsSetting(DirsKey, "Script folders",
            new[] { "~/.config/pane/scripts", "~/.config/sol/scripts" },
            "One folder per line. The second is Sol's location, kept for compatibility."),
        new PathsSetting(ExtensionsKey, "File extensions", new[] { ".sh" },
            "One per line, leading dot included."),
        new TextSetting(ShellKey, "Shell", "/bin/bash", "Interpreter used to run a script."),
    };

    string _home = "";
    IReadOnlyList<string> _dirs = Array.Empty<string>();
    IReadOnlyList<string> _extensions = Array.Empty<string>();
    string _shell = "/bin/bash";

    public Task InitializeAsync(FeatureContext ctx, CancellationToken ct)
    {
        _home = ctx.HomeDirectory;
        return Task.CompletedTask;
    }

    public void ApplyConfig(FeatureConfig config)
    {
        _dirs = config.GetPaths(DirsKey).Select(Expand).ToList();
        _extensions = config.GetPaths(ExtensionsKey)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToList();
        _shell = config.GetText(ShellKey);
    }

    public FeatureAvailability CheckAvailability() =>
        _dirs.Any(Directory.Exists)
            ? FeatureAvailability.Available
            : FeatureAvailability.Unavailable($"no script folder exists: {string.Join(", ", _dirs)}");

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var dir in _dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                if (!_extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

                var content = await File.ReadAllTextAsync(file, ct);
                var (name, icon) = ScriptHeader.Parse(content);
                if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(file);

                var path = file;
                var shell = _shell;
                yield return new PaneResult(name, "Script", icon, 0, () => RunScript(shell, path), path);
            }
        }
    }

    string Expand(string path) =>
        path == "~" ? _home
        : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(_home, path[2..])
        : path;

    static Task RunScript(string shell, string path)
    {
        var psi = new ProcessStartInfo(shell) { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        try { using var p = Process.Start(psi); } catch { /* best-effort */ }
        return Task.CompletedTask;
    }
}
