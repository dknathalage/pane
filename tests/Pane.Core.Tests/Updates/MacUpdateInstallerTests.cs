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

    // Deliberately non-overlapping roots: if newBundle sat inside payloadDir
    // (as an earlier version of this fixture had it), asserting on the payload
    // path would already be satisfied by the NEW= line alone, and the cleanup
    // test below could not fail even if the actual cleanup line were deleted.
    static string Script() => MacUpdateInstaller.BuildHelperScript(
        pid: 4242,
        newBundle: "/tmp/pane-extracted/Pane.app",
        target: "/Users/someone/Applications/Pane.app",
        payloadDir: "/tmp/pane-payload");

    [Fact]
    public void The_script_waits_for_our_process_to_exit_before_touching_anything()
    {
        var script = Script();

        Assert.Contains("kill -0 4242", script);
    }

    [Fact]
    public void The_script_moves_the_old_bundle_aside_rather_than_deleting_it_outright()
    {
        // Exact statements, not just "mv " and ".pane-old" as loose substrings
        // — both of those already appear in the *restore* line and the BACKUP
        // assignment respectively, so they would still be present even if this
        // specific move (the one that makes the bundle recoverable at all) were
        // deleted outright.
        var script = Script();

        Assert.Contains("BACKUP=\"$TARGET.pane-old\"", script);
        Assert.Contains("mv \"$TARGET\" \"$BACKUP\" || exit 1", script);
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
        // Exact final rm -rf on the payload variable, not just presence of the
        // path somewhere in the script — see the fixture comment above for why
        // a loose Contains(payloadDir) would not have caught this line being
        // deleted.
        var script = Script();

        Assert.Contains("PAYLOAD='/tmp/pane-payload'", script);
        Assert.Contains("rm -rf \"$PAYLOAD\"", script);
    }

    [Fact]
    public void The_script_quotes_every_path_so_spaces_do_not_split_arguments()
    {
        // "~/Applications/Pane.app" is fine, but a user's disk may not be.
        var script = MacUpdateInstaller.BuildHelperScript(
            1, "/tmp/a b/Pane.app", "/Users/x/My Apps/Pane.app", "/tmp/a b");

        Assert.Contains("'/Users/x/My Apps/Pane.app'", script);
        Assert.Contains("'/tmp/a b/Pane.app'", script);
    }

    [Fact]
    public void A_dollar_sign_or_single_quote_in_a_path_survives_literally_rather_than_expanding()
    {
        // Double-quoted interpolation (the original implementation) lets the
        // shell expand "$HOME" or run a command substitution living inside a
        // path. Single-quoting blocks that — the one thing it can't hold
        // directly, a literal single quote, must be escaped as '\''.
        var script = MacUpdateInstaller.BuildHelperScript(
            1, "/tmp/n$(rm -rf ~)/Pane.app", "/Users/o'brien/$HOME/Pane.app", "/tmp/payload");

        Assert.Contains("'/tmp/n$(rm -rf ~)/Pane.app'", script);
        Assert.Contains("'/Users/o'\\''brien/$HOME/Pane.app'", script);
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
