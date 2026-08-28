using Pane.Abstractions;

namespace Pane.Core.Context;

public sealed class ConsoleLogger : IPluginLogger
{
    readonly string _id;
    public ConsoleLogger(string id) => _id = id;
    public void Info(string m) => Console.WriteLine($"[{_id}] INFO  {m}");
    public void Warn(string m) => Console.WriteLine($"[{_id}] WARN  {m}");
    public void Error(string m, Exception? ex = null) => Console.WriteLine($"[{_id}] ERROR {m} {ex}");
}

public sealed class PluginContext : IPluginContext
{
    public IPluginLogger Logger { get; }
    public string DataDirectory { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }
    public IFuzzyMatcher Matcher { get; }

    public PluginContext(string id, string dataDir, IReadOnlyDictionary<string, string> settings, IFuzzyMatcher matcher)
    {
        Logger = new ConsoleLogger(id);
        DataDirectory = dataDir;
        Directory.CreateDirectory(dataDir);
        Settings = settings;
        Matcher = matcher;
    }
}
