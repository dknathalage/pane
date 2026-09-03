namespace Pane.Core.Features.Apps;

public record AppEntry(string Name, string LaunchTarget);

public interface IAppIndexer
{
    /// <summary>Where this platform normally keeps applications; the declared default for the "dirs" setting.</summary>
    IReadOnlyList<string> DefaultDirectories { get; }

    IEnumerable<AppEntry> Index(IReadOnlyList<string> dirs);
}
