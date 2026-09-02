using System.Runtime.InteropServices;

namespace Pane.Core.Features.Files;

/// <summary>Picks the native file-index backend for the current OS.</summary>
public static class FileSearcherFactory
{
    public static IFileSearcher Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return new MacFileSearcher();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return new LinuxFileSearcher();
        // Windows native search needs a COM/OleDb SystemIndex query — a labeled
        // follow-up. Until then it degrades to no results rather than misbehaving.
        return new NullFileSearcher();
    }
}

/// <summary>No-op backend for platforms without an index integration yet.</summary>
public sealed class NullFileSearcher : IFileSearcher
{
    public IReadOnlyList<FileHit> Search(string terms, int max, CancellationToken ct) =>
        Array.Empty<FileHit>();
}
