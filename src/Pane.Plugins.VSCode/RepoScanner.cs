namespace Pane.Plugins.VSCode;

public static class RepoScanner
{
    public static IEnumerable<string> Scan(string reposDir)
    {
        if (!Directory.Exists(reposDir)) return Array.Empty<string>();
        return Directory.GetDirectories(reposDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n) && !n!.StartsWith('.'))
            .OrderBy(n => n, StringComparer.Ordinal)!;
    }
}
