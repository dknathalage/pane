using Pane.Abstractions;
using Pane.Core.Marketplace;
using Pane.Plugins.Apps;
using Pane.Plugins.Calculator;
using Pane.Plugins.Files;
using Pane.Plugins.Scripts;
using Pane.Plugins.VSCode;
using Xunit;

// Each bundled plugin declares its version in PluginMetadata; marketplace.json
// advertises the same version in the catalog. If they drift, the app compares the
// running DLL's metadata version against the catalog and offers an update whose
// installed DLL still reports the old version — so "Update" never clears. This
// invariant is what release-please must keep true across both places.
public class MarketplaceVersionConsistencyTests
{
    public static IEnumerable<object[]> BundledPlugins() => new[]
    {
        new object[] { new AppsPlugin().Metadata },
        new object[] { new CalculatorPlugin().Metadata },
        new object[] { new FilesPlugin().Metadata },
        new object[] { new ScriptsPlugin().Metadata },
        new object[] { new VSCodePlugin().Metadata },
    };

    [Theory]
    [MemberData(nameof(BundledPlugins))]
    public void Plugin_metadata_version_matches_marketplace_catalog(PluginMetadata meta)
    {
        var market = MarketplaceJson.Parse(File.ReadAllText(FindMarketplaceJson()));
        var entry = market.Plugins.Single(p => p.Id == meta.Id);
        Assert.Equal(entry.Version, meta.Version);
    }

    static string FindMarketplaceJson()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "marketplace.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(
            "marketplace.json not found walking up from " + AppContext.BaseDirectory);
    }
}
