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
        store.Save(new PaneSettings(new HashSet<string> { "x" }, "Alt+Space"));
        var s = store.Load();
        Assert.Contains("x", s.DisabledPlugins);
        Assert.Equal("Alt+Space", s.Hotkey);
    }
}
