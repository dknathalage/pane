using System.IO.Compression;
using System.Net;
using Pane.Core.Plugins;
using Xunit;

public class PluginInstallUpdateTests
{
    static string FixtureDir(string name)
    {
        var config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name, "bin", config, "net10.0"));
    }

    static byte[] ZipDir(string dir)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var f in Directory.GetFiles(dir))
            {
                var e = zip.CreateEntry(Path.GetFileName(f));
                using var es = e.Open();
                using var fs = File.OpenRead(f);
                fs.CopyTo(es);
            }
        return ms.ToArray();
    }

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fn(request));
    }

    static PluginManager MgrServing(string dataRoot, byte[] zipBytes)
    {
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) }));
        return new PluginManager(dataRoot, store: null, fetcher: new PluginFetcher(http));
    }

    static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pane-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task InstallFromUrl_installs_and_loads_the_plugin()
    {
        var root = NewRoot();
        var zip = ZipDir(FixtureDir("TestPlugin"));
        var mgr = MgrServing(root, zip);

        var entry = await mgr.InstallFromUrlAsync("https://example/testplugin.zip");

        Assert.Equal("test", entry.Metadata.Id);
        Assert.Equal(PluginState.Enabled, entry.State);
        Assert.Single(mgr.Active());
        // Copied under plugins/<id>/
        Assert.True(File.Exists(Path.Combine(root, "plugins", "test", "TestPlugin.dll")));
    }

    [Theory]
    [InlineData("1.0", "2.0", true)]
    [InlineData("2.0", "2.0", false)]
    [InlineData("2.0", "1.0", false)]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("beta", "beta", false)]     // non-semver equal → no update
    [InlineData("beta", "rc1", true)]        // non-semver differs → update
    public void IsUpdateAvailable_compares_versions(string installed, string remote, bool expected)
        => Assert.Equal(expected, PluginVersion.IsUpdateAvailable(installed, remote));

    // Serves v1 bytes for one url and v2 bytes for another.
    static PluginManager MgrServingMany(string dataRoot, Dictionary<string, byte[]> byUrl)
    {
        var http = new HttpClient(new StubHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(byUrl[req.RequestUri!.ToString()])
            }));
        return new PluginManager(dataRoot, store: null, fetcher: new PluginFetcher(http));
    }

    [Fact]
    public async Task CheckForUpdate_detects_a_newer_version()
    {
        var root = NewRoot();
        var v1 = "https://example/v1.zip";
        var v2 = "https://example/v2.zip";
        var mgr = MgrServingMany(root, new()
        {
            [v1] = ZipDir(FixtureDir("TestPlugin")),
            [v2] = ZipDir(FixtureDir("TestPluginV2")),
        });

        await mgr.InstallFromUrlAsync(v1);
        var check = await mgr.CheckForUpdateAsync("test", v2);

        Assert.True(check.Available);
        Assert.Equal("1.0", check.InstalledVersion);
        Assert.Equal("2.0", check.RemoteVersion);
    }

    [Fact]
    public async Task Update_replaces_the_plugin_in_place()
    {
        var root = NewRoot();
        var v1 = "https://example/v1.zip";
        var v2 = "https://example/v2.zip";
        var mgr = MgrServingMany(root, new()
        {
            [v1] = ZipDir(FixtureDir("TestPlugin")),
            [v2] = ZipDir(FixtureDir("TestPluginV2")),
        });

        await mgr.InstallFromUrlAsync(v1);
        Assert.Equal("1.0", mgr.List().Single().Metadata.Version);

        await mgr.UpdateAsync("test", v2);

        Assert.Equal("2.0", mgr.List().Single().Metadata.Version);
        Assert.Single(mgr.Active());
    }

    [Fact]
    public async Task Update_with_bad_zip_leaves_old_plugin_loaded()
    {
        // Install v1 first.
        var root = NewRoot();
        var v1 = "https://example/v1.zip";
        var bad = "https://example/bad.zip";
        var mgr = MgrServingMany(root, new()
        {
            [v1] = ZipDir(FixtureDir("TestPlugin")),
            // A zip that contains no plugin dll — just a text file.
            [bad] = MakeZipWithNoPlugin(),
        });

        await mgr.InstallFromUrlAsync(v1);
        Assert.Equal("1.0", mgr.List().Single().Metadata.Version);

        // The update should fail because the archive has no plugin dll.
        await Assert.ThrowsAsync<PluginFetchException>(() => mgr.UpdateAsync("test", bad));

        // Old plugin must still be loaded and active at version 1.0.
        var entry = mgr.List().Single();
        Assert.Equal("1.0", entry.Metadata.Version);
        Assert.Equal(PluginState.Enabled, entry.State);
        Assert.Single(mgr.Active());
    }

    // Creates a zip that contains a text file but no .dll — simulates a corrupted/wrong update archive.
    static byte[] MakeZipWithNoPlugin()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("readme.txt");
            using var es = e.Open();
            es.Write("not a plugin"u8);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task InstallFromUrl_copies_subdirectory_files()
    {
        // Build a zip that has a plugin dll at the root AND a subdirectory file.
        var root = NewRoot();
        var pluginDir = FixtureDir("TestPlugin");
        var zipBytes = ZipDirWithSubdir(pluginDir, "subdir", "extra.dat", "hello"u8.ToArray());
        var mgr = MgrServing(root, zipBytes);

        await mgr.InstallFromUrlAsync("https://example/testplugin.zip");

        // The subdir file must have been copied recursively under plugins/test/subdir/extra.dat.
        Assert.True(File.Exists(Path.Combine(root, "plugins", "test", "subdir", "extra.dat")));
    }

    // Zips dir's files + a synthetic subdirectory entry.
    static byte[] ZipDirWithSubdir(string dir, string subdirName, string fileName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                var e = zip.CreateEntry(Path.GetFileName(f));
                using var es = e.Open();
                using var fs = File.OpenRead(f);
                fs.CopyTo(es);
            }
            var sub = zip.CreateEntry($"{subdirName}/{fileName}");
            using var subEs = sub.Open();
            subEs.Write(content);
        }
        return ms.ToArray();
    }
}
