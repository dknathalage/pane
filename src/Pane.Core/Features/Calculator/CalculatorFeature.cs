using System.Runtime.CompilerServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.Calculator;

public sealed class CalculatorFeature
{
    public FeatureDescriptor Descriptor { get; } = new(
        "calc", "Calculator", "🧮", "=", 10, new[] { "calc", "math", "=" });

    public Task InitializeAsync() => Task.CompletedTask;

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

}
