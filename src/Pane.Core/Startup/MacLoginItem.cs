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

    public MacLoginItem(string? bundlePath, string plistPath)
    {
        _bundle = bundlePath;
        _plistPath = plistPath;
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
        Launchctl("bootout", Domain());
        Launchctl("bootstrap", GuiDomain(), _plistPath);
    }

    public void Disable()
    {
        Launchctl("bootout", Domain());
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

    static void Launchctl(params string[] args)
    {
        var info = new ProcessStartInfo("/bin/launchctl")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) info.ArgumentList.Add(a);

        using var process = Process.Start(info);
        process?.WaitForExit(10_000);
    }
}
