using System.Xml.Linq;

namespace Pane.Core.Startup;

/// <summary>
/// The per-user LaunchAgent that starts Pane at login. Pure string work, kept
/// apart from launchctl so its content is unit-tested without side effects.
///
/// The label must stay in step with install.sh and build/uninstall-startup.sh.
/// </summary>
public static class LaunchAgentPlist
{
    public const string Label = "com.pane.launcher";

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{Label}.plist");

    public static string Build(string execPath) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{execPath}</string>
            <string>--startup</string>
          </array>
          <key>RunAtLoad</key><true/>
          <key>KeepAlive</key><false/>
          <key>ProcessType</key><string>Interactive</string>
          <!-- Without this, launchd kills every process in Pane's process group
               (man 5 launchd.plist) the moment this job dies — including the
               updater's detached swap helper, spawned as an ordinary child and
               mid-sleep waiting for Pane to exit. That would silently drop
               every update attempted from a login-item launch. -->
          <key>AbandonProcessGroup</key><true/>
        </dict>
        </plist>
        """;

    /// <summary>
    /// The executable a plist launches, or null if unreadable. Used to tell a
    /// registration for THIS bundle apart from one an older install left behind.
    /// </summary>
    public static string? ReadProgramPath(string plistXml)
    {
        try
        {
            var dict = XDocument.Parse(plistXml).Descendants("dict").FirstOrDefault();
            var key = dict?.Elements("key")
                .FirstOrDefault(e => e.Value == "ProgramArguments");

            // ElementsAfterSelf(), not NextNode: in a plist the value is the next
            // ELEMENT sibling, and a normally-formatted plist puts a whitespace
            // text node between </key> and the value. NextNode would return that
            // text node and silently read nothing.
            var value = key?.ElementsAfterSelf().FirstOrDefault();
            return value?.Elements("string").FirstOrDefault()?.Value;
        }
        catch { return null; }
    }
}
