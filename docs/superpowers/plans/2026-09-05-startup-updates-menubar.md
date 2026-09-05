# Startup, Self-Update, and Menu Bar Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Pane a real version, an auto-checking GitHub self-updater with a one-click install, an in-app "start at login" toggle, and a proper menu bar icon.

**Architecture:** All decision logic lives in `Pane.Core` (which already hosts macOS-specific types like `MacAppIndexer`), behind two seams — `IReleaseSource` for "what release is out there" and `IUpdateInstaller` for "put it on disk" — so everything is unit-testable with fakes. Self-replacement is done by a detached shell helper *after* the app exits, because a running self-contained .NET bundle cannot safely overwrite its own dylibs. Only ObjC interop stays in `Pane.App`.

**Tech Stack:** .NET 10 (`net10.0`), C#, xunit 2.9.3, bUnit 1.40.0, Blazor/Photino, `launchctl`, `ditto`, GitHub Releases API.

**Spec:** `docs/superpowers/specs/2026-09-05-startup-updates-menubar-design.md`

## Global Constraints

- Target framework is `net10.0`; `Nullable` and `ImplicitUsings` are enabled in every project.
- `Pane.Core` has **zero** NuGet package references. Use only the BCL (`HttpClient`, `System.Text.Json`, `System.Xml.Linq`). Do not add packages to it.
- `Pane.Core.csproj` sets `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>`. Version attributes exist only on the **entry assembly** (`Pane.App`).
- macOS-only feature. Guard platform calls with `OperatingSystem.IsMacOS()`; on other platforms the features report themselves unavailable rather than throwing.
- GitHub repo default is `dknathalage/pane`; it is public, so **never** add token/auth handling.
- Release asset names are exactly `Pane-osx-arm64.zip` and `Pane-osx-x64.zip`.
- LaunchAgent label is exactly `com.pane.launcher`; plist path `~/Library/LaunchAgents/com.pane.launcher.plist`.
- Single version source of truth: the `"."` key of `.release-please-manifest.json`.
- Tests never touch the network and never invoke `launchctl`. Use stub `HttpMessageHandler`s and pure functions.
- Existing test style: xunit `[Fact]`, `snake_case_method_names`, `Xunit` is a global using, temp files via `Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}...")`.
- Run all tests with `dotnet test Pane.slnx`.

---

### Task 1: `AppVersion` — parsing and comparison

