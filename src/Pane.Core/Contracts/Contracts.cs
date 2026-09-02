namespace Pane.Core.Contracts;

public record PaneQuery(string RawText, string? Keyword, string Terms);

public record PaneResult(
    string Title,
    string Subtitle,
    string Icon,
    double BaseScore,
    Func<Task> Activate,
    string? SearchText = null);
