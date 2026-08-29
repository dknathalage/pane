namespace Pane.Core.Plugins;

public sealed class PluginFetchException : Exception
{
    public PluginFetchException(string message, Exception? inner = null)
        : base(message, inner) { }
}
