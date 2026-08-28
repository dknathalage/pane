using Pane.Platform;
using Xunit;

public class HotkeyParsingTests
{
    [Fact]
    public void Parses_modifiers_and_key()
    {
        var c = HotkeyCombo.Parse("Alt+Space");
        Assert.True(c.alt);
        Assert.False(c.ctrl);
        Assert.Equal("Space", c.key);
    }

    [Fact]
    public void Parses_multiple_modifiers()
    {
        var c = HotkeyCombo.Parse("Ctrl+Shift+P");
        Assert.True(c.ctrl);
        Assert.True(c.shift);
        Assert.Equal("P", c.key);
    }
}
