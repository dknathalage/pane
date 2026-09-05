using System.Text.RegularExpressions;

namespace Pane.Core.Updates;

/// <summary>
/// A three-component release version. Deliberately narrower than
/// <see cref="System.Version"/>: it orders numerically, parses the tag shapes
/// this repo has actually produced, and refuses anything it cannot order
/// correctly (notably pre-release suffixes) rather than guessing.
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch)
    : IComparable<AppVersion>
{
    public static readonly AppVersion Zero = new(0, 0, 0);

    // A bare triple only.
    static readonly Regex Bare = new(@"^(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    // An optional component prefix ("pane-", "plugins-") and/or a leading "v".
    // Anchored at both ends so "1.2.0-rc1" fails instead of parsing as 1.2.0.
    static readonly Regex Tag = new(
        @"^(?:[A-Za-z][A-Za-z0-9._]*-)?v?(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    public static bool TryParse(string? text, out AppVersion version) =>
        TryMatch(Bare, text, out version);

    public static bool TryParseTag(string? tag, out AppVersion version) =>
        TryMatch(Tag, tag, out version);

    static bool TryMatch(Regex pattern, string? text, out AppVersion version)
    {
        version = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = pattern.Match(text.Trim());
        if (!m.Success) return false;

        // int.Parse can still overflow on an absurd tag; treat that as unparseable.
        if (!int.TryParse(m.Groups[1].Value, out var major) ||
            !int.TryParse(m.Groups[2].Value, out var minor) ||
            !int.TryParse(m.Groups[3].Value, out var patch)) return false;

        version = new AppVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        return Patch.CompareTo(other.Patch);
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
