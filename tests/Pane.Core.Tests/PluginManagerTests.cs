using Pane.Core.Plugins;
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

    static void CopyPlugin(string pluginsRoot, string fixtureName, string dllName)
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
        CopyPlugin(root, "TestPlugin", "TestPlugin.dll");
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
        CopyPlugin(root, "ThrowingPlugin", "ThrowingPlugin.dll");
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
        CopyPlugin(root, "TestPlugin", "TestPlugin.dll");
        var mgr = new PluginManager(dataRoot: root);
        await mgr.LoadAllAsync(root);

        await mgr.DisableAsync("test");
        Assert.Equal(PluginState.Disabled, mgr.List().Single().State);
        Assert.Empty(mgr.Active());

        await mgr.EnableAsync("test");
        Assert.Equal(PluginState.Enabled, mgr.List().Single().State);
        Assert.Single(mgr.Active());
    }
}
