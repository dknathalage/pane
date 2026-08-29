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
        // Load the main assembly from bytes rather than LoadFromAssemblyPath.
        // The runtime memory-maps and dedups assembly images BY PATH: after an in-place
        // update rewrites plugins/<id>/TestPlugin.dll, a fresh ALC + LoadFromAssemblyPath on
        // that same path still returns the OLD (pre-update) image while the previous ALC is
        // mid-unload — so the update appears to do nothing. Reading the current bytes and
        // using LoadFromStream sidesteps the path cache and always loads what is on disk now.
        using var fs = new FileStream(Path.GetFullPath(dllPath), FileMode.Open, FileAccess.Read, FileShare.Read);
        var asm = ctx.LoadFromStream(fs);
        var type = asm.GetTypes()
            .Single(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
        var plugin = (IPlugin)Activator.CreateInstance(type)!;
        return (plugin, ctx);
    }
}
