using System.Reflection;
using System.Runtime.Loader;

namespace Pane.Core.Loading;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginDllPath) : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(pluginDllPath);

    protected override Assembly? Load(AssemblyName name)
    {
        // Shared contract + runtime types resolve from the default context.
        if (name.Name is "Pane.Abstractions") return null;
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
