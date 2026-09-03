using System.Text.Json.Nodes;
using Pane.Core.Settings;
using Xunit;

public class SettingsStoreTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var s = new SettingsStore(TempPath()).Load();
        Assert.Empty(s.Features);
        Assert.False(string.IsNullOrEmpty(s.Hotkey));
    }

    [Fact]
    public void Save_then_load_roundtrips_hotkey()
    {
        var path = TempPath();
        var store = new SettingsStore(path);
        store.Save(new PaneSettings("Ctrl+Space", new()));
        Assert.Equal("Ctrl+Space", store.Load().Hotkey);
    }

    [Fact]
    public void Save_then_load_roundtrips_feature_values()
    {
        var path = TempPath();
        var store = new SettingsStore(path);
        store.Save(new PaneSettings(SettingsStore.DefaultHotkey, new()
        {
            ["files"] = new JsonObject { ["maxResults"] = 7, ["keyword"] = "f", ["enabled"] = false },
        }));

        var files = store.Load().Features["files"];
        Assert.Equal(7, (int)files["maxResults"]!);
        Assert.Equal("f", (string)files["keyword"]!);
        Assert.False((bool)files["enabled"]!);
    }

    [Fact]
    public void Save_then_load_roundtrips_feature_path_lists()
    {
        var path = TempPath();
        var store = new SettingsStore(path);
        store.Save(new PaneSettings(SettingsStore.DefaultHotkey, new()
        {
            ["scripts"] = new JsonObject { ["dirs"] = new JsonArray("/a", "/b") },
        }));

        var dirs = store.Load().Features["scripts"]["dirs"]!.AsArray();
        Assert.Equal(new[] { "/a", "/b" }, dirs.Select(d => (string)d!));
    }

    [Fact]
    public void Load_migrates_legacy_disabledPlugins_to_feature_enabled()
    {
        var path = TempPath();
        File.WriteAllText(path, """
            { "disabledPlugins": ["vscode"], "hotkey": "Alt+Space" }
            """);
        var s = new SettingsStore(path).Load();
        Assert.False((bool)s.Features["vscode"]["enabled"]!);
        File.Delete(path);
    }

    [Fact]
    public void Load_ignores_legacy_pluginSettings_key()
    {
        var path = TempPath();
        File.WriteAllText(path, """
            { "hotkey": "Alt+Space", "pluginSettings": { "files": { "hidden": "true" } } }
            """);
        var s = new SettingsStore(path).Load();
        Assert.Equal("Alt+Space", s.Hotkey);
        Assert.Empty(s.Features);
        File.Delete(path);
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_corrupt()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ not json");
        var s = new SettingsStore(path).Load();
        Assert.Equal(SettingsStore.DefaultHotkey, s.Hotkey);
        Assert.Empty(s.Features);
        File.Delete(path);
    }
}
