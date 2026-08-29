using System.Text.Json;

namespace Pane.Core.Marketplace;

public record MarketplaceOwner(string? Name, string? Url);

public record PluginSource(string Type, string? Url, string? Path);

public record MarketplacePlugin(
    string Id,
    string Name,
    string? Description,
    string? Icon,
    string Version,
    string? Category,
    string? Author,
    string? Homepage,
    PluginSource Source);

public record Marketplace(
    string Name,
    string? Description,
    MarketplaceOwner? Owner,
    IReadOnlyList<MarketplacePlugin> Plugins);

public sealed class MarketplaceParseException : Exception
{
    public MarketplaceParseException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class MarketplaceJson
{
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static Marketplace Parse(string json)
    {
        Marketplace? m;
        try { m = JsonSerializer.Deserialize<Marketplace>(json, Opts); }
        catch (JsonException ex) { throw new MarketplaceParseException("Invalid marketplace JSON", ex); }

        if (m is null || m.Name is null || m.Plugins is null)
            throw new MarketplaceParseException("Marketplace JSON missing required 'name' or 'plugins'");
        return m;
    }
}
