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

    [Fact]
    public void PluginMetadata_defaults_settings_to_null_and_carries_a_schema()
    {
        var bare = new PluginMetadata("id", "Name", "🧪", "1.0", "desc", new[] { "kw" });
        Assert.Null(bare.Settings);

        var withSchema = bare with
        {
            Settings = new[]
            {
                new PluginSettingSpec("apiKey", "API Key", PluginSettingType.Text),
                new PluginSettingSpec("theme", "Theme", PluginSettingType.Choice,
                    Default: "dark", Choices: new[] { "light", "dark" })
            }
        };
        Assert.Equal("apiKey", withSchema.Settings![0].Key);
        Assert.Equal(PluginSettingType.Choice, withSchema.Settings![1].Type);
        Assert.Equal("dark", withSchema.Settings![1].Default);
    }
}
