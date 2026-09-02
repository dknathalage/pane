using Pane.Core.Contracts;

namespace Pane.Core.Query;

public record ScoredResult(PaneResult Result, double Score, IReadOnlyList<int> TitleHighlights, string PluginId);
