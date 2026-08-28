using System.Runtime.CompilerServices;
using Pane.Abstractions;

public sealed class TestPlugin : IPlugin
{
    public PluginMetadata Metadata { get; } =
        new("test", "Test", "🧪", "1.0", "fixture", new[] { "test" });

    public Task InitializeAsync(IPluginContext ctx) => Task.CompletedTask;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new PaneResult("Echo " + q.Terms, "", "🧪", 1.0, () => Task.CompletedTask);
        await Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
