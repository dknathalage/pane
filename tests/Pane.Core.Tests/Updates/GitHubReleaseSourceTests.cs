using System.Net;
using Pane.Core.Updates;
using Xunit;

public class GitHubReleaseSourceTests
{
    // A stub transport: no test ever touches the network.
    sealed class StubHandler : HttpMessageHandler
    {
        readonly HttpStatusCode _status;
        readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
            => (_body, _status) = (body, status);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body),
            });
        }
    }

    const string Payload = """
        {
          "tag_name": "pane-v1.3.0",
          "html_url": "https://github.com/dknathalage/pane/releases/tag/pane-v1.3.0",
          "assets": [
            { "name": "Pane-osx-arm64.zip",
              "browser_download_url": "https://example.test/arm64.zip",
              "size": 12345 },
            { "name": "Pane-osx-x64.zip",
              "browser_download_url": "https://example.test/x64.zip",
              "size": 23456 }
          ]
        }
        """;

    static GitHubReleaseSource Source(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(new StubHandler(body, status));

    [Fact]
    public async Task A_release_payload_is_parsed_into_version_tag_and_url()
    {
        var release = await Source(Payload).FetchLatestAsync(CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal(new AppVersion(1, 3, 0), release!.Version);
        Assert.Equal("pane-v1.3.0", release.Tag);
        Assert.Contains("pane-v1.3.0", release.HtmlUrl);
    }

    [Fact]
    public async Task Assets_are_parsed_with_their_download_url_and_size()
    {
        var release = await Source(Payload).FetchLatestAsync(CancellationToken.None);

        var arm = release!.FindAsset("Pane-osx-arm64.zip");
        Assert.NotNull(arm);
        Assert.Equal("https://example.test/arm64.zip", arm!.DownloadUrl);
        Assert.Equal(12345, arm.Size);
    }

    [Fact]
    public async Task FindAsset_is_case_insensitive_and_returns_null_when_absent()
    {
        var release = await Source(Payload).FetchLatestAsync(CancellationToken.None);

        Assert.NotNull(release!.FindAsset("pane-OSX-arm64.ZIP"));
        Assert.Null(release.FindAsset("Pane-win-x64.zip"));
    }

    [Fact]
    public async Task GitHub_requires_a_user_agent_so_we_always_send_one()
    {
        var handler = new StubHandler(Payload);
        await new GitHubReleaseSource(handler).FetchLatestAsync(CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.NotEmpty(handler.LastRequest!.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task The_repo_is_addressed_by_the_releases_latest_endpoint()
    {
        var handler = new StubHandler(Payload);
        await new GitHubReleaseSource(handler, "someone/fork").FetchLatestAsync(CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/someone/fork/releases/latest",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task An_unorderable_tag_is_no_release_rather_than_a_failure()
    {
        // The repo's own history has plugins-v* releases with no Pane asset, and
        // a stray "nightly" tag must not become an error the user sees.
        const string body = """
            { "tag_name": "nightly", "html_url": "https://x.test", "assets": [] }
            """;

        Assert.Null(await Source(body).FetchLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_release_with_no_assets_still_parses()
    {
        // Exactly today's plugins-v1.2.0 situation once tags are renamed.
        const string body = """
            { "tag_name": "pane-v1.2.0", "html_url": "https://x.test", "assets": [] }
            """;

        var release = await Source(body).FetchLatestAsync(CancellationToken.None);

        Assert.NotNull(release);
        Assert.Empty(release!.Assets);
    }

    [Fact]
    public async Task A_server_error_is_raised_not_swallowed()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Source("{}", HttpStatusCode.InternalServerError)
                .FetchLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rate_limiting_is_raised_so_it_can_be_named_to_the_user()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Source("{}", HttpStatusCode.Forbidden)
                .FetchLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_body_is_no_release_rather_than_a_crash()
    {
        Assert.Null(await Source("{ not json").FetchLatestAsync(CancellationToken.None));
    }
}
