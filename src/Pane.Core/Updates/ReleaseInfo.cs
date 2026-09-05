namespace Pane.Core.Updates;

/// <summary>One downloadable file attached to a release.</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>A published release we could potentially install.</summary>
public sealed record ReleaseInfo(
    AppVersion Version,
    string Tag,
    string HtmlUrl,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The asset with this name, or null. Case-insensitive.</summary>
    public ReleaseAsset? FindAsset(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}
