using System.Reflection;

namespace Pane.Core.Updates;

/// <summary>
/// The running app's own version.
///
/// Reads the ENTRY assembly specifically: Pane.Core sets
/// GenerateAssemblyInfo=false, so this assembly carries no version attribute of
/// its own and reading typeof(AppVersionSource).Assembly would always yield
/// nothing. Pane.App leaves generation on, so `-p:Version=` reaches it there.
/// </summary>
public static class AppVersionSource
{
    static readonly Lazy<AppVersion> Lazy = new(() => FromInformationalVersion(
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion));

    public static AppVersion Current => Lazy.Value;

    /// <summary>
    /// Parses an AssemblyInformationalVersion, dropping the "+&lt;sha&gt;" build
    /// metadata SourceLink appends. Returns Zero when there is nothing usable.
    /// </summary>
    public static AppVersion FromInformationalVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AppVersion.Zero;

        var plus = raw.IndexOf('+');
        var text = plus >= 0 ? raw[..plus] : raw;

        return AppVersion.TryParse(text, out var v) ? v : AppVersion.Zero;
    }
}
