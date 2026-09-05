namespace Pane.Core.Updates;

/// <summary>
/// Puts a downloaded release on disk and restarts into it. Platform-specific;
/// exists as an interface so UpdateService is testable without ever touching
/// the filesystem or spawning a process.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>False when there is nothing safe to replace (e.g. a dev run).</summary>
    bool CanInstall { get; }

    /// <summary>Why <see cref="CanInstall"/> is false, for the user to read.</summary>
    string? UnavailableReason { get; }

    /// <param name="mustExceed">
    /// The downloaded bundle is rejected unless its own version is strictly
    /// greater than this. Checked before anything installed is touched.
    /// </param>
    Task InstallAsync(ReleaseAsset asset, AppVersion mustExceed,
                      IProgress<int> progress, CancellationToken ct);
}
