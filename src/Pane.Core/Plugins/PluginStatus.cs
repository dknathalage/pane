using Pane.Abstractions;

namespace Pane.Core.Plugins;

public enum PluginState { Enabled, Disabled, Errored }

public record PluginEntry(PluginMetadata Metadata, PluginState State, string? Error, IPlugin? Instance);
