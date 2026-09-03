using System.Runtime.CompilerServices;
using Pane.Core.Contracts;

namespace Pane.Core.Features.Calculator;

public sealed class CalculatorFeature : IPaneFeature
{
    const string DecimalsKey = "decimals";

    public FeatureDescriptor Descriptor { get; } = new(
        "calc", "Calculator", "🧮", "=", 10, new[] { "calc", "math", "=" });

    public IReadOnlyList<SettingDefinition> Settings { get; } = new SettingDefinition[]
    {
        new IntSetting(DecimalsKey, "Decimal places", 6, 0, 15),
    };

    int _decimals = 6;

    public Task InitializeAsync(FeatureContext ctx, CancellationToken ct) => Task.CompletedTask;

    public void ApplyConfig(FeatureConfig config) => _decimals = config.GetInt(DecimalsKey);

    public FeatureAvailability CheckAvailability() => FeatureAvailability.Available;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        if (Expression.TryEval(q.Terms, out var value))
        {
            var text = value.ToString("0." + new string('#', _decimals));
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
