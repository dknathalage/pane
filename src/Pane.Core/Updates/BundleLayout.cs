using System.Xml.Linq;

namespace Pane.Core.Updates;

/// <summary>
/// Pure reasoning about macOS .app bundles: which one are we running from, and
/// is a freshly downloaded one safe to install? Kept free of I/O side effects
/// beyond reading, so every branch is unit-tested against fixture directories.
/// </summary>
public static class BundleLayout
{
    public const string ExecutableName = "Pane.App";
    const string VersionKey = "CFBundleShortVersionString";

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> to the enclosing ".app",
    /// or null when the process is not running from a bundle (a dev `dotnet run`).
    /// </summary>
    public static string? FindEnclosingBundle(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory)) return null;

        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>The bundle we are currently running from, or null.</summary>
    public static string? CurrentBundle() => FindEnclosingBundle(AppContext.BaseDirectory);

    /// <summary>Reads CFBundleShortVersionString, or null if absent/unreadable.</summary>
    public static AppVersion? ReadBundleVersion(string bundlePath)
    {
        var plist = Path.Combine(bundlePath, "Contents", "Info.plist");
        if (!File.Exists(plist)) return null;

        try
        {
            // In a plist the value is the next ELEMENT sibling of its <key>.
            // ElementsAfterSelf() skips the whitespace text node that a
            // normally-formatted plist puts between </key> and <string>;
            // NextNode would return that text node and read nothing.
            var dict = XDocument.Load(plist).Descendants("dict").FirstOrDefault();
            var key = dict?.Elements("key").FirstOrDefault(e => e.Value == VersionKey);
            var value = key?.ElementsAfterSelf().FirstOrDefault()?.Value;

            return AppVersion.TryParse(value, out var v) ? v : null;
        }
        catch { return null; } // Unreadable plist → null is the intended contract.
    }

    /// <summary>
    /// Null when the bundle is safe to install, otherwise the reason it is not.
    /// Called before anything installed is touched.
    /// </summary>
    public static string? Validate(string bundlePath, AppVersion mustExceed)
    {
        if (!Directory.Exists(bundlePath))
            return "The downloaded archive did not contain Pane.app.";

        var exe = Path.Combine(bundlePath, "Contents", "MacOS", ExecutableName);
        if (!File.Exists(exe))
            return $"The downloaded Pane.app has no Contents/MacOS/{ExecutableName}.";

        if (ReadBundleVersion(bundlePath) is not { } version)
            return "The downloaded Pane.app has no readable version.";

        if (version <= mustExceed)
            return $"The downloaded Pane.app is version {version}, which is not newer.";

        return null;
    }
}
