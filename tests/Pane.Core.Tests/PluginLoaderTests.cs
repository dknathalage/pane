using Pane.Abstractions;
using Pane.Core.Loading;
using Xunit;

public class PluginLoaderTests
{
    static string TestPluginDll()
    {
        // Resolve the fixture build output relative to the test assembly.
        var config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        var root = AppContext.BaseDirectory;
        // tests/Pane.Core.Tests/bin/<config>/net10.0 -> fixture output
        var path = Path.GetFullPath(Path.Combine(
            root, "..", "..", "..", "Fixtures", "TestPlugin",
            "bin", config, "net10.0", "TestPlugin.dll"));
        return path;
    }

    [Fact]
    public void Loads_plugin_and_reads_metadata()
    {
        var (plugin, ctx) = PluginLoader.CreateContext(TestPluginDll());
        try
        {
            Assert.Equal("test", plugin.Metadata.Id);
            // Abstractions type is shared: the loaded IPlugin is our IPlugin.
            Assert.IsAssignableFrom<IPlugin>(plugin);
        }
        finally { ctx.Unload(); }
    }

    [Fact]
    public void Load_returns_plugin_instance()
    {
        var plugin = PluginLoader.Load(TestPluginDll());
        Assert.Equal("test", plugin.Metadata.Id);
    }
}
