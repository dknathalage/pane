using Pane.Core.Startup;
using Xunit;

public class MacLoginItemTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"pane-login-{Guid.NewGuid():N}");
    readonly string _plist;
    const string Bundle = "/Users/someone/Applications/Pane.app";
    const string Exec = Bundle + "/Contents/MacOS/Pane.App";

    public MacLoginItemTests()
    {
        Directory.CreateDirectory(_dir);
        _plist = Path.Combine(_dir, "com.pane.launcher.plist");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    MacLoginItem Item(string? bundle = Bundle) => new(bundle, _plist);

    [Fact]
    public void Without_a_bundle_the_login_item_cannot_be_managed()
    {
        var item = Item(bundle: null);

        Assert.False(item.CanManage);
        Assert.NotNull(item.UnavailableReason);
    }

    [Fact]
    public void With_a_bundle_the_login_item_can_be_managed_on_macOS()
    {
        // CanManage also requires macOS, so assert only the bundle half here.
        Assert.Equal(OperatingSystem.IsMacOS(), Item().CanManage);
    }

    [Fact]
    public void No_plist_means_it_does_not_start_at_login()
    {
        Assert.False(Item().IsEnabled);
    }

    [Fact]
    public void A_plist_pointing_at_this_bundle_means_it_starts_at_login()
    {
        File.WriteAllText(_plist, LaunchAgentPlist.Build(Exec));

        Assert.True(Item().IsEnabled);
    }

    [Fact]
    public void A_plist_pointing_somewhere_else_does_not_count_as_enabled()
    {
        // A stale agent from an older install would otherwise report "on" while
        // silently launching a different copy of Pane at login.
        File.WriteAllText(_plist,
            LaunchAgentPlist.Build("/Applications/Old/Pane.app/Contents/MacOS/Pane.App"));

        Assert.False(Item().IsEnabled);
    }

    [Fact]
    public void A_corrupt_plist_does_not_count_as_enabled()
    {
        File.WriteAllText(_plist, "<not-xml");

        Assert.False(Item().IsEnabled);
    }

    [Fact]
    public void The_expected_executable_sits_inside_the_bundle()
    {
        Assert.Equal(Exec, Item().ExpectedExecutable);
    }
}
