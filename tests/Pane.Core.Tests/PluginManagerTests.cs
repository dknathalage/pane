using Pane.Core.Plugins;
using Pane.Core.Settings;
using Xunit;

public class PluginManagerTests
{
    static string FixtureDir(string name)
    {
        var config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name, "bin", config, "net10.0"));
    }

    static string NewPluginsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pane-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static void CopyPlugin(string pluginsRoot, string fixtureName)
    {
        var dst = Path.Combine(pluginsRoot, fixtureName);
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(FixtureDir(fixtureName)))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
    }

    [Fact]
    public async Task Loads_enabled_plugin_and_lists_it()
    {
        var root = NewPluginsRoot();
        CopyPlugin(root, "TestPlugin");
        var mgr = new PluginManager(dataRoot: root);

        await mgr.LoadAllAsync(root);

        var entry = Assert.Single(mgr.List());
        Assert.Equal(PluginState.Enabled, entry.State);
        Assert.Equal("test", entry.Metadata.Id);
    }

    [Fact]
    public async Task Faulty_plugin_is_marked_errored_not_thrown()
    {
        var root = NewPluginsRoot();
        CopyPlugin(root, "ThrowingPlugin");
        var mgr = new PluginManager(dataRoot: root);

        await mgr.LoadAllAsync(root);   // must not throw

        var entry = Assert.Single(mgr.List());
        Assert.Equal(PluginState.Errored, entry.State);
        Assert.Contains("boom", entry.Error);
        Assert.Empty(mgr.Active());
    }

    [Fact]
    public async Task Disable_then_enable_toggles_state()
    {
        var root = NewPluginsRoot();
        CopyPlugin(root, "TestPlugin");
        var mgr = new PluginManager(dataRoot: root);
        await mgr.LoadAllAsync(root);

        await mgr.DisableAsync("test");
        Assert.Equal(PluginState.Disabled, mgr.List().Single().State);
        Assert.Empty(mgr.Active());

        await mgr.EnableAsync("test");
        Assert.Equal(PluginState.Enabled, mgr.List().Single().State);
        Assert.Single(mgr.Active());
    }

    [Fact]
    public async Task Uninstall_clears_disabled_state_from_settings()
    {
        var root = NewPluginsRoot();
        var settingsPath = Path.Combine(Path.GetTempPath(), "pane-test-settings-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new SettingsStore(settingsPath);

        CopyPlugin(root, "TestPlugin");
        var mgr = new PluginManager(dataRoot: root, store: store);
        await mgr.LoadAllAsync(root);

        await mgr.DisableAsync("test");
        Assert.Contains("test", store.Load().DisabledPlugins);

        await mgr.UninstallAsync("test");
        Assert.DoesNotContain("test", store.Load().DisabledPlugins);
    }

    static void CopyPluginIntoDir(string pluginsRoot, string fixtureName, string destSubfolder)
    {
        var dst = Path.Combine(pluginsRoot, destSubfolder);
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(FixtureDir(fixtureName)))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
    }

    [Fact]
    public async Task Duplicate_id_does_not_corrupt_first_loaded_plugin()
    {
        var root = NewPluginsRoot();
        // Copy the same TestPlugin fixture into two different subfolders.
        CopyPluginIntoDir(root, "TestPlugin", "a");
        CopyPluginIntoDir(root, "TestPlugin", "b");

        var mgr = new PluginManager(dataRoot: root);
        await mgr.LoadAllAsync(root); // must not throw

        var list = mgr.List();
        Assert.Single(list);
        Assert.Equal("test", list[0].Metadata.Id);
        Assert.Equal(PluginState.Enabled, list[0].State);
        Assert.Single(mgr.Active());
    }
}
