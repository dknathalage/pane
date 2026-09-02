using System.Diagnostics;

namespace Pane.Core.Features.Files;

/// <summary>Shared helper: run an index CLI, read newline-separated paths.</summary>
static class ProcessSearch
{
    public static IReadOnlyList<FileHit> RunLines(
        string file, IEnumerable<string> args, int max, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var hits = new List<FileHit>(max);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return hits;

            string? line;
            while (hits.Count < max && (line = p.StandardOutput.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Length == 0) continue;
                hits.Add(new FileHit(line, Directory.Exists(line)));
            }
            try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* tool missing / failed: contribute nothing */ }
        return hits;
    }
}
