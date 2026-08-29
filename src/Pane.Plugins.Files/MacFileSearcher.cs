namespace Pane.Plugins.Files;

/// <summary>macOS backend: queries Spotlight via <c>mdfind</c>, scoped to $HOME.</summary>
public sealed class MacFileSearcher : IFileSearcher
{
    public IReadOnlyList<FileHit> Search(string terms, int max, CancellationToken ct)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // -name matches the display name; -onlyin scopes to home to cut system noise.
        var args = new[] { "-onlyin", home, "-name", terms };
        return ProcessSearch.RunLines("mdfind", args, max, ct);
    }
}
