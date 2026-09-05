namespace Pane.Core.Updates;

/// <summary>
/// Where new releases come from. Exists so UpdateService can be tested against
/// a fake without any network access.
/// </summary>
public interface IReleaseSource
{
    /// <summary>
    /// The latest release, or null when there isn't one we can reason about
    /// (unparseable tag, malformed payload). Transport and HTTP failures throw.
    /// </summary>
    Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct);
}
