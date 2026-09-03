using System.Runtime.CompilerServices;
using Pane.Core.Contracts;

namespace Pane.Ui.Tests;

/// <summary>A feature declaring one of every kind of setting, so rendering its
/// pane exercises every branch of the generated form.</summary>
public sealed class DemoFeature : IPaneFeature
{
    public DemoFeature(string id = "demo", string name = "Demo", string? keyword = "@")
        => Descriptor = new FeatureDescriptor(id, name, "🧪", keyword, 5, new[] { id });

    public FeatureDescriptor Descriptor { get; }

    public IReadOnlyList<SettingDefinition> Settings { get; } = new SettingDefinition[]
    {
        new BoolSetting("deep", "Deep search", true),
        new IntSetting("maxResults", "Max results", 25, 1, 100),
        new TextSetting("shell", "Shell", "/bin/bash"),
        new PathsSetting("dirs", "Folders", new[] { "~/one" }),
    };

    public FeatureAvailability Availability { get; set; } = FeatureAvailability.Available;

    public Task InitializeAsync(FeatureContext ctx, CancellationToken ct) => Task.CompletedTask;
    public void ApplyConfig(FeatureConfig config) { }
    public FeatureAvailability CheckAvailability() => Availability;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        yield break;
    }
}
