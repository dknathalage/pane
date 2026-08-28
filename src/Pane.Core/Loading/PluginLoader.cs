using Pane.Abstractions;

namespace Pane.Core.Loading;

public static class PluginLoader
{
    // Fire-and-forget load: returns the plugin without a handle to its load context,
    // so the caller cannot unload it. Use CreateContext when you need to unload.
    public static IPlugin Load(string dllPath) => CreateContext(dllPath).plugin;

    public static (IPlugin plugin, PluginLoadContext ctx) CreateContext(string dllPath)
    {
        var ctx = new PluginLoadContext(dllPath);
        var asm = ctx.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
        var type = asm.GetTypes()
            .Single(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
        var plugin = (IPlugin)Activator.CreateInstance(type)!;
        return (plugin, ctx);
    }
}
