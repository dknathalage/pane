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
