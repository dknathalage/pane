using System.Diagnostics;

namespace Pane.Core.Startup;

/// <summary>
/// Owns ~/Library/LaunchAgents/com.pane.launcher.plist — the same agent
/// install.sh writes, so the two never fight.
/// </summary>
public sealed class MacLoginItem : ILoginItem
{
    readonly string? _bundle;
    readonly string _plistPath;
    readonly Func<string[], (int ExitCode, string Error)> _runLaunchctl;

    public MacLoginItem(string? bundlePath, string plistPath)
        : this(bundlePath, plistPath, RunLaunchctl) { }

    /// <summary>Test seam: swap in a fake launchctl runner without spawning one.</summary>
    internal MacLoginItem(string? bundlePath, string plistPath,
                          Func<string[], (int ExitCode, string Error)> runLaunchctl)
    {
        _bundle = bundlePath;
        _plistPath = plistPath;
        _runLaunchctl = runLaunchctl;
    }

    public MacLoginItem()
        : this(Pane.Core.Updates.BundleLayout.CurrentBundle(), LaunchAgentPlist.DefaultPath()) { }

    /// <summary>The executable a correct agent for this install would launch.</summary>
    public string? ExpectedExecutable => _bundle is null
        ? null
        : Path.Combine(_bundle, "Contents", "MacOS", Pane.Core.Updates.BundleLayout.ExecutableName);

    public bool CanManage => OperatingSystem.IsMacOS() && _bundle is not null;

    public string? UnavailableReason => CanManage
        ? null
        : "Pane is not running from an installed .app bundle, so it cannot start at login. "
          + "Install it with install.sh first.";

    // Comparing the path matters: a plist left by an older install elsewhere
    // must not report "on" while launching a different copy at login.
    public bool IsEnabled
    {
        get
        {
            if (ExpectedExecutable is null || !File.Exists(_plistPath)) return false;
            try
            {
                var program = LaunchAgentPlist.ReadProgramPath(File.ReadAllText(_plistPath));
                return string.Equals(program, ExpectedExecutable, StringComparison.Ordinal);
            }
            catch { return false; }
        }
    }

    public void Enable()
    {
        if (ExpectedExecutable is null) throw new InvalidOperationException(UnavailableReason);

        Directory.CreateDirectory(Path.GetDirectoryName(_plistPath)!);
        File.WriteAllText(_plistPath, LaunchAgentPlist.Build(ExpectedExecutable));

        // Tolerated: bootout fails when nothing is registered, which is the
        // normal case. It exists so re-enabling over a stale agent works.
        _runLaunchctl(["bootout", Domain()]);

        var (exitCode, error) = _runLaunchctl(["bootstrap", GuiDomain(), _plistPath]);
        if (exitCode != 0)
        {
            // IsEnabled only reads the plist we just wrote, so leaving it in
            // place here would report "on" while launchd never actually has
            // the job — delete it so the checkbox reflects reality.
            try { File.Delete(_plistPath); } catch { }
            throw new InvalidOperationException(
                $"launchctl bootstrap failed (exit {exitCode}): {error}".TrimEnd());
        }
    }

    public void Disable()
    {
        _runLaunchctl(["bootout", Domain()]);
        try { File.Delete(_plistPath); } catch { /* already gone is success */ }
    }

    static string GuiDomain() => $"gui/{GetUid()}";
    static string Domain() => $"gui/{GetUid()}/{LaunchAgentPlist.Label}";

    static int GetUid()
    {
        // getuid() via the process is enough here and avoids a P/Invoke in Core.
        var id = Process.Start(new ProcessStartInfo("/usr/bin/id", "-u")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
        id!.WaitForExit();
        return int.TryParse(id.StandardOutput.ReadToEnd().Trim(), out var uid) ? uid : 0;
    }

    static (int ExitCode, string Error) RunLaunchctl(string[] args)
    {
        var info = new ProcessStartInfo("/bin/launchctl")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) info.ArgumentList.Add(a);

        using var process = Process.Start(info);
        if (process is null) return (-1, "could not start /bin/launchctl");

        // Read stderr asynchronously so a chatty failure can't deadlock this
        // against WaitForExit (a full pipe would otherwise block the process
        // from exiting while we block waiting for it to exit).
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (-1, "launchctl timed out after 10s");
        }

        var error = errorTask.Wait(1_000) ? errorTask.Result : "";
        return (process.ExitCode, error.Trim());
    }
}
