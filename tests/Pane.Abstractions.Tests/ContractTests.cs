using Pane.Abstractions;
using Xunit;

public class ContractTests
{
    [Fact]
    public void PaneResult_carries_activation_delegate()
    {
        var ran = false;
        var r = new PaneResult("t", "s", "i", 1.0, () => { ran = true; return Task.CompletedTask; });
        r.Activate();
        Assert.True(ran);
        Assert.Null(r.SearchText);
    }

    [Fact]
    public void PluginMetadata_defaults_keyword_and_priority()
    {
        var m = new PluginMetadata("id", "Name", "🔍", "1.0", "desc", new[] { "a" });
        Assert.Null(m.Keyword);
        Assert.Equal(0, m.Priority);
    }
}
