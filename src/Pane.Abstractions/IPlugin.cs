namespace Pane.Abstractions;

public interface IPlugin : IAsyncDisposable
{
    PluginMetadata Metadata { get; }
    Task InitializeAsync(IPluginContext ctx);
    IAsyncEnumerable<PaneResult> QueryAsync(PaneQuery q, CancellationToken ct);
}

public interface IPluginContext
{
    IPluginLogger Logger { get; }
    string DataDirectory { get; }
    IReadOnlyDictionary<string, string> Settings { get; }
    IFuzzyMatcher Matcher { get; }
}

public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
