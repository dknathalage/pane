using Pane.Plugins.Apps;
using Xunit;

public class MacAppIndexerTests
{
    [Fact]
    public void Indexes_dotapp_bundles_by_name()
    {
        var root = Path.Combine(Path.GetTempPath(), $"apps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Safari.app"));
        Directory.CreateDirectory(Path.Combine(root, "Notes.app"));
        Directory.CreateDirectory(Path.Combine(root, "NotAnApp"));

        var indexer = new MacAppIndexer(new[] { root });
        var names = indexer.Index().Select(a => a.Name).OrderBy(x => x).ToArray();

        Assert.Equal(new[] { "Notes", "Safari" }, names);
    }
}
