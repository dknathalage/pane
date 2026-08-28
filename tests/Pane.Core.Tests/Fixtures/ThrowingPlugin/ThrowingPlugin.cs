using System.Runtime.CompilerServices;
using Pane.Abstractions;

public sealed class ThrowingPlugin : IPlugin
{
    public PluginMetadata Metadata { get; } =
        new("throwing", "Throwing", "💥", "1.0", "throws on init", new[] { "boom" });

    public Task InitializeAsync(IPluginContext ctx) => throw new InvalidOperationException("boom");

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    { yield break; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
