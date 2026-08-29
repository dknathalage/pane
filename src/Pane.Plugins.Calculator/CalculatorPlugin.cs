using System.Runtime.CompilerServices;
using Pane.Abstractions;

namespace Pane.Plugins.Calculator;

public sealed class CalculatorPlugin : IPlugin
{
    public PluginMetadata Metadata { get; } = new(
        "calc", "Calculator", "🧮",
        "1.1.1", // x-release-please-version
        "Evaluate math expressions",
        new[] { "calc", "math", "=" }, "=", Priority: 10);

    public Task InitializeAsync(IPluginContext ctx) => Task.CompletedTask;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        if (Expression.TryEval(q.Terms, out var value))
        {
            var text = value.ToString("0.######");
            yield return new PaneResult($"= {text}", "Copy to clipboard", "🧮", 100,
                () => Copy(text), q.Terms);
        }
        await Task.CompletedTask;
    }

    static Task Copy(string text)
    {
        // Clipboard via host: for v1 write to a pipe the App layer exposes, or shell out.
        // macOS: pbcopy; Windows: clip; Linux: xclip/wl-copy.
        try
        {
            var psi = OperatingSystem.IsMacOS()
                ? new System.Diagnostics.ProcessStartInfo("pbcopy")
                : OperatingSystem.IsWindows()
                    ? new System.Diagnostics.ProcessStartInfo("clip")
                    : new System.Diagnostics.ProcessStartInfo("xclip", "-selection clipboard");
            psi.RedirectStandardInput = true; psi.UseShellExecute = false;
            var p = System.Diagnostics.Process.Start(psi)!;
            p.StandardInput.Write(text); p.StandardInput.Close();
        }
        catch { /* silent */ }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
