namespace Pane.Core.Features.Apps;

public sealed class WindowsAppIndexer : IAppIndexer
{
    public IReadOnlyList<string> DefaultDirectories { get; } = new[]
    {
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
    };

    public IEnumerable<AppEntry> Index(IReadOnlyList<string> dirs)
    {
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                yield return new AppEntry(name, lnk);
            }
        }
    }
}
