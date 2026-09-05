namespace Pane.Core.Updates;

/// <summary>
/// Everything the updater can be doing, as one closed set. Every failure is a
/// status the user can read — never an exception that escapes to the UI.
/// </summary>
public abstract record UpdateStatus
{
    // Private ctor closes the hierarchy: only the nested cases below exist.
    UpdateStatus() { }

    public sealed record Idle : UpdateStatus;
    public sealed record Checking : UpdateStatus;
    public sealed record UpToDate(DateTimeOffset? CheckedAt) : UpdateStatus;
    public sealed record Available(ReleaseInfo Release, ReleaseAsset Asset) : UpdateStatus;
    public sealed record Downloading(int Percent) : UpdateStatus;
    public sealed record Installing : UpdateStatus;
    public sealed record Failed(string Message) : UpdateStatus;
}
