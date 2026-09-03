using System.Text.Json.Nodes;
using Pane.Core.Contracts;
using Pane.Core.Features.Apps;
using Xunit;

public class AppsFeatureTests : IDisposable
{
    readonly string _home = Path.Combine(Path.GetTempPath(), $"pane-apps-{Guid.NewGuid():N}");

    public AppsFeatureTests() => Directory.CreateDirectory(_home);
    public void Dispose() { try { Directory.Delete(_home, true); } catch { } }

    string AppDir(string relative, params string[] apps)
    {
        var dir = Path.Combine(_home, relative);
        foreach (var a in apps) Directory.CreateDirectory(Path.Combine(dir, a + ".app"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    async Task<AppsFeature> BuildAsync(JsonObject? values = null)
    {
        var f = new AppsFeature(new MacAppIndexer());
        await f.InitializeAsync(new FeatureContext(_home, _home), CancellationToken.None);
        f.ApplyConfig(FeatureConfig.Resolve(f.Descriptor, f.Settings, values ?? new JsonObject()));
        return f;
    }

    async Task<List<string>> NamesAsync(AppsFeature f)
    {
        var names = new List<string>();
        await foreach (var r in f.QueryAsync(new PaneQuery("", null, ""), CancellationToken.None))
            names.Add(r.Title);
        return names;
    }

    [Fact]
    public async Task Indexes_the_configured_directories()
    {
        var dir = AppDir("Apps", "Safari", "Notes");
        var f = await BuildAsync(new JsonObject { ["dirs"] = new JsonArray(dir) });

        Assert.Equal(new[] { "Notes", "Safari" }, (await NamesAsync(f)).OrderBy(n => n));
    }

    [Fact]
    public async Task Tilde_in_a_configured_directory_expands_to_the_host_supplied_home()
    {
        AppDir("Applications", "Mail");
        var f = await BuildAsync(new JsonObject { ["dirs"] = new JsonArray("~/Applications") });

        Assert.Equal(new[] { "Mail" }, await NamesAsync(f));
    }

    [Fact]
    public async Task Re_indexes_when_the_directories_setting_changes()
    {
        var first = AppDir("First", "Alpha");
        var second = AppDir("Second", "Beta");

        var f = await BuildAsync(new JsonObject { ["dirs"] = new JsonArray(first) });
        Assert.Equal(new[] { "Alpha" }, await NamesAsync(f));

        f.ApplyConfig(FeatureConfig.Resolve(f.Descriptor, f.Settings,
            new JsonObject { ["dirs"] = new JsonArray(second) }));

        Assert.Equal(new[] { "Beta" }, await NamesAsync(f));
    }

    [Fact]
    public async Task Is_unavailable_when_no_configured_directory_holds_an_application()
    {
        var f = await BuildAsync(new JsonObject { ["dirs"] = new JsonArray(Path.Combine(_home, "empty")) });

        var availability = f.CheckAvailability();
        Assert.False(availability.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(availability.Reason));
    }

    [Fact]
    public async Task Is_available_once_a_configured_directory_holds_an_application()
    {
        var dir = AppDir("Apps", "Safari");
        var f = await BuildAsync(new JsonObject { ["dirs"] = new JsonArray(dir) });

        Assert.True(f.CheckAvailability().IsAvailable);
    }
}
