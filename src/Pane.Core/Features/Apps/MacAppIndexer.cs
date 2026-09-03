namespace Pane.Core.Features.Apps;

public sealed class MacAppIndexer : IAppIndexer
{
    public IReadOnlyList<string> DefaultDirectories { get; } =
        new[] { "/Applications", "/System/Applications", "~/Applications" };

    public IEnumerable<AppEntry> Index(IReadOnlyList<string> dirs)
    {
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var app in Directory.GetDirectories(dir, "*.app"))
                yield return new AppEntry(Path.GetFileNameWithoutExtension(app), app);
        }
    }
}
