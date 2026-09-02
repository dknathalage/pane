namespace Pane.Abstractions;

public record FeatureDescriptor(
    string Id,
    string Name,
    string Icon,
    string? Keyword,
    int Priority,
    IReadOnlyList<string> Keywords);
