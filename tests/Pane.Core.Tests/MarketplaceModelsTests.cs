using Pane.Core.Marketplace;
using Xunit;

public class MarketplaceModelsTests
{
    const string Sample = """
    {
      "name": "Pane Official",
      "description": "Official plugins",
      "owner": { "name": "pane", "url": "https://github.com/dknathalage/pane" },
      "plugins": [
        {
          "id": "com.pane.apps",
          "name": "Apps",
          "description": "Launch apps",
          "icon": "🚀",
          "version": "1.0.0",
          "category": "system",
          "author": "pane",
          "homepage": "https://example",
          "source": { "type": "url", "url": "https://example/Apps.zip" }
        }
      ]
    }
    """;

    [Fact]
    public void Parses_marketplace_with_a_plugin()
    {
        var m = MarketplaceJson.Parse(Sample);
        Assert.Equal("Pane Official", m.Name);
        Assert.Equal("pane", m.Owner!.Name);
        var p = Assert.Single(m.Plugins);
        Assert.Equal("com.pane.apps", p.Id);
        Assert.Equal("1.0.0", p.Version);
        Assert.Equal("url", p.Source.Type);
        Assert.Equal("https://example/Apps.zip", p.Source.Url);
    }

    [Fact]
    public void Parses_when_optional_fields_absent()
    {
        var m = MarketplaceJson.Parse("""
        { "name": "Min", "plugins": [
          { "id": "x", "name": "X", "version": "1.0", "source": { "type": "url", "url": "u" } } ] }
        """);
        var p = Assert.Single(m.Plugins);
        Assert.Null(p.Description);
        Assert.Null(p.Category);
        Assert.Null(m.Owner);
    }

    [Fact]
    public void Throws_typed_on_malformed_json()
        => Assert.Throws<MarketplaceParseException>(() => MarketplaceJson.Parse("{ not json"));

    [Fact]
    public void Throws_typed_when_plugins_missing()
        => Assert.Throws<MarketplaceParseException>(() => MarketplaceJson.Parse("""{ "name": "x" }"""));
}
