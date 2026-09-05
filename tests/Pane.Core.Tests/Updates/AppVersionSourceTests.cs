using Pane.Core.Updates;
using Xunit;

public class AppVersionSourceTests
{
    [Fact]
    public void A_plain_version_is_read_as_is()
    {
        Assert.Equal(new AppVersion(1, 2, 0),
            AppVersionSource.FromInformationalVersion("1.2.0"));
    }

    [Fact]
    public void A_SourceLink_commit_suffix_is_trimmed()
    {
        // SourceLink appends "+<sha>" to AssemblyInformationalVersion.
        Assert.Equal(new AppVersion(1, 3, 4),
            AppVersionSource.FromInformationalVersion("1.3.4+a1b2c3d4e5f6"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.0.0.0")]
    public void An_absent_or_unreadable_version_reports_zero(string? raw)
    {
        // Zero is honest: it makes every real release look newer, rather than
        // silently hiding updates behind a fake version.
        Assert.Equal(AppVersion.Zero, AppVersionSource.FromInformationalVersion(raw));
    }

    [Fact]
    public void Current_never_throws_even_with_no_entry_assembly_attribute()
    {
        // The entry assembly under `dotnet test` is the test host itself,
        // which does carry its own AssemblyInformationalVersionAttribute (its
        // own SDK version, e.g. "17.14.1") — so this can't assert Zero, and
        // "v >= AppVersion.Zero" is true for every AppVersion and asserts
        // nothing. What this test actually pins is narrower: that reading
        // Current when there is no Pane-specific version stamped does not
        // throw. A failing call is the only way this test can fail.
        _ = AppVersionSource.Current;
    }
}
