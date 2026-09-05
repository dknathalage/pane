namespace Pane.Core.Contracts;

/// <summary>Host-provided facts a feature needs but should not discover for itself.</summary>
public sealed record FeatureContext(string DataDirectory, string HomeDirectory);

/// <summary>
/// Whether a feature can actually do its job on this machine — Files needs a
/// working OS search index, for instance. An unavailable feature is
/// skipped at dispatch without the user having to switch it off, and the reason is
/// shown in settings so the failure is legible rather than silent.
/// </summary>
public sealed record FeatureAvailability(bool IsAvailable, string? Reason)
{
    public static readonly FeatureAvailability Available = new(true, null);
    public static FeatureAvailability Unavailable(string reason) => new(false, reason);
}

/// <summary>
/// Everything the host needs from a feature. Features are passive: they never read
/// the settings file or decide whether they are enabled — the host resolves config
/// against <see cref="Settings"/> and pushes it in via <see cref="ApplyConfig"/>.
/// </summary>
public interface IPaneFeature
{
    FeatureDescriptor Descriptor { get; }

    /// <summary>The knobs this feature exposes, beyond the universal enabled/keyword/priority.</summary>
    IReadOnlyList<SettingDefinition> Settings { get; }

    Task InitializeAsync(FeatureContext ctx, CancellationToken ct);

    /// <summary>Called once after initialization and again after every settings change.</summary>
    void ApplyConfig(FeatureConfig config);

    /// <summary>
    /// Re-evaluated after every <see cref="ApplyConfig"/>, since the things a feature
    /// depends on (a search backend, a scan directory) are themselves configurable.
    /// </summary>
    FeatureAvailability CheckAvailability();

    IAsyncEnumerable<PaneResult> QueryAsync(PaneQuery q, CancellationToken ct);
}
