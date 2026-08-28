using Pane.Core.Settings;
using Xunit;

public class SettingsStoreTests
{
    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);
        var s = store.Load();
        Assert.Empty(s.DisabledPlugins);
        Assert.False(string.IsNullOrEmpty(s.Hotkey));
    }

    [Fact]
    public void Save_then_load_roundtrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);
        store.Save(new PaneSettings(new HashSet<string> { "x" }, "Alt+Space", new()));
        var s = store.Load();
        Assert.Contains("x", s.DisabledPlugins);
        Assert.Equal("Alt+Space", s.Hotkey);
    }

    [Fact]
    public void Roundtrips_per_plugin_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);
        store.Save(new PaneSettings(
            new HashSet<string>(), "Alt+Space",
            new Dictionary<string, Dictionary<string, string>>
            {
                ["p1"] = new() { ["k"] = "v" }
            }));
        var s = store.Load();
        Assert.Equal("v", s.PluginSettings["p1"]["k"]);
    }

    [Fact]
    public void Loads_legacy_two_field_json_with_empty_plugin_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"DisabledPlugins\":[\"x\"],\"Hotkey\":\"Alt+Space\"}");
        var store = new SettingsStore(path);
        var s = store.Load();
        Assert.Contains("x", s.DisabledPlugins);
        Assert.NotNull(s.PluginSettings);
        Assert.Empty(s.PluginSettings);
    }
}
