using System.Text.Json.Nodes;
using Pane.Core.Contracts;
using Pane.Core.Features.Calculator;
using Xunit;

public class CalculatorFeatureTests
{
    static CalculatorFeature Configured(JsonObject? values = null)
    {
        var f = new CalculatorFeature();
        f.ApplyConfig(FeatureConfig.Resolve(f.Descriptor, f.Settings, values ?? new JsonObject()));
        return f;
    }

    static async Task<string?> TitleAsync(CalculatorFeature f, string terms)
    {
        await foreach (var r in f.QueryAsync(new PaneQuery(terms, null, terms), CancellationToken.None))
            return r.Title;
        return null;
    }

    [Fact]
    public async Task Evaluates_a_plain_expression_without_the_keyword()
    {
        Assert.Equal("= 4", await TitleAsync(Configured(), "2+2"));
    }

    [Fact]
    public async Task Shows_six_decimal_places_by_default()
    {
        Assert.Equal("= 0.333333", await TitleAsync(Configured(), "1/3"));
    }

    [Fact]
    public async Task Decimal_places_are_configurable()
    {
        var f = Configured(new JsonObject { ["decimals"] = 2 });
        Assert.Equal("= 0.33", await TitleAsync(f, "1/3"));
    }

    [Fact]
    public void Is_always_available()
    {
        Assert.True(Configured().CheckAvailability().IsAvailable);
    }
}
