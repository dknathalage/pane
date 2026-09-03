using System.Text.Json.Nodes;
using Pane.Core.Contracts;
using Xunit;

public class FeatureConfigTests
{
    static readonly FeatureDescriptor Desc =
        new("demo", "Demo", "🧪", "@", 5, new[] { "demo" });

    static readonly SettingDefinition[] Schema =
    {
        new BoolSetting("deep", "Deep search", Default: true),
        new IntSetting("maxResults", "Max results", Default: 25, Min: 1, Max: 100),
        new TextSetting("shell", "Shell", Default: "/bin/bash"),
        new PathsSetting("dirs", "Directories", Default: new[] { "/default" }),
    };

    static FeatureConfig Resolve(JsonObject values) => FeatureConfig.Resolve(Desc, Schema, values);

    [Fact]
    public void Missing_values_fall_back_to_schema_defaults()
    {
        var c = Resolve(new JsonObject());
        Assert.True(c.GetBool("deep"));
        Assert.Equal(25, c.GetInt("maxResults"));
        Assert.Equal("/bin/bash", c.GetText("shell"));
        Assert.Equal(new[] { "/default" }, c.GetPaths("dirs"));
    }

    [Fact]
    public void Stored_values_override_defaults()
    {
        var c = Resolve(new JsonObject
        {
            ["deep"] = false,
            ["maxResults"] = 10,
            ["shell"] = "/bin/zsh",
            ["dirs"] = new JsonArray("/a", "/b"),
        });
        Assert.False(c.GetBool("deep"));
        Assert.Equal(10, c.GetInt("maxResults"));
        Assert.Equal("/bin/zsh", c.GetText("shell"));
        Assert.Equal(new[] { "/a", "/b" }, c.GetPaths("dirs"));
    }

    [Fact]
    public void Int_values_are_clamped_to_the_declared_range()
    {
        Assert.Equal(100, Resolve(new JsonObject { ["maxResults"] = 5000 }).GetInt("maxResults"));
        Assert.Equal(1, Resolve(new JsonObject { ["maxResults"] = -3 }).GetInt("maxResults"));
    }

    [Fact]
    public void Wrongly_typed_stored_value_falls_back_to_the_default()
    {
        var c = Resolve(new JsonObject { ["maxResults"] = "banana", ["deep"] = "yes" });
        Assert.Equal(25, c.GetInt("maxResults"));
        Assert.True(c.GetBool("deep"));
    }

    [Fact]
    public void Blank_and_non_string_path_entries_are_dropped()
    {
        var c = Resolve(new JsonObject { ["dirs"] = new JsonArray("/a", "  ", "", "/b") });
        Assert.Equal(new[] { "/a", "/b" }, c.GetPaths("dirs"));
    }

    [Fact]
    public void Blank_text_falls_back_to_the_default()
    {
        Assert.Equal("/bin/bash", Resolve(new JsonObject { ["shell"] = "   " }).GetText("shell"));
    }

    [Fact]
    public void Reading_a_key_the_feature_never_declared_throws()
    {
        var c = Resolve(new JsonObject());
        Assert.Throws<KeyNotFoundException>(() => c.GetInt("nope"));
    }

    [Fact]
    public void Reading_a_key_declared_with_another_type_throws()
    {
        var c = Resolve(new JsonObject());
        Assert.Throws<InvalidOperationException>(() => c.GetInt("shell"));
    }

    [Fact]
    public void Enabled_defaults_to_true_and_honours_a_stored_false()
    {
        Assert.True(Resolve(new JsonObject()).Enabled);
        Assert.False(Resolve(new JsonObject { ["enabled"] = false }).Enabled);
    }

    [Fact]
    public void Keyword_defaults_to_the_descriptor_keyword()
    {
        Assert.Equal("@", Resolve(new JsonObject()).Keyword);
        Assert.Equal("!", Resolve(new JsonObject { ["keyword"] = "!" }).Keyword);
    }

    [Fact]
    public void Blank_keyword_clears_the_prefix_rather_than_restoring_the_default()
    {
        Assert.Null(Resolve(new JsonObject { ["keyword"] = "" }).Keyword);
    }

    [Fact]
    public void Priority_defaults_to_the_descriptor_priority()
    {
        Assert.Equal(5, Resolve(new JsonObject()).Priority);
        Assert.Equal(-20, Resolve(new JsonObject { ["priority"] = -20 }).Priority);
    }

    [Fact]
    public void Universal_settings_are_exposed_so_the_ui_can_render_them_uniformly()
    {
        var keys = FeatureConfig.UniversalSettings(Desc).Select(s => s.Key).ToArray();
        Assert.Equal(new[] { "enabled", "keyword", "priority" }, keys);
    }

    [Fact]
    public void A_feature_may_not_redeclare_a_universal_key()
    {
        var clash = new SettingDefinition[] { new IntSetting("priority", "Priority", 0, -100, 100) };
        Assert.Throws<ArgumentException>(() => FeatureConfig.Resolve(Desc, clash, new JsonObject()));
    }
}
