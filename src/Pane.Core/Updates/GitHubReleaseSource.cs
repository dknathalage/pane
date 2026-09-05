using System.Net.Http.Headers;
using System.Text.Json;

namespace Pane.Core.Updates;

/// <summary>
/// Reads the latest release from the GitHub REST API. The repo is public, so
/// there is deliberately no token handling anywhere in this class.
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource
{
    public const string DefaultRepo = "dknathalage/pane";

    readonly HttpClient _http;
    readonly string _repo;

    /// <param name="handler">
    /// Injected so tests supply a stub transport. Owned by the HttpClient.
    /// </param>
    public GitHubReleaseSource(HttpMessageHandler handler, string repo = DefaultRepo)
    {
        _repo = repo;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub rejects API requests that arrive without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pane", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public GitHubReleaseSource(string repo = DefaultRepo)
        : this(new HttpClientHandler(), repo) { }

    public async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{_repo}/releases/latest";

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();   // 403 rate limit and 5xx surface here

        var body = await response.Content.ReadAsStringAsync(ct);
        return Parse(body);
    }

    // A payload we cannot read is "no release", not an error: a stray non-version
    // tag must not turn into a failure the user has to interpret.
    internal static ReleaseInfo? Parse(string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var tag = Text(root, "tag_name");
            if (!AppVersion.TryParseTag(tag, out var version)) return null;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    var name = Text(a, "name");
                    var link = Text(a, "browser_download_url");
                    if (name is null || link is null) continue;

                    var size = a.TryGetProperty("size", out var s) &&
                               s.TryGetInt64(out var bytes) ? bytes : 0L;
                    assets.Add(new ReleaseAsset(name, link, size));
                }
            }

            return new ReleaseInfo(version, tag!, Text(root, "html_url") ?? "", assets);
        }
    }

    static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
