namespace Pane.Core.Features.Files;

/// <summary>A single filesystem match returned by an <see cref="IFileSearcher"/>.</summary>
/// <param name="FullPath">Absolute path to the file or folder.</param>
/// <param name="IsDirectory">True when the path is a directory.</param>
public readonly record struct FileHit(string FullPath, bool IsDirectory);

/// <summary>
/// Queries the host OS's native search index for files/folders whose name
/// matches <paramref name="terms"/>. Implementations shell out to the platform
/// index (mdfind, plocate, …); the interface is the seam that keeps the plugin
/// logic testable without touching the real filesystem.
/// </summary>
public interface IFileSearcher
{
    /// <summary>False when this platform's index tool isn't installed or isn't implemented yet.</summary>
    bool IsAvailable { get; }

    IReadOnlyList<FileHit> Search(string terms, int max, CancellationToken ct);
}

/// <summary>Locates an index binary on PATH, shared by the platform backends.</summary>
public static class ExecutableProbe
{
    public static bool Exists(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append("/usr/bin")
            .Append("/bin")
            .Select(d => Path.Combine(d, command))
            .Any(File.Exists);
}
