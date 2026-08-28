namespace Pane.Abstractions;

public record PluginMetadata(
    string Id,
    string Name,
    string Icon,
    string Version,
    string Description,
    IReadOnlyList<string> Keywords,
    string? Keyword = null,
    int Priority = 0);

public record PaneQuery(string RawText, string? Keyword, string Terms);

public record PaneResult(
    string Title,
    string Subtitle,
    string Icon,
    double BaseScore,
    Func<Task> Activate,
    string? SearchText = null);
