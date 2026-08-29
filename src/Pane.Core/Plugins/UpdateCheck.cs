namespace Pane.Core.Plugins;

public record UpdateCheck(
    bool Available,
    string InstalledVersion,
    string? RemoteVersion,
    string? Error);

public static class PluginVersion
{
    public static bool IsUpdateAvailable(string installed, string remote)
    {
        if (System.Version.TryParse(installed, out var i) &&
            System.Version.TryParse(remote, out var r))
            return r > i;
        return !string.Equals(installed, remote, StringComparison.Ordinal);
    }
}
