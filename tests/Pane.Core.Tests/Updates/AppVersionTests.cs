using Pane.Core.Updates;
using Xunit;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void TryParse_reads_a_bare_triple(string text, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new AppVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.0.4")]
    [InlineData("v1.2.0")]      // TryParse is strict; tags go through TryParseTag
    [InlineData("banana")]
    public void TryParse_rejects_anything_else(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("pane-v1.2.0")]
    [InlineData("plugins-v1.2.0")]
    [InlineData("v1.2.0")]
    [InlineData("1.2.0")]
    public void TryParseTag_tolerates_every_prefix_this_repo_uses(string tag)
    {
        Assert.True(AppVersion.TryParseTag(tag, out var v));
        Assert.Equal(new AppVersion(1, 2, 0), v);
    }

    [Theory]
    [InlineData("pane-v1.2.0-rc1")]   // pre-release is out of scope, not mis-ordered
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseTag_rejects_tags_it_cannot_order(string? tag)
    {
        Assert.False(AppVersion.TryParseTag(tag, out _));
    }

    [Fact]
    public void Components_are_compared_numerically_not_as_text()
    {
        Assert.True(new AppVersion(1, 10, 0) > new AppVersion(1, 9, 0));
        Assert.True(new AppVersion(2, 0, 0) > new AppVersion(1, 99, 99));
        Assert.True(new AppVersion(1, 2, 3) > new AppVersion(1, 2, 2));
    }

    [Fact]
    public void Equal_versions_are_neither_greater_nor_less()
    {
        var a = new AppVersion(1, 2, 3);
        var b = new AppVersion(1, 2, 3);
        Assert.False(a > b);
        Assert.False(a < b);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToString_round_trips_through_TryParse()
    {
        Assert.True(AppVersion.TryParse(new AppVersion(3, 4, 5).ToString(), out var v));
        Assert.Equal(new AppVersion(3, 4, 5), v);
    }

    [Fact]
    public void Zero_is_lower_than_any_real_release()
    {
        Assert.True(new AppVersion(0, 0, 1) > AppVersion.Zero);
    }
}
