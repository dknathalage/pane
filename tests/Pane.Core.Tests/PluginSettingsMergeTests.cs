using Pane.Abstractions;
using Pane.Core.Plugins;
using Xunit;

public class PluginSettingsMergeTests
{
    static PluginSettingSpec Spec(string key, string? def) =>
        new(key, key, PluginSettingType.Text, Default: def);

    [Fact]
    public void Uses_schema_defaults_when_nothing_stored()
    {
        var merged = PluginSettingsMerge.Merge(new[] { Spec("a", "1"), Spec("b", "2") }, null);
        Assert.Equal("1", merged["a"]);
        Assert.Equal("2", merged["b"]);
    }

    [Fact]
    public void Stored_values_override_defaults()
    {
        var merged = PluginSettingsMerge.Merge(
            new[] { Spec("a", "1") },
            new Dictionary<string, string> { ["a"] = "override" });
        Assert.Equal("override", merged["a"]);
    }

    [Fact]
    public void Keys_without_a_default_are_absent_until_stored()
    {
        var merged = PluginSettingsMerge.Merge(new[] { Spec("a", null) }, null);
        Assert.False(merged.ContainsKey("a"));
    }

    [Fact]
    public void Stored_keys_not_in_schema_pass_through()
    {
        var merged = PluginSettingsMerge.Merge(
            null, new Dictionary<string, string> { ["x"] = "y" });
        Assert.Equal("y", merged["x"]);
    }
}
