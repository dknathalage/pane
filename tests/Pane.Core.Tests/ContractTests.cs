using Pane.Core.Contracts;

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
}
