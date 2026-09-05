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
        public AppVersion? MustExceed;
        public Exception? Throw;

        public Task InstallAsync(ReleaseAsset asset, AppVersion mustExceed,
                                 IProgress<int> progress, CancellationToken ct)
        {
            Installed = asset;
            MustExceed = mustExceed;
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
    public async Task A_timeout_becomes_a_readable_failure_not_a_silent_idle()
    {
        // HttpClient throws TaskCanceledException (which derives from
        // OperationCanceledException) on a timeout even when nobody asked to
        // cancel. With a token that was never cancelled, that must surface as
        // Failed, not be swallowed as if the caller cancelled.
        var source = new FakeSource { Throw = new TaskCanceledException("timed out") };
        var svc = Service(source, new());

        await svc.CheckAsync(CancellationToken.None);

        var failed = Assert.IsType<UpdateStatus.Failed>(svc.Status);
        Assert.Equal("The update check timed out.", failed.Message);
    }

    [Fact]
    public async Task Genuine_caller_cancellation_leaves_the_service_idle()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var source = new FakeSource { Throw = new OperationCanceledException(cts.Token) };
        var svc = Service(source, new());

        await svc.CheckAsync(cts.Token);

        Assert.IsType<UpdateStatus.Idle>(svc.Status);
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
    public async Task Installing_requires_the_bundle_to_exceed_the_RUNNING_version_not_the_releases()
    {
        // Regression: make-app.sh stamps CFBundleShortVersionString from the
        // same tag as the GitHub release, so a downloaded bundle's own version
        // always EQUALS the release's version. Passing the release's version as
        // "mustExceed" would make BundleLayout.Validate reject every real
        // release ("1.3.0 is not newer than 1.3.0"). It must be the version
        // this process is currently running.
        var installer = new FakeInstaller();
        var svc = Service(new FakeSource { Release = ReleaseWithAsset("1.3.0") }, installer,
            current: "1.2.0");
        await svc.CheckAsync(CancellationToken.None);

        await svc.InstallAsync(CancellationToken.None);

        Assert.Equal(new AppVersion(1, 2, 0), installer.MustExceed);
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
    public async Task A_failed_install_reports_why_it_failed()
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
