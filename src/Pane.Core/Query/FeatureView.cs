using Pane.Core.Contracts;

namespace Pane.Core.Query;

/// <summary>
/// A feature as the settings UI sees it: what it is, what it can be configured
/// with, what it is configured to right now, and whether it can run here.
/// </summary>
public sealed record FeatureView(
    FeatureDescriptor Descriptor,
    IReadOnlyList<SettingDefinition> Settings,
    FeatureConfig Config,
    FeatureAvailability Availability)
{
    public string Id => Descriptor.Id;

    /// <summary>Enabled by the user AND able to run here.</summary>
    public bool IsActive => Config.Enabled && Availability.IsAvailable;
}
