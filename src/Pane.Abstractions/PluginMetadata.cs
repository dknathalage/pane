namespace Pane.Abstractions;

public enum PluginSettingType { Text, Boolean, Number, Choice }

public record PluginSettingSpec(
    string Key,
    string Label,
    PluginSettingType Type,
    string? Default = null,
    string? Description = null,
    IReadOnlyList<string>? Choices = null);

public record PluginMetadata(
    string Id,
    string Name,
    string Icon,
    string Version,
    string Description,
    IReadOnlyList<string> Keywords,
    string? Keyword = null,
    int Priority = 0,
    IReadOnlyList<PluginSettingSpec>? Settings = null);

public record PaneQuery(string RawText, string? Keyword, string Terms);

public record PaneResult(
    string Title,
    string Subtitle,
    string Icon,
    double BaseScore,
    Func<Task> Activate,
    string? SearchText = null);
