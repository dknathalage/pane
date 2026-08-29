using Pane.Core.Marketplace;
using Xunit;

public class MarketplaceSourceTests
{
    [Theory]
    [InlineData("https://github.com/dknathalage/pane",
                "https://raw.githubusercontent.com/dknathalage/pane/HEAD/marketplace.json")]
    [InlineData("https://github.com/dknathalage/pane.git",
                "https://raw.githubusercontent.com/dknathalage/pane/HEAD/marketplace.json")]
    [InlineData("https://github.com/dknathalage/pane/",
                "https://raw.githubusercontent.com/dknathalage/pane/HEAD/marketplace.json")]
    public void Github_repo_url_resolves_to_raw_marketplace_json(string src, string expected)
    {
        var r = MarketplaceSource.Resolve(src);
        Assert.False(r.IsLocal);
        Assert.Equal(expected, r.Location);
    }

    [Fact]
    public void Direct_json_url_is_used_verbatim()
    {
        var r = MarketplaceSource.Resolve("https://example.com/team/marketplace.json");
        Assert.False(r.IsLocal);
        Assert.Equal("https://example.com/team/marketplace.json", r.Location);
    }

    [Fact]
    public void Existing_local_path_is_local()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mp-{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, "{}");
        try
        {
            var r = MarketplaceSource.Resolve(tmp);
            Assert.True(r.IsLocal);
            Assert.Equal(tmp, r.Location);
        }
        finally { File.Delete(tmp); }
    }
}
