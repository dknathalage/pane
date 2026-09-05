using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Pane.Core.Contracts;
using Pane.Core.Query;
using Pane.Core.Settings;
using Pane.Core.Startup;
using Pane.Core.Updates;
using Pane.Ui.Settings;
using Pane.Ui.Tests;
using Xunit;

public class SettingsPageTests : IDisposable
{
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

    readonly string _path = Path.Combine(Path.GetTempPath(), $"pane-ui-{Guid.NewGuid():N}.json");
    readonly TestContext _ctx = new();
    readonly SettingsStore _store;
    readonly DemoFeature _demo = new();
    readonly DemoFeature _other = new("other", "Other", keyword: null);

    public SettingsPageTests()
    {
        _store = new SettingsStore(_path);
        var dispatcher = new QueryDispatcher(
            new FuzzyMatcher(), _store, new IPaneFeature[] { _demo, _other });
        _ctx.Services.AddSingleton(_store);
        _ctx.Services.AddSingleton(dispatcher);
        _ctx.Services.AddSingleton(new UpdateService(
            new NoReleases(), new NoInstaller(),
            new UpdateState(Path.Combine(Path.GetTempPath(), $"pane-ui-state-{Guid.NewGuid():N}.json")),
            new AppVersion(1, 2, 0)));
        _ctx.Services.AddSingleton<ILoginItem>(new UnsupportedLoginItem());
    }

    public void Dispose()
    {
        _ctx.Dispose();
        try { File.Delete(_path); } catch { }
    }

    IRenderedComponent<SettingsPage> Render() => _ctx.RenderComponent<SettingsPage>();

    static IElement Tab(IRenderedComponent<SettingsPage> page, string label) =>
        page.FindAll(".settings-tab").Single(t => t.TextContent.Contains(label));

    // ── Sidebar ────────────────────────────────────────────────────────────

    [Fact]
    public void Sidebar_lists_general_then_every_feature()
    {
        var tabs = Render().FindAll(".settings-tab").Select(t => t.TextContent.Trim()).ToList();

        Assert.Equal("General", tabs[0]);
        Assert.Contains(tabs, t => t.Contains("Demo"));
        Assert.Contains(tabs, t => t.Contains("Other"));
    }

    [Fact]
    public void General_is_selected_when_the_page_opens()
    {
        var page = Render();

        Assert.Contains("active", Tab(page, "General").ClassName);
        Assert.NotNull(page.Find(".settings-detail input[name=hotkey]"));
    }

    [Fact]
    public void Selecting_a_feature_marks_its_tab_active_and_deselects_general()
    {
        var page = Render();

        Tab(page, "Demo").Click();

        Assert.Contains("active", Tab(page, "Demo").ClassName);
        Assert.DoesNotContain("active", Tab(page, "General").ClassName);
    }

    [Fact]
    public void Selecting_a_feature_shows_that_features_settings()
    {
        var page = Render();

        Tab(page, "Demo").Click();

        var labels = page.FindAll(".pane-setting-label").Select(e => e.TextContent).ToList();
        Assert.Contains(labels, l => l.Contains("Keyword"));
        Assert.Contains(labels, l => l.Contains("Priority"));
        Assert.Contains(labels, l => l.Contains("Max results"));
        Assert.Contains(labels, l => l.Contains("Folders"));
    }

    [Fact]
    public void Only_the_selected_features_settings_are_shown()
    {
        var page = Render();

        Tab(page, "Demo").Click();

        Assert.Single(page.FindAll(".settings-detail .pane-feature-header"));
    }

    [Fact]
    public void An_unavailable_feature_is_marked_in_the_sidebar()
    {
        _demo.Availability = FeatureAvailability.Unavailable("code binary not found");

        var marker = Render().Find(".settings-tab .settings-tab-state");

        Assert.Contains("unavailable", marker.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_disabled_feature_is_marked_in_the_sidebar()
    {
        var page = Render();
        Tab(page, "Demo").Click();
        page.Find(".pane-feature-header input[type=checkbox]").Change(false);

        var marker = Tab(page, "Demo").QuerySelector(".settings-tab-state");
        Assert.Contains("off", marker!.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    // ── Feature detail ─────────────────────────────────────────────────────

    [Fact]
    public void The_detail_header_shows_why_a_feature_is_unavailable()
    {
        _demo.Availability = FeatureAvailability.Unavailable("code binary not found");
        var page = Render();

        Tab(page, "Demo").Click();

        Assert.Contains("code binary not found", page.Find(".pane-warn").TextContent);
    }

    [Fact]
    public void Editing_a_number_setting_persists_it()
    {
        var page = Render();
        Tab(page, "Demo").Click();

        page.FindAll("input[type=number]").Last().Change("7");

        Assert.Equal(7, (int)_store.Load().Features["demo"]["maxResults"]!);
    }

    [Fact]
    public void Editing_the_keyword_persists_it()
    {
        var page = Render();
        Tab(page, "Demo").Click();

        page.Find(".settings-detail input[type=text]").Change("!");

        Assert.Equal("!", (string)_store.Load().Features["demo"]["keyword"]!);
    }

    [Fact]
    public void Editing_a_path_list_persists_one_entry_per_line()
    {
        var page = Render();
        Tab(page, "Demo").Click();

        page.Find(".settings-detail textarea").Change("/a\n/b\n");

        var dirs = _store.Load().Features["demo"]["dirs"]!.AsArray().Select(d => (string)d!);
        Assert.Equal(new[] { "/a", "/b" }, dirs);
    }

    [Fact]
    public void Toggling_a_feature_off_persists_it()
    {
        var page = Render();
        Tab(page, "Demo").Click();

        page.Find(".pane-feature-header input[type=checkbox]").Change(false);

        Assert.False((bool)_store.Load().Features["demo"]["enabled"]!);
    }

    [Fact]
    public void An_edit_is_reflected_in_the_form_without_reopening_the_tab()
    {
        var page = Render();
        Tab(page, "Demo").Click();

        page.FindAll("input[type=number]").Last().Change("7");

        Assert.Equal("7", page.FindAll("input[type=number]").Last().GetAttribute("value"));
    }

    // ── General ────────────────────────────────────────────────────────────

    [Fact]
    public void A_valid_hotkey_is_persisted()
    {
        Render().Find("input[name=hotkey]").Change("Ctrl+Shift+P");

        Assert.Equal("Ctrl+Shift+P", _store.Load().Hotkey);
    }

    [Fact]
    public void An_invalid_hotkey_is_rejected_rather_than_saved()
    {
        var page = Render();

        page.Find("input[name=hotkey]").Change("Space");

        Assert.Equal(SettingsStore.DefaultHotkey, _store.Load().Hotkey);
        Assert.NotNull(page.Find(".pane-warn"));
    }

    [Fact]
    public void The_hotkey_field_says_it_applies_on_restart()
    {
        Assert.Contains("restart", Render().Find(".settings-detail").TextContent,
            StringComparison.OrdinalIgnoreCase);
    }
}
