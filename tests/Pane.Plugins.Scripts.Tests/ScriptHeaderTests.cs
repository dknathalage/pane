using Pane.Plugins.Scripts;
using Xunit;

public class ScriptHeaderTests
{
    [Fact]
    public void Parses_name_and_icon_from_header()
    {
        var content = "# name: Open Repo\n# icon: 📂\necho hi\n";
        var (name, icon) = ScriptHeader.Parse(content);
        Assert.Equal("Open Repo", name);
        Assert.Equal("📂", icon);
    }

    [Fact]
    public void Falls_back_to_defaults_when_absent()
    {
        var (name, icon) = ScriptHeader.Parse("echo hi\n");
        Assert.Equal("", name);        // caller substitutes filename
        Assert.Equal("📜", icon);
    }
}
