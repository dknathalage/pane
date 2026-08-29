using Pane.Core.Marketplace;
using Xunit;

public class MarketplaceConfigStoreTests
{
    static MarketplaceConfigStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"mkts-{Guid.NewGuid():N}.json"),
            "Pane Official", "https://github.com/dknathalage/pane");

    [Fact]
    public void Seeds_builtin_default_when_absent()
    {
        var s = NewStore();
        var one = Assert.Single(s.List());
        Assert.Equal("Pane Official", one.Name);
        Assert.True(one.BuiltIn);
    }

    [Fact]
    public void Add_then_list_includes_user_source()
    {
        var s = NewStore();
        s.Add("Team", "https://github.com/acme/plugins");
        Assert.Contains(s.List(), r => r.Name == "Team" && !r.BuiltIn);
        Assert.Equal(2, s.List().Count);
    }

    [Fact]
    public void Add_is_idempotent_on_same_source()
    {
        var s = NewStore();
        s.Add("Team", "https://github.com/acme/plugins");
        s.Add("Team again", "https://github.com/acme/plugins");
        Assert.Equal(2, s.List().Count);   // default + one Team
    }

    [Fact]
    public void Remove_user_source_works_but_builtin_is_protected()
    {
        var s = NewStore();
        s.Add("Team", "https://github.com/acme/plugins");
        s.Remove("https://github.com/acme/plugins");
        Assert.Single(s.List());
        Assert.Throws<InvalidOperationException>(() => s.Remove("https://github.com/dknathalage/pane"));
    }
}
