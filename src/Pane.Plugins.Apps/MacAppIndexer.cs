namespace Pane.Plugins.Apps;

public sealed class MacAppIndexer : IAppIndexer
{
    readonly string[] _dirs;

    public MacAppIndexer(string[]? dirs = null) =>
        _dirs = dirs ?? new[] { "/Applications", "/System/Applications",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications") };

    public IEnumerable<AppEntry> Index()
    {
        foreach (var dir in _dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var app in Directory.GetDirectories(dir, "*.app"))
                yield return new AppEntry(Path.GetFileNameWithoutExtension(app), app);
        }
    }
}
