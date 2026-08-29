using System.Runtime.CompilerServices;
using Pane.Abstractions;

public sealed class TestPlugin : IPlugin
{
    public PluginMetadata Metadata { get; } =
        new("test", "Test", "🧪", "2.0", "fixture v2", new[] { "test" });

    public Task InitializeAsync(IPluginContext ctx) => Task.CompletedTask;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new PaneResult("Echo2 " + q.Terms, "", "🧪", 1.0, () => Task.CompletedTask);
        await Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
