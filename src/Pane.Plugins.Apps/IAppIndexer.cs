namespace Pane.Plugins.Apps;

public record AppEntry(string Name, string LaunchTarget);

public interface IAppIndexer
{
    IEnumerable<AppEntry> Index();
}
