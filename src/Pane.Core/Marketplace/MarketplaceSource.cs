using System.Text.RegularExpressions;

namespace Pane.Core.Marketplace;

public record ResolvedSource(bool IsLocal, string Location);

public static class MarketplaceSource
{
    static readonly Regex GithubRepo = new(
        @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ResolvedSource Resolve(string source)
    {
        source = source.Trim();

        // Existing local file (dev): read it directly.
        if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase) && File.Exists(source))
            return new ResolvedSource(true, source);

        // A GitHub repo URL → the repo's raw marketplace.json at HEAD.
        var m = GithubRepo.Match(source);
        if (m.Success)
            return new ResolvedSource(false,
                $"https://raw.githubusercontent.com/{m.Groups["owner"].Value}/{m.Groups["repo"].Value}/HEAD/marketplace.json");

        // Anything else (already a .json URL, or another raw URL) is used as-is.
        return new ResolvedSource(false, source);
    }
}
