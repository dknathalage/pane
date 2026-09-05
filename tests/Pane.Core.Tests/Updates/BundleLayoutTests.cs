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
                      bool withExecutable = true, bool multilinePlist = false)
    {
        var app = Path.Combine(_root, name);
        var macOs = Path.Combine(app, "Contents", "MacOS");
        Directory.CreateDirectory(macOs);

        if (withExecutable)
            File.WriteAllText(Path.Combine(macOs, BundleLayout.ExecutableName), "#!/bin/sh\n");

        if (version is not null)
        {
            var plistContent = multilinePlist
                ? $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                      <key>CFBundleName</key>
                      <string>Pane</string>
                      <key>CFBundleShortVersionString</key>
                      <string>{version}</string>
                    </dict>
                    </plist>
                    """
                : $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                      <key>CFBundleName</key><string>Pane</string>
                      <key>CFBundleShortVersionString</key><string>{version}</string>
                    </dict>
                    </plist>
                    """;

            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), plistContent);
        }

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

    [Fact]
    public void The_bundle_version_is_read_from_multiline_plist()
    {
        // Test that multiline plists (with newlines/indentation between tags) work.
        // This would have caught the NextNode bug that skips whitespace text nodes.
        Assert.Equal(new AppVersion(2, 1, 5),
            BundleLayout.ReadBundleVersion(MakeBundle(version: "2.1.5", multilinePlist: true)));
    }

    // ── Validation ─────────────────────────────────────────────────────────

    [Fact]
    public void A_well_formed_newer_bundle_validates()
    {
        Assert.Null(BundleLayout.Validate(MakeBundle(version: "1.3.0"), new AppVersion(1, 2, 0)));
    }

    [Fact]
    public void A_multiline_plist_bundle_with_newer_version_validates()
    {
        // Multiline plists (normally formatted with whitespace) must also validate correctly.
        Assert.Null(BundleLayout.Validate(MakeBundle(version: "2.0.0", multilinePlist: true), new AppVersion(1, 5, 0)));
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
    public void A_bundle_strictly_older_than_current_is_rejected()
    {
        // Even older versions (not just equal) must be rejected.
        var reason = BundleLayout.Validate(MakeBundle(version: "1.1.0"), new AppVersion(1, 2, 0));

        Assert.NotNull(reason);
        Assert.Contains("1.1.0", reason);
    }

    [Fact]
    public void A_missing_bundle_is_rejected()
    {
        var reason = BundleLayout.Validate(
            Path.Combine(_root, "Nope.app"), new AppVersion(1, 2, 0));

        Assert.NotNull(reason);
    }
}
