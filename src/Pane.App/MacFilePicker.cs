using System.Diagnostics;
using Pane.Core;

namespace Pane.App;

/// <summary>
/// macOS folder picker using osascript (no ObjC interop required).
/// Returns null when the user cancels (osascript exits with non-zero code).
/// </summary>
internal sealed class MacFilePicker : IFilePicker
{
    public async Task<string?> PickFolderAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            ArgumentList = { "-e", "POSIX path of (choose folder)" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        string stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            return null;   // User cancelled

        string path = stdout.Trim();
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
