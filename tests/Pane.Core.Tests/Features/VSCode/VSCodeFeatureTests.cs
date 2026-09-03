using System.Text.Json.Nodes;
using Pane.Core.Contracts;
using Pane.Core.Features.VSCode;
using Xunit;

public class VSCodeFeatureTests : IDisposable
{
    readonly string _home = Path.Combine(Path.GetTempPath(), $"pane-vscode-{Guid.NewGuid():N}");

    public VSCodeFeatureTests() => Directory.CreateDirectory(_home);
    public void Dispose() { try { Directory.Delete(_home, true); } catch { } }

    string Repo(string name)
    {
        var dir = Path.Combine(_home, "repos", name);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        return dir;
    }

    string FakeEditor()
    {
        var path = Path.Combine(_home, "code");
        File.WriteAllText(path, "#!/bin/sh\n");
        return path;
    }

    async Task<VSCodeFeature> BuildAsync(JsonObject? values = null)
    {
        var f = new VSCodeFeature();
        await f.InitializeAsync(new FeatureContext(_home, _home), CancellationToken.None);
        f.ApplyConfig(FeatureConfig.Resolve(f.Descriptor, f.Settings, values ?? new JsonObject()));
        return f;
    }

    async Task<List<string>> TitlesAsync(VSCodeFeature f)
    {
        var titles = new List<string>();
        await foreach (var r in f.QueryAsync(new PaneQuery("", null, ""), CancellationToken.None))
            titles.Add(r.Title);
        return titles;
    }

    [Fact]
    public async Task Is_unavailable_when_the_configured_editor_does_not_exist()
    {
        Repo("alpha");
        var f = await BuildAsync(new JsonObject { ["editor"] = Path.Combine(_home, "no-such-editor") });

        var availability = f.CheckAvailability();
        Assert.False(availability.IsAvailable);
        Assert.Contains("no-such-editor", availability.Reason);
    }

    [Fact]
    public async Task Is_available_when_the_editor_and_a_repo_folder_both_exist()
    {
        Repo("alpha");
        var f = await BuildAsync(new JsonObject { ["editor"] = FakeEditor() });

        Assert.True(f.CheckAvailability().IsAvailable);
    }

    [Fact]
    public async Task Is_unavailable_when_no_configured_repo_folder_exists()
    {
        var f = await BuildAsync(new JsonObject
        {
            ["editor"] = FakeEditor(),
            ["repoDirs"] = new JsonArray(Path.Combine(_home, "nowhere")),
        });

        var availability = f.CheckAvailability();
        Assert.False(availability.IsAvailable);
        Assert.Contains("nowhere", availability.Reason);
    }

    [Fact]
    public async Task Availability_is_re_evaluated_after_the_editor_setting_changes()
    {
        Repo("alpha");
        var f = await BuildAsync(new JsonObject { ["editor"] = Path.Combine(_home, "missing") });
        Assert.False(f.CheckAvailability().IsAvailable);

        f.ApplyConfig(FeatureConfig.Resolve(f.Descriptor, f.Settings,
            new JsonObject { ["editor"] = FakeEditor() }));

        Assert.True(f.CheckAvailability().IsAvailable);
    }

    [Fact]
    public async Task Scans_the_default_repo_folder_under_the_host_supplied_home()
    {
        Repo("alpha");
        Repo("beta");
        var f = await BuildAsync();

        Assert.Equal(new[] { "alpha", "beta" }, (await TitlesAsync(f)).OrderBy(t => t));
    }

    [Fact]
    public async Task Scans_every_configured_repo_folder()
    {
        Repo("alpha");
        var extra = Path.Combine(_home, "work");
        Directory.CreateDirectory(Path.Combine(extra, "gamma", ".git"));

        var f = await BuildAsync(new JsonObject { ["repoDirs"] = new JsonArray("~/repos", extra) });

        Assert.Equal(new[] { "alpha", "gamma" }, (await TitlesAsync(f)).OrderBy(t => t));
    }

    [Fact]
    public async Task Tilde_in_a_configured_path_expands_to_the_host_supplied_home()
    {
        Repo("alpha");
        var f = await BuildAsync(new JsonObject { ["repoDirs"] = new JsonArray("~/repos") });

        Assert.Equal(new[] { "alpha" }, await TitlesAsync(f));
    }
}
