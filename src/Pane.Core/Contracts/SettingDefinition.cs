namespace Pane.Core.Contracts;

/// <summary>
/// One knob a feature exposes. The feature declares the schema; the host persists
/// the values and renders the form, so adding a setting never touches the UI.
/// </summary>
public abstract record SettingDefinition(string Key, string Label, string? Help = null);

public sealed record BoolSetting(string Key, string Label, bool Default, string? Help = null)
    : SettingDefinition(Key, Label, Help);

public sealed record IntSetting(string Key, string Label, int Default, int Min, int Max, string? Help = null)
    : SettingDefinition(Key, Label, Help);

public sealed record TextSetting(string Key, string Label, string Default, string? Help = null)
    : SettingDefinition(Key, Label, Help);

/// <summary>A list of filesystem paths, edited one per line.</summary>
public sealed record PathsSetting(string Key, string Label, IReadOnlyList<string> Default, string? Help = null)
    : SettingDefinition(Key, Label, Help);