**Files:**
- Create: `src/Pane.Core/Updates/AppVersion.cs`
- Test: `tests/Pane.Core.Tests/Updates/AppVersionTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct Pane.Core.Updates.AppVersion(int Major, int Minor, int Patch)` with `static bool TryParse(string?, out AppVersion)`, `static bool TryParseTag(string?, out AppVersion)`, `static readonly AppVersion Zero`, operators `>` `<` `>=` `<=`, `IComparable<AppVersion>`, and `ToString()` returning `"1.2.0"`.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/Updates/AppVersionTests.cs`:

```csharp
using Pane.Core.Updates;
using Xunit;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void TryParse_reads_a_bare_triple(string text, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new AppVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.0.4")]
    [InlineData("v1.2.0")]      // TryParse is strict; tags go through TryParseTag
    [InlineData("banana")]
    public void TryParse_rejects_anything_else(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("pane-v1.2.0")]
    [InlineData("plugins-v1.2.0")]
    [InlineData("v1.2.0")]
    [InlineData("1.2.0")]
    public void TryParseTag_tolerates_every_prefix_this_repo_uses(string tag)
    {
        Assert.True(AppVersion.TryParseTag(tag, out var v));
        Assert.Equal(new AppVersion(1, 2, 0), v);
    }

    [Theory]
    [InlineData("pane-v1.2.0-rc1")]   // pre-release is out of scope, not mis-ordered
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseTag_rejects_tags_it_cannot_order(string? tag)
    {
        Assert.False(AppVersion.TryParseTag(tag, out _));
    }

    [Fact]
    public void Components_are_compared_numerically_not_as_text()
    {
        Assert.True(new AppVersion(1, 10, 0) > new AppVersion(1, 9, 0));
        Assert.True(new AppVersion(2, 0, 0) > new AppVersion(1, 99, 99));
        Assert.True(new AppVersion(1, 2, 3) > new AppVersion(1, 2, 2));
    }

    [Fact]
    public void Equal_versions_are_neither_greater_nor_less()
    {
        var a = new AppVersion(1, 2, 3);
        var b = new AppVersion(1, 2, 3);
        Assert.False(a > b);
        Assert.False(a < b);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToString_round_trips_through_TryParse()
    {
        Assert.True(AppVersion.TryParse(new AppVersion(3, 4, 5).ToString(), out var v));
        Assert.Equal(new AppVersion(3, 4, 5), v);
    }

    [Fact]
    public void Zero_is_lower_than_any_real_release()
    {
        Assert.True(new AppVersion(0, 0, 1) > AppVersion.Zero);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter AppVersionTests`
Expected: FAIL — compile error, `Pane.Core.Updates` namespace / `AppVersion` type does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Pane.Core/Updates/AppVersion.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter AppVersionTests`
Expected: PASS — all facts and theories green.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Updates/AppVersion.cs tests/Pane.Core.Tests/Updates/AppVersionTests.cs
git commit -m "feat(updates): add AppVersion parsing and numeric comparison"
```

---

### Task 2: Stamp a real version into the app

The app currently reports no version at all: `build/make-app.sh` hardcodes `CFBundleVersion 1.0` and nothing passes `-p:Version=` to the publish. Everything downstream compares against this, so it comes first.

**Files:**
- Create: `src/Pane.Core/Updates/AppVersionSource.cs`
- Test: `tests/Pane.Core.Tests/Updates/AppVersionSourceTests.cs`
- Modify: `build/make-app.sh` (version resolution + `Info.plist` stamping)
- Modify: `.github/workflows/release-please.yml` (export `PANE_VERSION` from the tag)

**Interfaces:**
- Consumes: `AppVersion` (Task 1).
- Produces: `static class Pane.Core.Updates.AppVersionSource` with `static AppVersion Current { get; }` and `static AppVersion FromInformationalVersion(string? raw)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/Updates/AppVersionSourceTests.cs`:

```csharp
using Pane.Core.Updates;
using Xunit;

public class AppVersionSourceTests
{
    [Fact]
    public void A_plain_version_is_read_as_is()
    {
        Assert.Equal(new AppVersion(1, 2, 0),
            AppVersionSource.FromInformationalVersion("1.2.0"));
    }

    [Fact]
    public void A_SourceLink_commit_suffix_is_trimmed()
    {
        // SourceLink appends "+<sha>" to AssemblyInformationalVersion.
        Assert.Equal(new AppVersion(1, 3, 4),
            AppVersionSource.FromInformationalVersion("1.3.4+a1b2c3d4e5f6"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.0.0.0")]
    public void An_absent_or_unreadable_version_reports_zero(string? raw)
    {
        // Zero is honest: it makes every real release look newer, rather than
        // silently hiding updates behind a fake version.
        Assert.Equal(AppVersion.Zero, AppVersionSource.FromInformationalVersion(raw));
    }

    [Fact]
    public void Current_never_throws_even_with_no_entry_assembly_attribute()
    {
        // Under the test host there is no stamped version; the contract is that
        // this degrades to Zero rather than blowing up at startup.
        var v = AppVersionSource.Current;
        Assert.True(v >= AppVersion.Zero);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter AppVersionSourceTests`
Expected: FAIL — `AppVersionSource` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Pane.Core/Updates/AppVersionSource.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter AppVersionSourceTests`
Expected: PASS.

- [ ] **Step 5: Teach `make-app.sh` to resolve and stamp the version**

In `build/make-app.sh`, after the line `OUTDIR="${2:-$ROOT/dist}"`, add version resolution:

```bash
# Version: CI passes PANE_VERSION from the release tag; locally we read the
# release-please manifest, which is the single source of truth. A dev build
# with neither is 0.0.0 — honest, and lower than any real release.
if [ -n "${PANE_VERSION:-}" ]; then
  VERSION="$PANE_VERSION"
elif [ -f "$ROOT/.release-please-manifest.json" ]; then
  VERSION="$(sed -n 's/.*"\.": *"\([^"]*\)".*/\1/p' "$ROOT/.release-please-manifest.json")"
fi
VERSION="${VERSION:-0.0.0}"
echo "==> Version $VERSION"
```

Then change the publish call to stamp the managed assembly — replace:

```bash
dotnet publish "$ROOT/src/Pane.App" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -o "$PUBLISH" --nologo -v q
```

with:

```bash
dotnet publish "$ROOT/src/Pane.App" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:Version="$VERSION" -o "$PUBLISH" --nologo -v q
```

Finally, in the `Info.plist` heredoc, replace the two hardcoded lines:

```
  <key>CFBundleVersion</key><string>1.0</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
```

with:

```
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
```

(The heredoc delimiter is unquoted `PLIST`, so `$VERSION` expands. Leave it unquoted.)

- [ ] **Step 6: Verify the stamping works end to end**

Run:

```bash
bash build/make-app.sh "$(uname -m | sed 's/^arm64$/osx-arm64/; s/^x86_64$/osx-x64/')" /tmp/pane-verify
/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' /tmp/pane-verify/Pane.app/Contents/Info.plist
```

Expected: prints `1.2.0` (the current manifest value), **not** `1.0`.

Then confirm the managed assembly carries it too:

```bash
strings /tmp/pane-verify/Pane.app/Contents/MacOS/Pane.App.dll | grep -m1 '^1\.2\.0'
```

Expected: prints `1.2.0`. Clean up with `rm -rf /tmp/pane-verify`.

- [ ] **Step 7: Pass the release tag through CI**

In `.github/workflows/release-please.yml`, replace the build step:

```yaml
      - name: Build Pane.app (${{ matrix.rid }})
        run: bash build/make-app.sh ${{ matrix.rid }} dist
```

with one that derives the version from the tag release-please just cut (e.g. `pane-v1.3.0` → `1.3.0`):

```yaml
      - name: Build Pane.app (${{ matrix.rid }})
        env:
          TAG: ${{ needs.release-please.outputs.tag_name }}
        run: |
          export PANE_VERSION="${TAG##*v}"
          echo "Building version $PANE_VERSION from tag $TAG"
          bash build/make-app.sh ${{ matrix.rid }} dist
```

- [ ] **Step 8: Commit**

```bash
git add src/Pane.Core/Updates/AppVersionSource.cs \
        tests/Pane.Core.Tests/Updates/AppVersionSourceTests.cs \
        build/make-app.sh .github/workflows/release-please.yml
git commit -m "feat(build): stamp the real version into the assembly and bundle"
```

---

### Task 3: `GitHubReleaseSource` — what release is out there

**Files:**
- Create: `src/Pane.Core/Updates/ReleaseInfo.cs`
- Create: `src/Pane.Core/Updates/IReleaseSource.cs`
- Create: `src/Pane.Core/Updates/GitHubReleaseSource.cs`
- Test: `tests/Pane.Core.Tests/Updates/GitHubReleaseSourceTests.cs`

**Interfaces:**
- Consumes: `AppVersion.TryParseTag` (Task 1).
- Produces:
  - `sealed record ReleaseAsset(string Name, string DownloadUrl, long Size)`
  - `sealed record ReleaseInfo(AppVersion Version, string Tag, string HtmlUrl, IReadOnlyList<ReleaseAsset> Assets)` with `ReleaseAsset? FindAsset(string name)`
  - `interface IReleaseSource { Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct); }`
  - `sealed class GitHubReleaseSource : IReleaseSource` with ctor `(HttpMessageHandler handler, string repo = "dknathalage/pane")`

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/Updates/GitHubReleaseSourceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter GitHubReleaseSourceTests`
Expected: FAIL — `ReleaseInfo`, `IReleaseSource`, `GitHubReleaseSource` do not exist.

- [ ] **Step 3: Write the records and the interface**

Create `src/Pane.Core/Updates/ReleaseInfo.cs`:

```csharp
namespace Pane.Core.Updates;

/// <summary>One downloadable file attached to a release.</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>A published release we could potentially install.</summary>
public sealed record ReleaseInfo(
    AppVersion Version,
    string Tag,
    string HtmlUrl,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The asset with this name, or null. Case-insensitive.</summary>
    public ReleaseAsset? FindAsset(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}
```

Create `src/Pane.Core/Updates/IReleaseSource.cs`:

```csharp
namespace Pane.Core.Updates;

/// <summary>
/// Where new releases come from. Exists so UpdateService can be tested against
/// a fake without any network access.
/// </summary>
public interface IReleaseSource
{
    /// <summary>
    /// The latest release, or null when there isn't one we can reason about
    /// (unparseable tag, malformed payload). Transport and HTTP failures throw.
    /// </summary>
    Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Write the GitHub client**

Create `src/Pane.Core/Updates/GitHubReleaseSource.cs`:

```csharp
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
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter GitHubReleaseSourceTests`
Expected: PASS — all 10 tests green.

- [ ] **Step 6: Commit**

```bash
git add src/Pane.Core/Updates/ReleaseInfo.cs src/Pane.Core/Updates/IReleaseSource.cs \
        src/Pane.Core/Updates/GitHubReleaseSource.cs \
        tests/Pane.Core.Tests/Updates/GitHubReleaseSourceTests.cs
git commit -m "feat(updates): read the latest release from the GitHub API"
```

---

### Task 4: `UpdateState` — remember when we last checked

**Files:**
- Create: `src/Pane.Core/Updates/UpdateState.cs`
- Test: `tests/Pane.Core.Tests/Updates/UpdateStateTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed class Pane.Core.Updates.UpdateState` with ctor `(string path)`, `DateTimeOffset? LoadLastCheck()`, `void SaveLastCheck(DateTimeOffset when)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/Updates/UpdateStateTests.cs`:

```csharp
using Pane.Core.Updates;
using Xunit;

public class UpdateStateTests
{
    static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"pane-state-{Guid.NewGuid():N}.json");

    [Fact]
    public void A_missing_file_means_never_checked()
    {
        Assert.Null(new UpdateState(TempPath()).LoadLastCheck());
    }

    [Fact]
    public void The_last_check_time_round_trips()
    {
        var path = TempPath();
        var when = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        new UpdateState(path).SaveLastCheck(when);

        Assert.Equal(when, new UpdateState(path).LoadLastCheck());
        File.Delete(path);
    }

    [Fact]
    public void A_corrupt_file_means_never_checked_rather_than_throwing()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ not json");

        Assert.Null(new UpdateState(path).LoadLastCheck());
        File.Delete(path);
    }

    [Fact]
    public void Saving_creates_the_directory_when_it_is_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pane-state-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "update-state.json");

        new UpdateState(path).SaveLastCheck(DateTimeOffset.UtcNow);

        Assert.True(File.Exists(path));
        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter UpdateStateTests`
Expected: FAIL — `UpdateState` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Pane.Core/Updates/UpdateState.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pane.Core.Updates;

/// <summary>
/// When we last asked GitHub. Deliberately separate from settings.json: this is
/// machine state, not a user preference, and writing it on every check would
/// churn a file the user edits by hand.
/// </summary>
public sealed class UpdateState
{
    const string LastCheckKey = "lastCheckUtc";
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    readonly string _path;
    public UpdateState(string path) => _path = path;

    public DateTimeOffset? LoadLastCheck()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject root) return null;
            var raw = root[LastCheckKey]?.GetValue<string>();
            return DateTimeOffset.TryParse(raw, out var when) ? when : null;
        }
        catch { return null; }
    }

    public void SaveLastCheck(DateTimeOffset when)
    {
        try
        {
            var root = new JsonObject { [LastCheckKey] = when.ToString("O") };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, root.ToJsonString(Opts));
        }
        catch { /* a state file we cannot write must not break update checking */ }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter UpdateStateTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Updates/UpdateState.cs tests/Pane.Core.Tests/Updates/UpdateStateTests.cs
git commit -m "feat(updates): persist the last update-check time"
```

---

### Task 5: `UpdateService` — the state machine

**Files:**
- Create: `src/Pane.Core/Updates/UpdateStatus.cs`
- Create: `src/Pane.Core/Updates/IUpdateInstaller.cs`
- Create: `src/Pane.Core/Updates/UpdateService.cs`
- Test: `tests/Pane.Core.Tests/Updates/UpdateServiceTests.cs`

**Interfaces:**
- Consumes: `AppVersion`, `ReleaseInfo`, `ReleaseAsset`, `IReleaseSource` (Tasks 1, 3), `UpdateState` (Task 4).
- Produces:
  - `abstract record UpdateStatus` with nested `Idle`, `Checking`, `UpToDate(DateTimeOffset? CheckedAt)`, `Available(ReleaseInfo Release, ReleaseAsset Asset)`, `Downloading(int Percent)`, `Installing`, `Failed(string Message)`
  - `interface IUpdateInstaller { bool CanInstall { get; } string? UnavailableReason { get; } Task InstallAsync(ReleaseAsset asset, AppVersion expected, IProgress<int> progress, CancellationToken ct); }`
  - `sealed class UpdateService` with ctor `(IReleaseSource source, IUpdateInstaller installer, UpdateState state, AppVersion current)`, members `UpdateStatus Status`, `event Action? StatusChanged`, `bool CanInstall`, `string? InstallUnavailableReason`, `Task CheckAsync(CancellationToken ct)`, `Task MaybeAutoCheckAsync(bool autoCheckEnabled, CancellationToken ct)`, `Task InstallAsync(CancellationToken ct)`, `AppVersion CurrentVersion`, `static string? AssetNameForCurrentMachine()`, `static readonly TimeSpan CheckInterval`.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/Updates/UpdateServiceTests.cs`:

```csharp
using Pane.Core.Updates;
using Xunit;

public class UpdateServiceTests
{
    sealed class FakeSource : IReleaseSource
    {
        public ReleaseInfo? Release;
        public Exception? Throw;
        public int Calls;

        public Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct)
        {
            Calls++;
            if (Throw is not null) return Task.FromException<ReleaseInfo?>(Throw);
            return Task.FromResult(Release);
        }
    }

    sealed class FakeInstaller : IUpdateInstaller
    {
        public bool CanInstall { get; set; } = true;
        public string? UnavailableReason { get; set; }
        public ReleaseAsset? Installed;
        public Exception? Throw;

        public Task InstallAsync(ReleaseAsset asset, AppVersion expected,
                                 IProgress<int> progress, CancellationToken ct)
        {
            Installed = asset;
            progress.Report(50);
            if (Throw is not null) return Task.FromException(Throw);
            return Task.CompletedTask;
        }
    }

    static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"pane-svc-{Guid.NewGuid():N}.json");

    // The asset this machine would actually download, so tests match reality.
    static string AssetName() => UpdateService.AssetNameForCurrentMachine()!;

    static ReleaseInfo ReleaseWithAsset(string version) =>
        new(Parse(version), $"pane-v{version}", "https://x.test",
            new[] { new ReleaseAsset(AssetName(), "https://x.test/a.zip", 10) });

    static ReleaseInfo ReleaseWithoutAsset(string version) =>
        new(Parse(version), $"pane-v{version}", "https://x.test", Array.Empty<ReleaseAsset>());

    static AppVersion Parse(string v)
    {
        Assert.True(AppVersion.TryParse(v, out var parsed));
        return parsed;
    }

    static UpdateService Service(FakeSource source, FakeInstaller installer,
                                 string current = "1.2.0", string? statePath = null) =>
        new(source, installer, new UpdateState(statePath ?? TempPath()), Parse(current));

    // ── Checking ───────────────────────────────────────────────────────────

    [Fact]
    public void The_service_starts_idle()
    {
        Assert.IsType<UpdateStatus.Idle>(Service(new(), new()).Status);
    }

    [Fact]
    public async Task A_newer_release_with_a_matching_asset_is_available()
    {
        var source = new FakeSource { Release = ReleaseWithAsset("1.3.0") };

        var svc = Service(source, new());
        await svc.CheckAsync(CancellationToken.None);

        var available = Assert.IsType<UpdateStatus.Available>(svc.Status);
        Assert.Equal(new AppVersion(1, 3, 0), available.Release.Version);
        Assert.Equal(AssetName(), available.Asset.Name);
    }

    [Fact]
    public async Task The_same_version_is_up_to_date()
    {
        var svc = Service(new FakeSource { Release = ReleaseWithAsset("1.2.0") }, new());

        await svc.CheckAsync(CancellationToken.None);

        Assert.IsType<UpdateStatus.UpToDate>(svc.Status);
    }

    [Fact]
    public async Task An_older_release_is_up_to_date_not_a_downgrade()
    {
        var svc = Service(new FakeSource { Release = ReleaseWithAsset("1.1.0") }, new());

        await svc.CheckAsync(CancellationToken.None);

        Assert.IsType<UpdateStatus.UpToDate>(svc.Status);
    }

    [Fact]
    public async Task A_newer_release_with_no_installable_asset_is_up_to_date()
    {
        // This is literally today's repo: releases exist, none carry a Pane zip.
        // Offering an update we cannot install would be a dead end for the user.
        var svc = Service(new FakeSource { Release = ReleaseWithoutAsset("1.3.0") }, new());

        await svc.CheckAsync(CancellationToken.None);

        Assert.IsType<UpdateStatus.UpToDate>(svc.Status);
    }

    [Fact]
    public async Task No_release_at_all_is_up_to_date()
    {
        var svc = Service(new FakeSource { Release = null }, new());

        await svc.CheckAsync(CancellationToken.None);

        Assert.IsType<UpdateStatus.UpToDate>(svc.Status);
    }

    [Fact]
    public async Task A_transport_failure_becomes_a_readable_failure()
    {
        var source = new FakeSource { Throw = new HttpRequestException("boom") };
        var svc = Service(source, new());

        await svc.CheckAsync(CancellationToken.None);

        var failed = Assert.IsType<UpdateStatus.Failed>(svc.Status);
        Assert.NotEmpty(failed.Message);
    }

    [Fact]
    public async Task A_failed_check_does_not_advance_the_last_check_time()
    {
        var path = TempPath();
        var source = new FakeSource { Throw = new HttpRequestException("boom") };

        await Service(source, new(), statePath: path).CheckAsync(CancellationToken.None);

        Assert.Null(new UpdateState(path).LoadLastCheck());
    }

    [Fact]
    public async Task A_successful_check_records_the_time()
    {
        var path = TempPath();
        var source = new FakeSource { Release = ReleaseWithAsset("1.3.0") };

        await Service(source, new(), statePath: path).CheckAsync(CancellationToken.None);

        Assert.NotNull(new UpdateState(path).LoadLastCheck());
        File.Delete(path);
    }

    [Fact]
    public async Task Status_changes_are_announced_to_subscribers()
    {
        var svc = Service(new FakeSource { Release = ReleaseWithAsset("1.3.0") }, new());
        var seen = 0;
        svc.StatusChanged += () => seen++;

        await svc.CheckAsync(CancellationToken.None);

        Assert.True(seen >= 2, "expected at least Checking and a terminal status");
    }

    // ── Auto-check ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Auto_check_does_nothing_when_the_setting_is_off()
    {
        var source = new FakeSource { Release = ReleaseWithAsset("1.3.0") };

        await Service(source, new()).MaybeAutoCheckAsync(false, CancellationToken.None);

        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Auto_check_runs_when_it_has_never_checked()
    {
        var source = new FakeSource { Release = ReleaseWithAsset("1.3.0") };

        await Service(source, new()).MaybeAutoCheckAsync(true, CancellationToken.None);

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Auto_check_is_skipped_inside_the_check_interval()
    {
        var path = TempPath();
        new UpdateState(path).SaveLastCheck(DateTimeOffset.UtcNow.AddHours(-1));
        var source = new FakeSource { Release = ReleaseWithAsset("1.3.0") };

        await Service(source, new(), statePath: path)
            .MaybeAutoCheckAsync(true, CancellationToken.None);

        Assert.Equal(0, source.Calls);
        File.Delete(path);
    }

    [Fact]
    public async Task Auto_check_runs_again_once_the_interval_has_passed()
    {
        var path = TempPath();
        new UpdateState(path).SaveLastCheck(
            DateTimeOffset.UtcNow - UpdateService.CheckInterval - TimeSpan.FromMinutes(1));
        var source = new FakeSource { Release = ReleaseWithAsset("1.3.0") };

        await Service(source, new(), statePath: path)
            .MaybeAutoCheckAsync(true, CancellationToken.None);

        Assert.Equal(1, source.Calls);
        File.Delete(path);
    }

    // ── Installing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Installing_without_an_available_update_is_refused()
    {
        var installer = new FakeInstaller();
        var svc = Service(new(), installer);

        await svc.InstallAsync(CancellationToken.None);

        Assert.Null(installer.Installed);
    }

    [Fact]
    public async Task Installing_hands_the_matching_asset_to_the_installer()
    {
        var installer = new FakeInstaller();
        var svc = Service(new FakeSource { Release = ReleaseWithAsset("1.3.0") }, installer);
        await svc.CheckAsync(CancellationToken.None);

        await svc.InstallAsync(CancellationToken.None);

        Assert.NotNull(installer.Installed);
        Assert.Equal(AssetName(), installer.Installed!.Name);
    }

    [Fact]
    public async Task Install_progress_is_reported_as_a_percentage()
    {
        var installer = new FakeInstaller();
        var svc = Service(new FakeSource { Release = ReleaseWithAsset("1.3.0") }, installer);
        await svc.CheckAsync(CancellationToken.None);

        var percents = new List<int>();
        svc.StatusChanged += () =>
        {
            if (svc.Status is UpdateStatus.Downloading d) percents.Add(d.Percent);
        };
        await svc.InstallAsync(CancellationToken.None);

        Assert.Contains(50, percents);
    }

    [Fact]
    public async Task A_failed_install_reports_why_and_keeps_the_update_offered()
    {
        var installer = new FakeInstaller { Throw = new InvalidOperationException("bad bundle") };
        var svc = Service(new FakeSource { Release = ReleaseWithAsset("1.3.0") }, installer);
        await svc.CheckAsync(CancellationToken.None);

        await svc.InstallAsync(CancellationToken.None);

        var failed = Assert.IsType<UpdateStatus.Failed>(svc.Status);
        Assert.Contains("bad bundle", failed.Message);
    }

    [Fact]
    public void An_installer_that_cannot_run_is_surfaced_with_its_reason()
    {
        var installer = new FakeInstaller
        {
            CanInstall = false,
            UnavailableReason = "Pane is not running from an installed .app bundle.",
        };

        var svc = Service(new(), installer);

        Assert.False(svc.CanInstall);
        Assert.Contains(".app bundle", svc.InstallUnavailableReason);
    }

    [Fact]
    public void The_asset_name_matches_this_machines_architecture()
    {
        var name = UpdateService.AssetNameForCurrentMachine();

        Assert.True(name is "Pane-osx-arm64.zip" or "Pane-osx-x64.zip",
            $"unexpected asset name '{name}'");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter UpdateServiceTests`
Expected: FAIL — `UpdateStatus`, `IUpdateInstaller`, `UpdateService` do not exist.

- [ ] **Step 3: Write the status type**

Create `src/Pane.Core/Updates/UpdateStatus.cs`:

```csharp
namespace Pane.Core.Updates;

/// <summary>
/// Everything the updater can be doing, as one closed set. Every failure is a
/// status the user can read — never an exception that escapes to the UI.
/// </summary>
public abstract record UpdateStatus
{
    // Private ctor closes the hierarchy: only the nested cases below exist.
    UpdateStatus() { }

    public sealed record Idle : UpdateStatus;
    public sealed record Checking : UpdateStatus;
    public sealed record UpToDate(DateTimeOffset? CheckedAt) : UpdateStatus;
    public sealed record Available(ReleaseInfo Release, ReleaseAsset Asset) : UpdateStatus;
    public sealed record Downloading(int Percent) : UpdateStatus;
    public sealed record Installing : UpdateStatus;
    public sealed record Failed(string Message) : UpdateStatus;
}
```

- [ ] **Step 4: Write the installer seam**

Create `src/Pane.Core/Updates/IUpdateInstaller.cs`:

```csharp
namespace Pane.Core.Updates;

/// <summary>
/// Puts a downloaded release on disk and restarts into it. Platform-specific;
/// exists as an interface so UpdateService is testable without ever touching
/// the filesystem or spawning a process.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>False when there is nothing safe to replace (e.g. a dev run).</summary>
    bool CanInstall { get; }

    /// <summary>Why <see cref="CanInstall"/> is false, for the user to read.</summary>
    string? UnavailableReason { get; }

    /// <param name="expected">
    /// The version the downloaded bundle must actually contain. Checked before
    /// anything installed is touched.
    /// </param>
    Task InstallAsync(ReleaseAsset asset, AppVersion expected,
                      IProgress<int> progress, CancellationToken ct);
}
```

- [ ] **Step 5: Write the service**

Create `src/Pane.Core/Updates/UpdateService.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Pane.Core.Updates;

/// <summary>
/// Decides whether an update exists and drives installing it. The only place
/// that compares versions or picks an asset.
/// </summary>
public sealed class UpdateService
{
    /// <summary>How stale a check may be before an auto-check runs again.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    readonly IReleaseSource _source;
    readonly IUpdateInstaller _installer;
    readonly UpdateState _state;
    readonly AppVersion _current;

    public UpdateService(IReleaseSource source, IUpdateInstaller installer,
                         UpdateState state, AppVersion current)
    {
        _source = source;
        _installer = installer;
        _state = state;
        _current = current;
    }

    public AppVersion CurrentVersion => _current;
    public UpdateStatus Status { get; private set; } = new UpdateStatus.Idle();
    public event Action? StatusChanged;

    public bool CanInstall => _installer.CanInstall;
    public string? InstallUnavailableReason => _installer.UnavailableReason;

    /// <summary>The release asset this machine can actually run, or null.</summary>
    public static string? AssetNameForCurrentMachine() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "Pane-osx-arm64.zip",
            Architecture.X64 => "Pane-osx-x64.zip",
            _ => null,
        };

    public async Task CheckAsync(CancellationToken ct)
    {
        Set(new UpdateStatus.Checking());
        try
        {
            var release = await _source.FetchLatestAsync(ct);
            var asset = release is null ? null : AssetFor(release);

            // A newer release with no asset for this machine is not actionable,
            // so it is reported as up to date rather than as a broken update.
            if (release is not null && asset is not null && release.Version > _current)
            {
                _state.SaveLastCheck(DateTimeOffset.UtcNow);
                Set(new UpdateStatus.Available(release, asset));
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _state.SaveLastCheck(now);
            Set(new UpdateStatus.UpToDate(now));
        }
        catch (OperationCanceledException)
        {
            Set(new UpdateStatus.Idle());
        }
        catch (Exception ex)
        {
            // Note: the last-check time is deliberately NOT advanced, so a
            // transient outage does not suppress checks for another 24h.
            Set(new UpdateStatus.Failed(Describe(ex)));
        }
    }

    /// <summary>Checks only if enabled and the previous check has gone stale.</summary>
    public async Task MaybeAutoCheckAsync(bool autoCheckEnabled, CancellationToken ct)
    {
        if (!autoCheckEnabled) return;

        var last = _state.LoadLastCheck();
        if (last is { } when && DateTimeOffset.UtcNow - when < CheckInterval) return;

        await CheckAsync(ct);
    }

    public async Task InstallAsync(CancellationToken ct)
    {
        if (Status is not UpdateStatus.Available available) return;
        if (!_installer.CanInstall)
        {
            Set(new UpdateStatus.Failed(
                _installer.UnavailableReason ?? "This copy of Pane cannot update itself."));
            return;
        }

        var progress = new Progress<int>(pct => Set(new UpdateStatus.Downloading(pct)));
        try
        {
            Set(new UpdateStatus.Downloading(0));
            await _installer.InstallAsync(available.Asset, available.Release.Version, progress, ct);
            Set(new UpdateStatus.Installing());
        }
        catch (OperationCanceledException)
        {
            Set(available);   // still offered, nothing was changed
        }
        catch (Exception ex)
        {
            Set(new UpdateStatus.Failed(Describe(ex)));
        }
    }

    ReleaseAsset? AssetFor(ReleaseInfo release) =>
        AssetNameForCurrentMachine() is { } name ? release.FindAsset(name) : null;

    static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
            "GitHub rate-limited the update check. Try again later.",
        HttpRequestException => "Could not reach GitHub to check for updates.",
        TaskCanceledException => "The update check timed out.",
        _ => ex.Message,
    };

    void Set(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter UpdateServiceTests`
Expected: PASS — all 20 tests green.

- [ ] **Step 7: Commit**

```bash
git add src/Pane.Core/Updates/UpdateStatus.cs src/Pane.Core/Updates/IUpdateInstaller.cs \
        src/Pane.Core/Updates/UpdateService.cs \
        tests/Pane.Core.Tests/Updates/UpdateServiceTests.cs
git commit -m "feat(updates): add the update check/install state machine"
```

---

### Task 6: `BundleLayout` — find and validate a `.app`

The pure, testable half of self-replacement: where are we installed, and is a downloaded bundle safe to install?

**Files:**
- Create: `src/Pane.Core/Updates/BundleLayout.cs`
- Test: `tests/Pane.Core.Tests/Updates/BundleLayoutTests.cs`

**Interfaces:**
- Consumes: `AppVersion` (Task 1).
- Produces: `static class Pane.Core.Updates.BundleLayout` with:
  - `const string ExecutableName = "Pane.App"`
  - `static string? FindEnclosingBundle(string? startDirectory)`
  - `static string? CurrentBundle()` — the bundle this process runs from
  - `static AppVersion? ReadBundleVersion(string bundlePath)`
  - `static string? Validate(string bundlePath, AppVersion mustExceed)` — returns null when valid, else the reason.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/Updates/BundleLayoutTests.cs`:

```csharp
using Pane.Core.Updates;
using Xunit;

public class BundleLayoutTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"pane-bundle-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Builds a Pane.app skeleton; pass version null to omit Info.plist.</summary>
    string MakeBundle(string name = "Pane.app", string? version = "1.3.0",
                      bool withExecutable = true)
    {
        var app = Path.Combine(_root, name);
        var macOs = Path.Combine(app, "Contents", "MacOS");
        Directory.CreateDirectory(macOs);

        if (withExecutable)
            File.WriteAllText(Path.Combine(macOs, BundleLayout.ExecutableName), "#!/bin/sh\n");

        if (version is not null)
            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>CFBundleName</key><string>Pane</string>
                  <key>CFBundleShortVersionString</key><string>{version}</string>
                </dict>
                </plist>
                """);

        return app;
    }

    // ── Finding the enclosing bundle ───────────────────────────────────────

    [Fact]
    public void A_directory_inside_Contents_MacOS_resolves_to_the_app()
    {
        var app = MakeBundle();

        var found = BundleLayout.FindEnclosingBundle(Path.Combine(app, "Contents", "MacOS"));

        Assert.Equal(app, found);
    }

    [Fact]
    public void A_plain_directory_is_not_inside_a_bundle()
    {
        Directory.CreateDirectory(_root);

        Assert.Null(BundleLayout.FindEnclosingBundle(_root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_start_directory_is_not_inside_a_bundle(string? start)
    {
        Assert.Null(BundleLayout.FindEnclosingBundle(start));
    }

    // ── Reading the version ────────────────────────────────────────────────

    [Fact]
    public void The_bundle_version_is_read_from_Info_plist()
    {
        Assert.Equal(new AppVersion(1, 3, 0),
            BundleLayout.ReadBundleVersion(MakeBundle(version: "1.3.0")));
    }

    [Fact]
    public void A_bundle_with_no_Info_plist_has_no_version()
    {
        Assert.Null(BundleLayout.ReadBundleVersion(MakeBundle(version: null)));
    }

    // ── Validation ─────────────────────────────────────────────────────────

    [Fact]
    public void A_well_formed_newer_bundle_validates()
    {
        Assert.Null(BundleLayout.Validate(MakeBundle(version: "1.3.0"), new AppVersion(1, 2, 0)));
    }

    [Fact]
    public void A_bundle_with_no_executable_is_rejected()
    {
        var reason = BundleLayout.Validate(
            MakeBundle(withExecutable: false), new AppVersion(1, 2, 0));

        Assert.NotNull(reason);
        Assert.Contains(BundleLayout.ExecutableName, reason);
    }

    [Fact]
    public void A_bundle_with_no_Info_plist_is_rejected()
    {
        var reason = BundleLayout.Validate(MakeBundle(version: null), new AppVersion(1, 2, 0));

        Assert.NotNull(reason);
        Assert.Contains("version", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_bundle_that_is_not_newer_is_rejected()
    {
        // Guards against a swapped or rolled-back asset quietly downgrading us.
        var reason = BundleLayout.Validate(MakeBundle(version: "1.2.0"), new AppVersion(1, 2, 0));

        Assert.NotNull(reason);
        Assert.Contains("1.2.0", reason);
    }

    [Fact]
    public void A_missing_bundle_is_rejected()
    {
        var reason = BundleLayout.Validate(
            Path.Combine(_root, "Nope.app"), new AppVersion(1, 2, 0));

        Assert.NotNull(reason);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter BundleLayoutTests`
Expected: FAIL — `BundleLayout` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Pane.Core/Updates/BundleLayout.cs`:

```csharp
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
            // In a plist, a <key> is followed by its value as the next sibling.
            var dict = XDocument.Load(plist).Descendants("dict").FirstOrDefault();
            var key = dict?.Elements("key").FirstOrDefault(e => e.Value == VersionKey);
            var value = (key?.NextNode as XElement)?.Value;

            return AppVersion.TryParse(value, out var v) ? v : null;
        }
        catch { return null; }
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter BundleLayoutTests`
Expected: PASS — all 11 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Updates/BundleLayout.cs tests/Pane.Core.Tests/Updates/BundleLayoutTests.cs
git commit -m "feat(updates): locate and validate .app bundles"
```

---

### Task 7: `MacUpdateInstaller` — download, validate, hand off

A running self-contained .NET app demand-loads dylibs out of its own bundle for its whole lifetime, so it cannot safely overwrite itself. A detached helper does the swap *after* we exit.

**Files:**
- Create: `src/Pane.Core/Updates/MacUpdateInstaller.cs`
- Test: `tests/Pane.Core.Tests/Updates/MacUpdateInstallerTests.cs`

**Interfaces:**
- Consumes: `IUpdateInstaller`, `ReleaseAsset`, `AppVersion` (Tasks 1, 5), `BundleLayout` (Task 6).
- Produces: `sealed class Pane.Core.Updates.MacUpdateInstaller : IUpdateInstaller` with ctor `(HttpMessageHandler handler, string? installedBundle, Action quitApp)`, plus `internal static string BuildHelperScript(int pid, string newBundle, string target, string payloadDir)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/Updates/MacUpdateInstallerTests.cs`:

```csharp
using Pane.Core.Updates;
using Xunit;

public class MacUpdateInstallerTests
{
    static MacUpdateInstaller Installer(string? bundle) =>
        new(new HttpClientHandler(), bundle, quitApp: () => { });

    // ── Whether we can install at all ──────────────────────────────────────

    [Fact]
    public void A_dev_run_outside_a_bundle_cannot_install()
    {
        var installer = Installer(null);

        Assert.False(installer.CanInstall);
        Assert.NotNull(installer.UnavailableReason);
    }

    [Fact]
    public void The_reason_names_the_bundle_so_the_user_understands_why()
    {
        Assert.Contains(".app", Installer(null).UnavailableReason!);
    }

    [Fact]
    public void An_installed_bundle_can_install()
    {
        Assert.True(Installer("/Users/someone/Applications/Pane.app").CanInstall);
    }

    // ── The handoff script ─────────────────────────────────────────────────

    static string Script() => MacUpdateInstaller.BuildHelperScript(
        pid: 4242,
        newBundle: "/tmp/pane-dl/Pane.app",
        target: "/Users/someone/Applications/Pane.app",
        payloadDir: "/tmp/pane-dl");

    [Fact]
    public void The_script_waits_for_our_process_to_exit_before_touching_anything()
    {
        var script = Script();

        Assert.Contains("kill -0 4242", script);
    }

    [Fact]
    public void The_script_moves_the_old_bundle_aside_rather_than_deleting_it_outright()
    {
        // A failed copy must leave a working app, not none.
        var script = Script();

        Assert.Contains("mv ", script);
        Assert.Contains(".pane-old", script);
    }

    [Fact]
    public void The_script_restores_the_backup_when_the_copy_fails()
    {
        Assert.Contains("mv \"$BACKUP\" \"$TARGET\"", Script());
    }

    [Fact]
    public void The_script_clears_the_quarantine_flag_so_Gatekeeper_allows_launch()
    {
        Assert.Contains("xattr -dr com.apple.quarantine", Script());
    }

    [Fact]
    public void The_script_reopens_the_app_when_it_is_done()
    {
        Assert.Contains("open ", Script());
    }

    [Fact]
    public void The_script_cleans_up_the_downloaded_payload()
    {
        Assert.Contains("/tmp/pane-dl", Script());
    }

    [Fact]
    public void The_script_quotes_every_path_so_spaces_do_not_split_arguments()
    {
        // "~/Applications/Pane.app" is fine, but a user's disk may not be.
        var script = MacUpdateInstaller.BuildHelperScript(
            1, "/tmp/a b/Pane.app", "/Users/x/My Apps/Pane.app", "/tmp/a b");

        Assert.Contains("\"/Users/x/My Apps/Pane.app\"", script);
        Assert.Contains("\"/tmp/a b/Pane.app\"", script);
    }

    [Fact]
    public async Task Installing_from_outside_a_bundle_is_refused_before_any_download()
    {
        // The refusal must come before any network or filesystem work.
        var installer = Installer(null);
        var asset = new ReleaseAsset("Pane-osx-arm64.zip", "https://x.test/a.zip", 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync(asset, new AppVersion(9, 9, 9),
                new Progress<int>(), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter MacUpdateInstallerTests`
Expected: FAIL — `MacUpdateInstaller` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Pane.Core/Updates/MacUpdateInstaller.cs`:

```csharp
using System.Diagnostics;

namespace Pane.Core.Updates;

/// <summary>
/// Replaces the installed Pane.app with a downloaded release.
///
/// The swap is done by a detached shell helper AFTER this process exits. A
/// self-contained .NET app demand-loads dylibs out of Contents/MacOS for its
/// whole lifetime, so overwriting the bundle in place risks faulting the very
/// process doing the overwriting.
///
/// Trust model: this downloads and runs unsigned code, trusting TLS and
/// GitHub's control of the release assets — exactly the trust install.sh
/// already requires. It is NOT signature or checksum verification.
/// </summary>
public sealed class MacUpdateInstaller : IUpdateInstaller
{
    readonly HttpClient _http;
    readonly string? _bundle;
    readonly Action _quitApp;

    /// <param name="installedBundle">The .app to replace, or null when not installed.</param>
    /// <param name="quitApp">Terminates the app so the helper can take over.</param>
    public MacUpdateInstaller(HttpMessageHandler handler, string? installedBundle, Action quitApp)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _bundle = installedBundle;
        _quitApp = quitApp;
    }

    public MacUpdateInstaller(Action quitApp)
        : this(new HttpClientHandler(), BundleLayout.CurrentBundle(), quitApp) { }

    public bool CanInstall => OperatingSystem.IsMacOS() && _bundle is not null;

    public string? UnavailableReason => CanInstall
        ? null
        : "Pane is not running from an installed .app bundle, so it cannot update itself. "
          + "Install it with install.sh first.";

    public async Task InstallAsync(ReleaseAsset asset, AppVersion expected,
                                   IProgress<int> progress, CancellationToken ct)
    {
        if (_bundle is null) throw new InvalidOperationException(UnavailableReason);

        var work = Path.Combine(Path.GetTempPath(), $"pane-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var zip = Path.Combine(work, asset.Name);
            await DownloadAsync(asset, zip, progress, ct);

            var extracted = Path.Combine(work, "extracted");
            Directory.CreateDirectory(extracted);
            await ExtractAsync(zip, extracted, ct);

            // ditto preserves the symlinks and xattrs inside a .app that plain
            // unzip flattens, so the extracted bundle is actually launchable.
            var newBundle = Path.Combine(extracted, "Pane.app");
            if (BundleLayout.Validate(newBundle, mustExceed: expected) is { } reason)
                throw new InvalidOperationException(reason);

            // Nothing installed has been touched up to this point.
            Handoff(newBundle, work);
        }
        catch
        {
            TryDelete(work);
            throw;
        }
    }

    async Task DownloadAsync(ReleaseAsset asset, string destination,
                             IProgress<int> progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int lastReported = -1, read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;

            if (total <= 0) continue;
            var percent = (int)(written * 100 / total);
            if (percent == lastReported) continue;   // don't spam the UI per chunk
            lastReported = percent;
            progress.Report(percent);
        }
    }

    static async Task ExtractAsync(string zip, string destination, CancellationToken ct)
    {
        var info = new ProcessStartInfo("/usr/bin/ditto")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-x");
        info.ArgumentList.Add("-k");
        info.ArgumentList.Add(zip);
        info.ArgumentList.Add(destination);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not run /usr/bin/ditto.");

        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"The downloaded archive could not be expanded. {error}".Trim());
    }

    void Handoff(string newBundle, string payloadDir)
    {
        // The script lives OUTSIDE payloadDir: it deletes that directory, and a
        // shell reading its own script incrementally must not have it removed
        // underneath. What it leaves behind is a few hundred bytes in the system
        // temp dir, which macOS reaps.
        var scriptDir = Path.Combine(Path.GetTempPath(), $"pane-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDir);
        var script = Path.Combine(scriptDir, "swap.sh");

        File.WriteAllText(script,
            BuildHelperScript(Environment.ProcessId, newBundle, _bundle!, payloadDir));
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Process.Start(new ProcessStartInfo("/bin/sh", script) { UseShellExecute = false });

        _quitApp();
    }

    /// <summary>
    /// The swap script. Pure so its safety properties are unit-tested: it waits
    /// for us to exit, moves the old bundle aside rather than deleting it, and
    /// puts it back if the copy fails.
    /// </summary>
    internal static string BuildHelperScript(int pid, string newBundle, string target,
                                             string payloadDir) => $"""
        #!/bin/sh
        # Written by Pane to replace itself. Safe to delete.
        NEW="{newBundle}"
        TARGET="{target}"
        BACKUP="{target}.pane-old"

        # Wait for Pane to exit (bounded at ~30s so a wedged process can't hang us).
        i=0
        while kill -0 {pid} 2>/dev/null && [ $i -lt 60 ]; do
          sleep 0.5
          i=$((i + 1))
        done

        rm -rf "$BACKUP"
        if [ -d "$TARGET" ]; then
          mv "$TARGET" "$BACKUP" || exit 1
        fi

        if cp -R "$NEW" "$TARGET"; then
          xattr -dr com.apple.quarantine "$TARGET" 2>/dev/null || true
          rm -rf "$BACKUP"
        else
          # Put the working app back rather than leaving the user with none.
          rm -rf "$TARGET"
          [ -d "$BACKUP" ] && mv "$BACKUP" "$TARGET"
        fi

        open "$TARGET"
        rm -rf "{payloadDir}"
        """;

    static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter MacUpdateInstallerTests`
Expected: PASS — all 11 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Updates/MacUpdateInstaller.cs \
        tests/Pane.Core.Tests/Updates/MacUpdateInstallerTests.cs
git commit -m "feat(updates): install a downloaded release via a detached swap helper"
```

---

### Task 8: Start at login

**Files:**
- Create: `src/Pane.Core/Startup/ILoginItem.cs`
- Create: `src/Pane.Core/Startup/LaunchAgentPlist.cs`
- Create: `src/Pane.Core/Startup/MacLoginItem.cs`
- Test: `tests/Pane.Core.Tests/Startup/LaunchAgentPlistTests.cs`
- Test: `tests/Pane.Core.Tests/Startup/MacLoginItemTests.cs`
- Modify: `install.sh:79` (`launchctl load` → `bootstrap`)

**Interfaces:**
- Consumes: `BundleLayout` (Task 6).
- Produces:
  - `interface Pane.Core.Startup.ILoginItem { bool CanManage { get; } string? UnavailableReason { get; } bool IsEnabled { get; } void Enable(); void Disable(); }`
  - `static class LaunchAgentPlist` with `const string Label = "com.pane.launcher"`, `static string DefaultPath()`, `static string Build(string execPath)`, `static string? ReadProgramPath(string plistXml)`
  - `sealed class MacLoginItem : ILoginItem` with ctor `(string? bundlePath, string plistPath)`, a parameterless ctor, and `string? ExpectedExecutable`
  - `sealed class UnsupportedLoginItem : ILoginItem`

- [ ] **Step 1: Write the failing plist test**

Create `tests/Pane.Core.Tests/Startup/LaunchAgentPlistTests.cs`:

```csharp
using Pane.Core.Startup;
using Xunit;

public class LaunchAgentPlistTests
{
    const string Exec = "/Users/someone/Applications/Pane.app/Contents/MacOS/Pane.App";

    [Fact]
    public void The_label_matches_the_one_install_sh_registers()
    {
        // install.sh, uninstall-startup.sh and this class must agree, or the
        // toggle would manage a different agent than the installer wrote.
        Assert.Equal("com.pane.launcher", LaunchAgentPlist.Label);
        Assert.Contains("com.pane.launcher", LaunchAgentPlist.Build(Exec));
    }

    [Fact]
    public void The_plist_launches_the_bundle_executable()
    {
        Assert.Contains(Exec, LaunchAgentPlist.Build(Exec));
    }

    [Fact]
    public void The_plist_passes_startup_so_Pane_comes_up_hidden()
    {
        Assert.Contains("--startup", LaunchAgentPlist.Build(Exec));
    }

    [Fact]
    public void The_plist_runs_at_load_but_is_not_kept_alive()
    {
        var plist = LaunchAgentPlist.Build(Exec);

        Assert.Contains("<key>RunAtLoad</key><true/>", plist.Replace("\n", "").Replace("  ", ""));
        Assert.Contains("KeepAlive", plist);
    }

    [Fact]
    public void The_plist_is_well_formed_xml()
    {
        var doc = System.Xml.Linq.XDocument.Parse(LaunchAgentPlist.Build(Exec));

        Assert.Equal("plist", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void The_program_path_can_be_read_back_out()
    {
        var plist = LaunchAgentPlist.Build(Exec);

        Assert.Equal(Exec, LaunchAgentPlist.ReadProgramPath(plist));
    }

    [Fact]
    public void Reading_a_malformed_plist_yields_no_path_rather_than_throwing()
    {
        Assert.Null(LaunchAgentPlist.ReadProgramPath("<not-xml"));
    }

    [Fact]
    public void The_default_path_is_the_user_LaunchAgents_folder()
    {
        var path = LaunchAgentPlist.DefaultPath();

        Assert.EndsWith("Library/LaunchAgents/com.pane.launcher.plist", path);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter LaunchAgentPlistTests`
Expected: FAIL — `Pane.Core.Startup` does not exist.

- [ ] **Step 3: Write the plist generator**

Create `src/Pane.Core/Startup/LaunchAgentPlist.cs`:

```csharp
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

            return (key?.NextNode as XElement)?.Elements("string").FirstOrDefault()?.Value;
        }
        catch { return null; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter LaunchAgentPlistTests`
Expected: PASS — all 8 tests green.

- [ ] **Step 5: Write the failing login-item test**

Create `tests/Pane.Core.Tests/Startup/MacLoginItemTests.cs`:

```csharp
using Pane.Core.Startup;
using Xunit;

public class MacLoginItemTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"pane-login-{Guid.NewGuid():N}");
    readonly string _plist;
    const string Bundle = "/Users/someone/Applications/Pane.app";
    const string Exec = Bundle + "/Contents/MacOS/Pane.App";

    public MacLoginItemTests()
    {
        Directory.CreateDirectory(_dir);
        _plist = Path.Combine(_dir, "com.pane.launcher.plist");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    MacLoginItem Item(string? bundle = Bundle) => new(bundle, _plist);

    [Fact]
    public void Without_a_bundle_the_login_item_cannot_be_managed()
    {
        var item = Item(bundle: null);

        Assert.False(item.CanManage);
        Assert.NotNull(item.UnavailableReason);
    }

    [Fact]
    public void With_a_bundle_the_login_item_can_be_managed_on_macOS()
    {
        // CanManage also requires macOS, so assert only the bundle half here.
        Assert.Equal(OperatingSystem.IsMacOS(), Item().CanManage);
    }

    [Fact]
    public void No_plist_means_it_does_not_start_at_login()
    {
        Assert.False(Item().IsEnabled);
    }

    [Fact]
    public void A_plist_pointing_at_this_bundle_means_it_starts_at_login()
    {
        File.WriteAllText(_plist, LaunchAgentPlist.Build(Exec));

        Assert.True(Item().IsEnabled);
    }

    [Fact]
    public void A_plist_pointing_somewhere_else_does_not_count_as_enabled()
    {
        // A stale agent from an older install would otherwise report "on" while
        // silently launching a different copy of Pane at login.
        File.WriteAllText(_plist,
            LaunchAgentPlist.Build("/Applications/Old/Pane.app/Contents/MacOS/Pane.App"));

        Assert.False(Item().IsEnabled);
    }

    [Fact]
    public void A_corrupt_plist_does_not_count_as_enabled()
    {
        File.WriteAllText(_plist, "<not-xml");

        Assert.False(Item().IsEnabled);
    }

    [Fact]
    public void The_expected_executable_sits_inside_the_bundle()
    {
        Assert.Equal(Exec, Item().ExpectedExecutable);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter MacLoginItemTests`
Expected: FAIL — `ILoginItem` / `MacLoginItem` do not exist.

- [ ] **Step 7: Write the login item**

Create `src/Pane.Core/Startup/ILoginItem.cs`:

```csharp
namespace Pane.Core.Startup;

/// <summary>Whether Pane starts itself when the user logs in.</summary>
public interface ILoginItem
{
    /// <summary>False when there is nothing to register (dev run, wrong OS).</summary>
    bool CanManage { get; }

    /// <summary>Why <see cref="CanManage"/> is false, for the user to read.</summary>
    string? UnavailableReason { get; }

    /// <summary>True only when the agent on disk launches THIS bundle.</summary>
    bool IsEnabled { get; }

    void Enable();
    void Disable();
}

/// <summary>Stand-in where login items don't apply, so the UI can still render.</summary>
public sealed class UnsupportedLoginItem : ILoginItem
{
    public bool CanManage => false;
    public string? UnavailableReason => "Starting at login is only supported on macOS.";
    public bool IsEnabled => false;
    public void Enable() { }
    public void Disable() { }
}
```

Create `src/Pane.Core/Startup/MacLoginItem.cs`:

```csharp
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
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter MacLoginItemTests`
Expected: PASS — all 7 tests green.

- [ ] **Step 9: Bring `install.sh` in line**

`install.sh` uses the deprecated `launchctl load`, while `build/install-startup.sh` already uses `bootout`/`bootstrap`. Make them agree so the installer and the in-app toggle register the agent identically.

In `install.sh`, replace:

```bash
launchctl load "$PLIST"
```

with:

```bash
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"
```

And in the "Stopping any running Pane" block above it, replace the matching
`launchctl unload "$PLIST" 2>/dev/null || true` with:

```bash
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
```

- [ ] **Step 10: Verify the script still parses**

Run: `bash -n install.sh && echo "syntax ok"`
Expected: prints `syntax ok`.

- [ ] **Step 11: Commit**

```bash
git add src/Pane.Core/Startup tests/Pane.Core.Tests/Startup install.sh
git commit -m "feat(startup): manage the login-item LaunchAgent from Pane"
```

---

### Task 9: An `autoCheckUpdates` setting

**Files:**
- Modify: `src/Pane.Core/Settings/SettingsStore.cs`
- Test: `tests/Pane.Core.Tests/SettingsStoreTests.cs` (append)

**Interfaces:**
- Consumes: nothing.
- Produces: `PaneSettings` gains a third positional member — `sealed record PaneSettings(string Hotkey, Dictionary<string, JsonObject> Features, bool AutoCheckUpdates = true)`. The default keeps every existing two-argument call site compiling.

- [ ] **Step 1: Write the failing test**

Append to `tests/Pane.Core.Tests/SettingsStoreTests.cs`, inside the class:

```csharp
    [Fact]
    public void Auto_check_updates_defaults_to_on()
    {
        Assert.True(new SettingsStore(TempPath()).Load().AutoCheckUpdates);
    }

    [Fact]
    public void Auto_check_updates_roundtrips_when_turned_off()
    {
        var path = TempPath();
        var store = new SettingsStore(path);

        store.Save(new PaneSettings(SettingsStore.DefaultHotkey, new(), AutoCheckUpdates: false));

        Assert.False(store.Load().AutoCheckUpdates);
        File.Delete(path);
    }

    [Fact]
    public void An_older_settings_file_without_the_key_loads_as_on()
    {
        // Upgrading must not silently disable update checks, nor discard the
        // rest of an existing document.
        var path = TempPath();
        File.WriteAllText(path, """
            { "hotkey": "Ctrl+Space", "features": { "files": { "maxResults": 9 } } }
            """);

        var s = new SettingsStore(path).Load();

        Assert.True(s.AutoCheckUpdates);
        Assert.Equal("Ctrl+Space", s.Hotkey);
        Assert.Equal(9, (int)s.Features["files"]["maxResults"]!);
        File.Delete(path);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests/Pane.Core.Tests.csproj --filter SettingsStoreTests`
Expected: FAIL — `PaneSettings` has no `AutoCheckUpdates` member.

- [ ] **Step 3: Write minimal implementation**

In `src/Pane.Core/Settings/SettingsStore.cs`, change the record declaration:

```csharp
public sealed record PaneSettings(
    string Hotkey,
    Dictionary<string, JsonObject> Features,
    bool AutoCheckUpdates = true)
```

Add the key constant next to the others:

```csharp
    const string AutoCheckUpdatesKey = "autoCheckUpdates";
```

In `Load()`, read it, defaulting to true for older files:

```csharp
            var hotkey = root[HotkeyKey]?.GetValue<string>();
            var autoCheck = root[AutoCheckUpdatesKey]?.GetValue<bool>() ?? true;
            var features = ReadFeatures(root);
            MigrateDisabledPlugins(root, features);

            return new PaneSettings(
                string.IsNullOrEmpty(hotkey) ? DefaultHotkey : hotkey, features, autoCheck);
```

In `Save()`, write it:

```csharp
        var root = new JsonObject
        {
            [HotkeyKey] = s.Hotkey,
            [AutoCheckUpdatesKey] = s.AutoCheckUpdates,
            [FeaturesKey] = features,
        };
```

And update `Defaults()`:

```csharp
    static PaneSettings Defaults() => new(DefaultHotkey, new(), AutoCheckUpdates: true);
```

- [ ] **Step 4: Run the whole suite to verify nothing regressed**

Run: `dotnet test Pane.slnx`
Expected: PASS — the three new tests plus every existing one. `PaneSettings`'s default argument keeps the old two-argument call sites in `SettingsStoreTests` and `SettingsPageTests` compiling.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Settings/SettingsStore.cs tests/Pane.Core.Tests/SettingsStoreTests.cs
git commit -m "feat(settings): persist the auto-check-for-updates preference"
```

---

### Task 10: Settings → General gets version, startup, and update controls

**Files:**
- Modify: `src/Pane.Ui/Settings/GeneralPane.razor`
- Modify: `src/Pane.Ui/wwwroot/pane.css` (append)
- Modify: `tests/Pane.Ui.Tests/SettingsPageTests.cs` (register the new services)
- Test: `tests/Pane.Ui.Tests/GeneralPaneTests.cs`

**Interfaces:**
- Consumes: `UpdateService`, `UpdateStatus`, `IReleaseSource`, `IUpdateInstaller`, `UpdateState`, `AppVersion` (Tasks 1–5), `ILoginItem` (Task 8), `PaneSettings.AutoCheckUpdates` (Task 9).
- Produces: no new public API. Adds DOM hooks the tests select on: `input[name=startAtLogin]`, `input[name=autoCheckUpdates]`, `button.pane-check-updates`, `button.pane-install-update`, `.pane-update-status`, `.pane-version`.

**Important:** `GeneralPane` is rendered by `SettingsPage`, so the moment it injects new services **every existing `SettingsPageTests` test fails** with an unregistered-service error. Step 5 fixes that; do not skip it.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Ui.Tests/GeneralPaneTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Pane.Core.Settings;
using Pane.Core.Startup;
using Pane.Core.Updates;
using Pane.Ui.Settings;
using Xunit;

public class GeneralPaneTests : IDisposable
{
    sealed class FakeSource : IReleaseSource
    {
        public ReleaseInfo? Release;
        public Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct) =>
            Task.FromResult(Release);
    }

    sealed class FakeInstaller : IUpdateInstaller
    {
        public bool CanInstall { get; set; } = true;
        public string? UnavailableReason { get; set; }
        public bool Ran;
        public Task InstallAsync(ReleaseAsset asset, AppVersion expected,
                                 IProgress<int> progress, CancellationToken ct)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    sealed class FakeLoginItem : ILoginItem
    {
        public bool CanManage { get; set; } = true;
        public string? UnavailableReason { get; set; }
        public bool IsEnabled { get; set; }
        public void Enable() => IsEnabled = true;
        public void Disable() => IsEnabled = false;
    }

    readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), $"pane-gp-{Guid.NewGuid():N}.json");
    readonly string _statePath =
        Path.Combine(Path.GetTempPath(), $"pane-gp-state-{Guid.NewGuid():N}.json");

    readonly TestContext _ctx = new();
    readonly SettingsStore _store;
    readonly FakeSource _source = new();
    readonly FakeInstaller _installer = new();
    readonly FakeLoginItem _login = new();
    readonly UpdateService _updates;

    public GeneralPaneTests()
    {
        _store = new SettingsStore(_settingsPath);
        _updates = new UpdateService(_source, _installer,
            new UpdateState(_statePath), new AppVersion(1, 2, 0));

        _ctx.Services.AddSingleton(_store);
        _ctx.Services.AddSingleton(_updates);
        _ctx.Services.AddSingleton<ILoginItem>(_login);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        try { File.Delete(_settingsPath); } catch { }
        try { File.Delete(_statePath); } catch { }
    }

    IRenderedComponent<GeneralPane> Render() => _ctx.RenderComponent<GeneralPane>();

    static ReleaseInfo NewRelease() => new(
        new AppVersion(1, 3, 0), "pane-v1.3.0", "https://x.test",
        new[] { new ReleaseAsset(UpdateService.AssetNameForCurrentMachine()!,
                                 "https://x.test/a.zip", 10) });

    // ── Version ────────────────────────────────────────────────────────────

    [Fact]
    public void The_running_version_is_shown()
    {
        Assert.Contains("1.2.0", Render().Find(".pane-version").TextContent);
    }

    // ── Start at login ─────────────────────────────────────────────────────

    [Fact]
    public void The_startup_checkbox_reflects_the_real_on_disk_state()
    {
        _login.IsEnabled = true;

        var box = Render().Find("input[name=startAtLogin]");

        Assert.True(box.HasAttribute("checked"));
    }

    [Fact]
    public void Ticking_start_at_login_registers_the_login_item()
    {
        Render().Find("input[name=startAtLogin]").Change(true);

        Assert.True(_login.IsEnabled);
    }

    [Fact]
    public void Unticking_start_at_login_removes_the_login_item()
    {
        _login.IsEnabled = true;

        Render().Find("input[name=startAtLogin]").Change(false);

        Assert.False(_login.IsEnabled);
    }

    [Fact]
    public void An_unmanageable_login_item_is_disabled_and_explains_why()
    {
        _login.CanManage = false;
        _login.UnavailableReason = "Pane is not running from an installed .app bundle.";

        var page = Render();

        Assert.True(page.Find("input[name=startAtLogin]").HasAttribute("disabled"));
        Assert.Contains(".app bundle", page.Markup);
    }

    // ── Auto-check ─────────────────────────────────────────────────────────

    [Fact]
    public void Auto_check_is_on_by_default()
    {
        Assert.True(Render().Find("input[name=autoCheckUpdates]").HasAttribute("checked"));
    }

    [Fact]
    public void Turning_auto_check_off_persists_it()
    {
        Render().Find("input[name=autoCheckUpdates]").Change(false);

        Assert.False(_store.Load().AutoCheckUpdates);
    }

    [Fact]
    public void Turning_auto_check_off_leaves_the_hotkey_alone()
    {
        _store.Save(new PaneSettings("Ctrl+Space", new()));

        Render().Find("input[name=autoCheckUpdates]").Change(false);

        Assert.Equal("Ctrl+Space", _store.Load().Hotkey);
    }

    // ── Checking and installing ────────────────────────────────────────────

    [Fact]
    public void Check_now_reports_being_up_to_date()
    {
        _source.Release = null;

        var page = Render();
        page.Find("button.pane-check-updates").Click();

        Assert.Contains("up to date", page.Find(".pane-update-status").TextContent,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_available_release_is_named_in_the_status()
    {
        _source.Release = NewRelease();

        var page = Render();
        page.Find("button.pane-check-updates").Click();

        Assert.Contains("1.3.0", page.Find(".pane-update-status").TextContent);
    }

    [Fact]
    public void An_available_release_offers_an_install_button()
    {
        _source.Release = NewRelease();

        var page = Render();
        page.Find("button.pane-check-updates").Click();

        Assert.NotNull(page.Find("button.pane-install-update"));
    }

    [Fact]
    public void There_is_no_install_button_when_nothing_is_available()
    {
        var page = Render();
        page.Find("button.pane-check-updates").Click();

        Assert.Empty(page.FindAll("button.pane-install-update"));
    }

    [Fact]
    public void Pressing_install_runs_the_installer()
    {
        _source.Release = NewRelease();

        var page = Render();
        page.Find("button.pane-check-updates").Click();
        page.Find("button.pane-install-update").Click();

        Assert.True(_installer.Ran);
    }

    [Fact]
    public void An_installer_that_cannot_run_says_so_instead_of_offering_a_button()
    {
        _installer.CanInstall = false;
        _installer.UnavailableReason = "Pane is not running from an installed .app bundle.";
        _source.Release = NewRelease();

        var page = Render();
        page.Find("button.pane-check-updates").Click();

        Assert.Empty(page.FindAll("button.pane-install-update"));
        Assert.Contains(".app bundle", page.Markup);
    }

    // ── Existing behaviour still holds ─────────────────────────────────────

    [Fact]
    public void The_hotkey_field_is_still_there_and_still_validates()
    {
        var page = Render();

        page.Find("input[name=hotkey]").Change("Space");

        Assert.Equal(SettingsStore.DefaultHotkey, _store.Load().Hotkey);
        Assert.NotNull(page.Find(".pane-warn"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Ui.Tests/Pane.Ui.Tests.csproj --filter GeneralPaneTests`
Expected: FAIL — no `.pane-version` element; `GeneralPane` does not inject `UpdateService` or `ILoginItem`.

- [ ] **Step 3: Rewrite `GeneralPane.razor`**

Replace the whole of `src/Pane.Ui/Settings/GeneralPane.razor` with:

```razor
@using Pane.Core.Settings
@using Pane.Core.Startup
@using Pane.Core.Updates
@implements IDisposable
@inject SettingsStore Settings
@inject UpdateService Updates
@inject ILoginItem LoginItem

<div class="pane-feature-settings">
    <label class="pane-setting">
        <span class="pane-setting-label">
            Global hotkey
            <span class="pane-setting-help">
                Modifier plus a key, e.g. <code>Alt+Space</code>.
                Takes effect when Pane restarts.
            </span>
        </span>
        <input type="text" name="hotkey" value="@_hotkey" @onchange="OnHotkeyChanged" />
    </label>

    @if (_error is not null)
    {
        <span class="pane-warn">@_error</span>
    }

    <label class="pane-setting">
        <span class="pane-setting-label">
            Start at login
            <span class="pane-setting-help">
                @(LoginItem.CanManage
                    ? "Pane launches hidden when you log in, so the hotkey is always live."
                    : LoginItem.UnavailableReason)
            </span>
        </span>
        <input type="checkbox" name="startAtLogin"
               checked="@LoginItem.IsEnabled"
               disabled="@(!LoginItem.CanManage)"
               @onchange="OnStartAtLoginChanged" />
    </label>

    <label class="pane-setting">
        <span class="pane-setting-label">
            Check for updates automatically
            <span class="pane-setting-help">
                Asks GitHub for a newer release once a day. Pane never installs
                one without you pressing Update.
            </span>
        </span>
        <input type="checkbox" name="autoCheckUpdates"
               checked="@_autoCheck" @onchange="OnAutoCheckChanged" />
    </label>

    <div class="pane-setting pane-update">
        <span class="pane-setting-label">
            <span class="pane-version">Pane v@Updates.CurrentVersion</span>
            <span class="pane-setting-help pane-update-status">@StatusText</span>
            @if (!Updates.CanInstall && Updates.InstallUnavailableReason is { } why)
            {
                <span class="pane-setting-help">@why</span>
            }
        </span>
        <span class="pane-update-actions">
            @if (Updates.Status is UpdateStatus.Available && Updates.CanInstall)
            {
                <button class="pane-install-update" @onclick="Install">Install and restart</button>
            }
            <button class="pane-check-updates" disabled="@Busy" @onclick="Check">Check now</button>
        </span>
    </div>
</div>

@code {
    string _hotkey = "";
    string? _error;
    bool _autoCheck;

    protected override void OnInitialized()
    {
        var settings = Settings.Load();
        _hotkey = settings.Hotkey;
        _autoCheck = settings.AutoCheckUpdates;
        Updates.StatusChanged += OnStatusChanged;
    }

    public void Dispose() => Updates.StatusChanged -= OnStatusChanged;

    // The service raises this from whichever thread ran the check, so hop back
    // to the renderer's context before touching component state.
    void OnStatusChanged() => InvokeAsync(StateHasChanged);

    bool Busy => Updates.Status is UpdateStatus.Checking
                                or UpdateStatus.Downloading
                                or UpdateStatus.Installing;

    string StatusText => Updates.Status switch
    {
        UpdateStatus.Checking => "Checking for updates…",
        UpdateStatus.UpToDate { CheckedAt: { } at } => $"Up to date — checked {Ago(at)}.",
        UpdateStatus.UpToDate => "Up to date.",
        UpdateStatus.Available a => $"Pane v{a.Release.Version} is available.",
        UpdateStatus.Downloading d => $"Downloading… {d.Percent}%",
        UpdateStatus.Installing => "Installing — Pane will restart.",
        UpdateStatus.Failed f => f.Message,
        _ => "",
    };

    static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
    }

    // Parse is lenient, so an unchecked combo would save happily and then never
    // fire — leaving no way back into settings except editing the file by hand.
    void OnHotkeyChanged(ChangeEventArgs e)
    {
        var combo = (e.Value?.ToString() ?? "").Trim();
        if (!HotkeyCombo.IsValid(combo))
        {
            _error = $"'{combo}' is not a usable hotkey — it needs a modifier and a key.";
            return;
        }

        _error = null;
        _hotkey = combo;
        Settings.Save(Settings.Load() with { Hotkey = combo });
    }

    void OnStartAtLoginChanged(ChangeEventArgs e)
    {
        if (!LoginItem.CanManage) return;

        // Load-modify-save through the live object, then re-read: the checkbox
        // must show what is actually on disk, not what we asked for.
        try
        {
            if ((bool)e.Value!) LoginItem.Enable();
            else LoginItem.Disable();
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    void OnAutoCheckChanged(ChangeEventArgs e)
    {
        _autoCheck = (bool)e.Value!;
        Settings.Save(Settings.Load() with { AutoCheckUpdates = _autoCheck });
    }

    Task Check() => Updates.CheckAsync(CancellationToken.None);
    Task Install() => Updates.InstallAsync(CancellationToken.None);
}
```

- [ ] **Step 4: Add the styles**

Append to `src/Pane.Ui/wwwroot/pane.css`:

```css
/* ── Update / startup controls ─────────────────────────────────────────── */
.pane-update {
    padding-top: 14px;
    border-top: 1px solid var(--border);
}

.pane-version {
    font-size: 13px;
    font-weight: 500;
}

.pane-update-status { min-height: 15px; }

.pane-update-actions {
    flex: 0 0 auto;
    display: flex;
    gap: 6px;
    align-items: flex-start;
}

.pane-update-actions button {
    background: var(--bg-elev);
    color: var(--fg);
    border: 1px solid var(--border);
    border-radius: 6px;
    padding: 5px 10px;
    font-size: 12px;
    font-family: inherit;
    cursor: pointer;
    transition: background 0.12s ease, border-color 0.12s ease;
}

.pane-update-actions button:hover:not(:disabled) { background: var(--sel); }

.pane-update-actions button:disabled {
    opacity: 0.5;
    cursor: default;
}

.pane-update-actions .pane-install-update {
    background: var(--accent);
    border-color: var(--accent);
    color: #fff;
    font-weight: 500;
}
```

- [ ] **Step 5: Register the new services in `SettingsPageTests`**

`SettingsPage` renders `GeneralPane`, which now injects two more services, so every existing test in that file would fail on an unregistered service. In `tests/Pane.Ui.Tests/SettingsPageTests.cs` add these usings:

```csharp
using Pane.Core.Startup;
using Pane.Core.Updates;
```

then add a minimal no-op login item and installer to the class:

```csharp
    sealed class NoInstaller : IUpdateInstaller
    {
        public bool CanInstall => false;
        public string? UnavailableReason => "not installed";
        public Task InstallAsync(ReleaseAsset asset, AppVersion expected,
                                 IProgress<int> progress, CancellationToken ct) =>
            Task.CompletedTask;
    }

    sealed class NoReleases : IReleaseSource
    {
        public Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct) =>
            Task.FromResult<ReleaseInfo?>(null);
    }
```

and register them at the end of the constructor, after the existing
`_ctx.Services.AddSingleton(dispatcher);`:

```csharp
        _ctx.Services.AddSingleton(new UpdateService(
            new NoReleases(), new NoInstaller(),
            new UpdateState(Path.Combine(Path.GetTempPath(), $"pane-ui-state-{Guid.NewGuid():N}.json")),
            new AppVersion(1, 2, 0)));
        _ctx.Services.AddSingleton<ILoginItem>(new UnsupportedLoginItem());
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test Pane.slnx`
Expected: PASS — the 15 new `GeneralPaneTests` plus every pre-existing test, including all of `SettingsPageTests`.

- [ ] **Step 7: Commit**

```bash
git add src/Pane.Ui/Settings/GeneralPane.razor src/Pane.Ui/wwwroot/pane.css \
        tests/Pane.Ui.Tests/GeneralPaneTests.cs tests/Pane.Ui.Tests/SettingsPageTests.cs
git commit -m "feat(settings): show version, start-at-login and update controls"
```

---

### Task 11: Wire it into the app

**Files:**
- Modify: `src/Pane.App/Program.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–10.
- Produces: no new API. Registers `IReleaseSource`, `IUpdateInstaller`, `UpdateService`, `ILoginItem` in DI and triggers the startup auto-check.

- [ ] **Step 1: Register the services**

In `src/Pane.App/Program.cs`, add the usings alongside the existing ones:

```csharp
using Pane.Core.Startup;
using Pane.Core.Updates;
```

Then, after the `builder.Services.AddSingleton<IFilePicker, MacFilePicker>();` line, add:

```csharp
// ── Updates + login item ───────────────────────────────────────────────────
// Both are macOS-only and both refuse to act when Pane is not running from an
// installed .app (a `dotnet run` dev session), reporting why in Settings rather
// than corrupting a checkout.
builder.Services.AddSingleton<IReleaseSource>(_ => new GitHubReleaseSource());
builder.Services.AddSingleton<IUpdateInstaller>(_ => new MacUpdateInstaller(
    quitApp: () => MacApp.Terminate()));
builder.Services.AddSingleton(sp => new UpdateService(
    sp.GetRequiredService<IReleaseSource>(),
    sp.GetRequiredService<IUpdateInstaller>(),
    new UpdateState(Path.Combine(dataRoot, "update-state.json")),
    AppVersionSource.Current));
builder.Services.AddSingleton<ILoginItem>(_ =>
    OperatingSystem.IsMacOS() ? new MacLoginItem() : new UnsupportedLoginItem());
```

- [ ] **Step 2: Add the terminate helper the installer needs**

`MacUpdateInstaller` must quit the app so the swap helper can take over. Append this method to `src/Pane.App/MacApp.cs`, inside the `MacApp` class:

```csharp
    /// <summary>
    /// Quits via NSApplication so the app tears down the way the Quit menu item
    /// does. Used by the updater to get out of the way of the swap helper.
    /// </summary>
    public static void Terminate()
    {
        try
        {
            var app = Send(GetClass("NSApplication"), Sel("sharedApplication"));
            if (app != IntPtr.Zero) SendVoid(app, Sel("terminate:"), IntPtr.Zero);
        }
        catch { Environment.Exit(0); }
    }
```

If `MacApp.cs` does not already declare `Send`, `SendVoid`, `GetClass` and `Sel`
with those exact signatures, use whatever equivalents it does declare — read the
file first and match its existing interop helpers rather than adding duplicates.

- [ ] **Step 3: Kick off the auto-check after the window is up**

In `src/Pane.App/Program.cs`, just before the `// ── Run ──` comment block, add:

```csharp
// ── Update check ───────────────────────────────────────────────────────────
// On a background task so a slow or offline network never delays startup, and
// re-armed every 6h so a long-running instance eventually crosses the 24h
// staleness boundary rather than checking once per launch and never again.
var updates = app.Services.GetRequiredService<UpdateService>();
_ = Task.Run(async () =>
{
    while (true)
    {
        try { await updates.MaybeAutoCheckAsync(settingsStore.Load().AutoCheckUpdates, CancellationToken.None); }
        catch { /* a failed check is already a status; never take the app down */ }
        await Task.Delay(TimeSpan.FromHours(6));
    }
});
```

- [ ] **Step 4: Verify it builds and every test still passes**

Run: `dotnet build Pane.slnx && dotnet test Pane.slnx`
Expected: build succeeds with no warnings introduced; all tests PASS.

- [ ] **Step 5: Verify the app actually runs**

Run: `dotnet run --project src/Pane.App`
Expected: the launcher window appears; pressing the hotkey toggles it; opening Settings shows **General** with a version line reading `Pane v0.0.0` (a dev run carries no stamped version), a **disabled** "Start at login" checkbox explaining Pane is not running from a bundle, and a working **Check now** button. Quit from the menu bar item.

- [ ] **Step 6: Commit**

```bash
git add src/Pane.App/Program.cs src/Pane.App/MacApp.cs
git commit -m "feat(app): wire up the updater and login item"
```

---

### Task 12: A real menu bar icon

**Files:**
- Modify: `src/Pane.App/MacStatusBar.cs:42-79`

**Interfaces:**
- Consumes: nothing. Independent of Tasks 1–11.
- Produces: no API change — `MacStatusBar.Setup(string title, Action onClick)` keeps its signature; `title` becomes the accessibility description and the text fallback.

- [ ] **Step 1: Add the interop signatures the icon needs**

In `src/Pane.App/MacStatusBar.cs`, add these next to the existing `objc_msgSend` declarations (around line 32):

```csharp
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendTwoPtr(IntPtr r, IntPtr s, IntPtr a, IntPtr b);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool a);
```

- [ ] **Step 2: Replace the text title with a template image**

In `Setup`, replace this block:

```csharp
        var titlePtr = Marshal.StringToHGlobalAnsi(title);
        var nsTitle = SendPtr(GetClass("NSString"), Sel("stringWithUTF8String:"), titlePtr);
        Marshal.FreeHGlobal(titlePtr);
        SendVoid(button, Sel("setTitle:"), nsTitle);
```

with:

```csharp
        SetButtonIcon(button, title);
```

and add this method to the class:

```csharp
    /// <summary>
    /// An SF Symbol as a template image — macOS then inverts it for light and
    /// dark menu bars automatically, and it takes far less width than the text
    /// title it replaces. SF Symbols require macOS 11, which the bundle's
    /// LSMinimumSystemVersion already demands.
    ///
    /// If the symbol cannot be loaded we fall back to the text title: an empty
    /// status item would be invisible and unclickable, which is worse than ugly.
    /// </summary>
    static void SetButtonIcon(IntPtr button, string title)
    {
        var image = SendTwoPtr(
            GetClass("NSImage"),
            Sel("imageWithSystemSymbolName:accessibilityDescription:"),
            NSString(SymbolName),
            NSString(title));

        if (image != IntPtr.Zero)
        {
            SendBool(image, Sel("setTemplate:"), true);
            SendVoid(button, Sel("setImage:"), image);
            return;
        }

        SendVoid(button, Sel("setTitle:"), NSString(title));
    }
```

Add the symbol name as a constant next to the other `const` fields near the top of the class:

```csharp
    // The launcher's own glyph. Changing this changes the menu bar icon.
    const string SymbolName = "magnifyingglass";
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build Pane.slnx`
Expected: build succeeds.

- [ ] **Step 4: Verify the icon by eye**

Run: `dotnet run --project src/Pane.App`

Expected: the menu bar shows a **magnifying-glass icon**, not the word "Pane". Confirm all of:
- left-clicking it toggles the launcher;
- right-clicking it opens the Open / Quit menu;
- the icon is legible in both themes — switch with System Settings → Appearance → Light / Dark.

Quit from the menu bar item when done.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.App/MacStatusBar.cs
git commit -m "feat(menubar): show an SF Symbol icon instead of the text title"
```

---

### Task 13: Document the new behaviour

**Files:**
- Modify: `README.md` (the "Settings" and "Install" sections)

**Interfaces:**
- Consumes: the finished behaviour of Tasks 1–12.
- Produces: nothing code-facing.

- [ ] **Step 1: Describe the icon**

In `README.md`, the Usage section currently says the menu bar item is labelled
"Pane". Replace:

```
lives in the menu bar — left-click the **Pane** item to open the launcher,
right-click it for **Open Pane** and **Quit Pane**.
```

with:

```
lives in the menu bar — left-click the magnifying-glass icon to open the
launcher, right-click it for **Open Pane** and **Quit Pane**.
```

- [ ] **Step 2: Describe the new General settings**

In the Settings section, replace the **General** bullet:

```
- **General** — the global hotkey (e.g. `Ctrl+Space`). A combo needs a modifier and
  a key; anything else is refused rather than saved. Applies when Pane restarts.
```

with:

```
- **General** — the global hotkey (e.g. `Ctrl+Space`); a combo needs a modifier
  and a key, and anything else is refused rather than saved (it applies when
  Pane restarts). Also **Start at login**, which registers Pane's LaunchAgent so
  the hotkey is live from the moment you log in, and the updater: Pane's version,
  a daily **check for updates** against this repo's GitHub releases, and an
  **Install and restart** button when a newer release is out.
```

- [ ] **Step 3: Add an Updating section**

After the Install section, add:

```markdown
## Updating

Pane checks GitHub for a newer release once a day and shows it in
**Settings → General**. Pressing **Install and restart** downloads the release
for your architecture, verifies it is a well-formed, newer `Pane.app`, replaces
the installed bundle, and relaunches. Nothing is replaced until that check
passes, and a failed swap restores the app you had.

Turn the daily check off with **Check for updates automatically**; **Check now**
always works regardless.

Pane is not code-signed or notarized, so this trusts HTTPS and GitHub's control
of the release assets — the same trust `install.sh` already asks for. It is not
signature verification.

Updating needs Pane to be running from an installed `Pane.app`; a `dotnet run`
development session says so and disables the button.
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: describe the menu bar icon, login item and updater"
```

---

## Final verification

- [ ] **Run the whole suite**

Run: `dotnet test Pane.slnx`
Expected: every test passes. Record the actual pass/fail counts — do not claim success without reading the output.

- [ ] **Build a real bundle and confirm the version is stamped**

```bash
RID="$(uname -m | sed 's/^arm64$/osx-arm64/; s/^x86_64$/osx-x64/')"
bash build/make-app.sh "$RID" /tmp/pane-final
/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' /tmp/pane-final/Pane.app/Contents/Info.plist
open /tmp/pane-final/Pane.app
```

Expected: the plist prints the manifest version (not `1.0`), and the launched
app's Settings → General shows that same version, an **enabled** Start at login
checkbox, and a working Check now.

- [ ] **Exercise the login item for real**

Tick **Start at login**, then confirm the agent is registered:

```bash
launchctl print "gui/$(id -u)/com.pane.launcher" | head -5
```

Expected: prints the service, not "Could not find service". Untick it and
confirm the same command now fails. Clean up with `rm -rf /tmp/pane-final`.

- [ ] **Known limitation to report, not to fix**

The updater can only find a release once one carries a `Pane-osx-*.zip` asset.
Today's newest release is `plugins-v1.2.0` with only plugin zips, so until the
first `pane-v*` release is cut by the workflow, **Check now** correctly reports
"Up to date". Verifying a real end-to-end update requires that release to exist;
say so plainly rather than implying the update path has been proven.
