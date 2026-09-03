using System.Text.Json.Nodes;
using Pane.Core.Contracts;
using Pane.Core.Features.Scripts;
using Xunit;

public class ScriptsFeatureTests : IDisposable
{
    readonly string _home = Path.Combine(Path.GetTempPath(), $"pane-scripts-{Guid.NewGuid():N}");

    public ScriptsFeatureTests() => Directory.CreateDirectory(_home);
    public void Dispose() { try { Directory.Delete(_home, true); } catch { } }

    string WriteScript(string relativeDir, string fileName, string body = "#!/bin/sh\ntrue\n")
    {
        var dir = Path.Combine(_home, relativeDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, body);
        return path;
    }

    async Task<ScriptsFeature> BuildAsync(JsonObject? values = null)
    {
        var f = new ScriptsFeature();
        await f.InitializeAsync(new FeatureContext(_home, _home), CancellationToken.None);
        f.ApplyConfig(FeatureConfig.Resolve(f.Descriptor, f.Settings, values ?? new JsonObject()));
        return f;
    }

    async Task<List<PaneResult>> ResultsAsync(ScriptsFeature f)
    {
        var results = new List<PaneResult>();
        await foreach (var r in f.QueryAsync(new PaneQuery("", null, ""), CancellationToken.None))
            results.Add(r);
        return results;
    }

    [Fact]
    public async Task Scans_the_default_script_folders_under_the_host_supplied_home()
    {
        WriteScript(".config/pane/scripts", "deploy.sh");
        WriteScript(".config/sol/scripts", "legacy.sh");

        var titles = (await ResultsAsync(await BuildAsync())).Select(r => r.Title).OrderBy(t => t);
        Assert.Equal(new[] { "deploy", "legacy" }, titles);
    }

    [Fact]
    public async Task Scans_configured_folders_instead_of_the_defaults()
    {
        WriteScript(".config/pane/scripts", "ignored.sh");
        WriteScript("custom", "mine.sh");

        var f = await BuildAsync(new JsonObject { ["dirs"] = new JsonArray(Path.Combine(_home, "custom")) });
        Assert.Equal("mine", Assert.Single(await ResultsAsync(f)).Title);
    }

    [Fact]
    public async Task Only_the_configured_extensions_are_offered()
    {
        WriteScript("custom", "shell.sh");
        WriteScript("custom", "python.py");
        WriteScript("custom", "notes.txt");

        var f = await BuildAsync(new JsonObject
        {
            ["dirs"] = new JsonArray(Path.Combine(_home, "custom")),
            ["extensions"] = new JsonArray(".sh", ".py"),
        });

        var titles = (await ResultsAsync(f)).Select(r => r.Title).OrderBy(t => t);
        Assert.Equal(new[] { "python", "shell" }, titles);
    }

    [Fact]
    public async Task Is_unavailable_when_no_configured_folder_exists()
    {
        var f = await BuildAsync(new JsonObject { ["dirs"] = new JsonArray(Path.Combine(_home, "nowhere")) });

        var availability = f.CheckAvailability();
        Assert.False(availability.IsAvailable);
        Assert.Contains("nowhere", availability.Reason);
    }

    [Fact]
    public async Task Is_available_once_a_configured_folder_exists()
    {
        WriteScript(".config/pane/scripts", "deploy.sh");
        Assert.True((await BuildAsync()).CheckAvailability().IsAvailable);
    }

    [Fact]
    public async Task Runs_the_script_with_the_configured_shell()
    {
        var marker = Path.Combine(_home, "ran.txt");
        WriteScript("custom", "touchit.sh", $"echo hi > '{marker}'\n");

        var f = await BuildAsync(new JsonObject
        {
            ["dirs"] = new JsonArray(Path.Combine(_home, "custom")),
            ["shell"] = "/bin/sh",
        });

        await Assert.Single(await ResultsAsync(f)).Activate();

        for (var i = 0; i < 60 && !File.Exists(marker); i++) await Task.Delay(50);
        Assert.True(File.Exists(marker), "the script did not run");
    }
}
