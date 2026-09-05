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
            ["alpha"] = new JsonObject { ["dirs"] = new JsonArray("/a", "/b") },
        }));

        var dirs = store.Load().Features["alpha"]["dirs"]!.AsArray();
        Assert.Equal(new[] { "/a", "/b" }, dirs.Select(d => (string)d!));
    }

    [Fact]
    public void Load_migrates_legacy_disabledPlugins_to_feature_enabled()
    {
        var path = TempPath();
        File.WriteAllText(path, """
            { "disabledPlugins": ["alpha"], "hotkey": "Alt+Space" }
            """);
        var s = new SettingsStore(path).Load();
        Assert.False((bool)s.Features["alpha"]["enabled"]!);
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

    [Fact]
    public void Auto_check_updates_defaults_to_on()
    {
        Assert.True(new SettingsStore(TempPath()).Load().AutoCheckUpdates);
    }

    [Fact]
    public void Auto_check_updates_roundtrips_when_turned_off()
    {
        var path = TempPath();
        var store = new SettingsStore(path);

        store.Save(new PaneSettings(SettingsStore.DefaultHotkey, new(), AutoCheckUpdates: false));

        Assert.False(store.Load().AutoCheckUpdates);
        File.Delete(path);
    }

    [Fact]
    public void An_older_settings_file_without_the_key_loads_as_on()
    {
        // Upgrading must not silently disable update checks, nor discard the
        // rest of an existing document.
        var path = TempPath();
        File.WriteAllText(path, """
            { "hotkey": "Ctrl+Space", "features": { "files": { "maxResults": 9 } } }
            """);

        var s = new SettingsStore(path).Load();

        Assert.True(s.AutoCheckUpdates);
        Assert.Equal("Ctrl+Space", s.Hotkey);
        Assert.Equal(9, (int)s.Features["files"]["maxResults"]!);
        File.Delete(path);
    }
}
