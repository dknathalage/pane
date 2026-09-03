using Pane.Core.Settings;
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

    // A combo the settings UI would otherwise save happily, bricking the hotkey
    // until the user hand-edits settings.json.
    [Theory]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+Shift+P")]
    [InlineData("cmd+k")]
    public void Valid_combos_have_a_modifier_and_a_key(string combo)
    {
        Assert.True(HotkeyCombo.IsValid(combo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alt+")]          // modifier, no key
    [InlineData("Alt")]           // modifier only
    [InlineData("Space")]         // key with no modifier
    [InlineData("Ctrl+Shift")]    // two modifiers, still no key
    public void Invalid_combos_are_rejected(string combo)
    {
        Assert.False(HotkeyCombo.IsValid(combo));
    }
}
