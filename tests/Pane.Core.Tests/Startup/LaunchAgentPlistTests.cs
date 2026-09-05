using Pane.Core.Startup;
using Xunit;

public class LaunchAgentPlistTests
{
    const string Exec = "/Users/someone/Applications/Pane.app/Contents/MacOS/Pane.App";

    [Fact]
    public void The_label_matches_the_one_install_sh_registers()
    {
        // install.sh, uninstall-startup.sh and this class must agree, or the
        // toggle would manage a different agent than the installer wrote.
        Assert.Equal("com.pane.launcher", LaunchAgentPlist.Label);
        Assert.Contains("com.pane.launcher", LaunchAgentPlist.Build(Exec));
    }

    [Fact]
    public void The_plist_launches_the_bundle_executable()
    {
        Assert.Contains(Exec, LaunchAgentPlist.Build(Exec));
    }

    [Fact]
    public void The_plist_passes_startup_so_Pane_comes_up_hidden()
    {
        Assert.Contains("--startup", LaunchAgentPlist.Build(Exec));
    }

    [Fact]
    public void The_plist_runs_at_load_but_is_not_kept_alive()
    {
        var plist = LaunchAgentPlist.Build(Exec);

        Assert.Contains("<key>RunAtLoad</key><true/>", plist.Replace("\n", "").Replace("  ", ""));
        Assert.Contains("KeepAlive", plist);
    }

    [Fact]
    public void The_plist_abandons_its_process_group_so_launchd_does_not_kill_the_update_helper()
    {
        // man 5 launchd.plist: without this, launchd kills every process in
        // the job's process group when the job dies — including the updater's
        // detached swap helper, spawned as an ordinary child of Pane.
        var plist = LaunchAgentPlist.Build(Exec);

        Assert.Contains("<key>AbandonProcessGroup</key><true/>",
            plist.Replace("\n", "").Replace("  ", ""));
    }

    [Fact]
    public void The_plist_is_well_formed_xml()
    {
        var doc = System.Xml.Linq.XDocument.Parse(LaunchAgentPlist.Build(Exec));

        Assert.Equal("plist", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void The_program_path_can_be_read_back_out()
    {
        var plist = LaunchAgentPlist.Build(Exec);

        Assert.Equal(Exec, LaunchAgentPlist.ReadProgramPath(plist));
    }

    [Fact]
    public void Reading_a_malformed_plist_yields_no_path_rather_than_throwing()
    {
        Assert.Null(LaunchAgentPlist.ReadProgramPath("<not-xml"));
    }

    [Fact]
    public void The_default_path_is_the_user_LaunchAgents_folder()
    {
        var path = LaunchAgentPlist.DefaultPath();

        Assert.EndsWith("Library/LaunchAgents/com.pane.launcher.plist", path);
    }
}
