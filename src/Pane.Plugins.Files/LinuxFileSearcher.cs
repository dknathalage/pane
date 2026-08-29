namespace Pane.Plugins.Files;

/// <summary>
/// Linux backend: queries the updatedb index via <c>plocate</c>, falling back to
/// <c>locate</c>. Case-insensitive, basename match, limited to <c>max</c> rows.
/// </summary>
public sealed class LinuxFileSearcher : IFileSearcher
{
    public IReadOnlyList<FileHit> Search(string terms, int max, CancellationToken ct)
    {
        var args = new[] { "-i", "-b", "-l", max.ToString(), terms };
        var hits = ProcessSearch.RunLines("plocate", args, max, ct);
        if (hits.Count == 0)
            hits = ProcessSearch.RunLines("locate", args, max, ct);
        return hits;
    }
}
