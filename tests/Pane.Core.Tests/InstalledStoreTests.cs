using Pane.Core.Marketplace;
using Xunit;

public class InstalledStoreTests
{
    static InstalledStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"inst-{Guid.NewGuid():N}.json"));

    [Fact]
    public void Record_then_get_roundtrips()
    {
        var s = NewStore();
        s.Record("com.pane.apps", new InstalledInfo("Pane Official", "https://x/Apps.zip", "1.0.0"));
        var info = s.Get("com.pane.apps");
        Assert.NotNull(info);
        Assert.Equal("Pane Official", info!.Marketplace);
        Assert.Equal("https://x/Apps.zip", info.SourceUrl);
        Assert.Equal("1.0.0", info.Version);
    }

    [Fact]
    public void Get_unknown_returns_null()
        => Assert.Null(NewStore().Get("nope"));

    [Fact]
    public void Remove_deletes_the_entry()
    {
        var s = NewStore();
        s.Record("x", new InstalledInfo("m", "u", "1.0"));
        s.Remove("x");
        Assert.Null(s.Get("x"));
    }

    [Fact]
    public void Record_overwrites_and_persists_across_instances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"inst-{Guid.NewGuid():N}.json");
        new InstalledStore(path).Record("x", new InstalledInfo("m", "u", "1.0"));
        new InstalledStore(path).Record("x", new InstalledInfo("m", "u", "2.0"));
        Assert.Equal("2.0", new InstalledStore(path).Get("x")!.Version);
    }
}
