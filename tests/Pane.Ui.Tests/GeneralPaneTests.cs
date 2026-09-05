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
