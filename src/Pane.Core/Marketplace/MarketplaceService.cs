using System.Security.Cryptography;
using System.Text;
using Pane.Core.Plugins;

namespace Pane.Core.Marketplace;

public enum MarketplaceItemState { Available, Installed, UpdateAvailable }

public record MarketplaceEntry(
    MarketplacePlugin Plugin,
    string MarketplaceName,
    MarketplaceItemState State,
    string? InstalledVersion);

public sealed class MarketplaceService
{
    readonly HttpClient _http;
    readonly MarketplaceConfigStore _config;
    readonly InstalledStore _installed;
    readonly PluginManager _plugins;
    readonly string _cacheDir;

    public MarketplaceService(HttpClient http, MarketplaceConfigStore config,
        InstalledStore installed, PluginManager plugins, string cacheDir)
    {
        _http = http;
        _config = config;
        _installed = installed;
        _plugins = plugins;
        _cacheDir = cacheDir;
    }

    public async Task<IReadOnlyList<MarketplaceEntry>> GetCatalogAsync(CancellationToken ct = default)
    {
        // installed id -> live version from the running plugin manager
        var installedVersions = _plugins.List()
            .GroupBy(e => e.Metadata.Id)
            .ToDictionary(g => g.Key, g => g.First().Metadata.Version);

        var seen = new HashSet<string>();      // dedupe by plugin id, first marketplace wins
        var entries = new List<MarketplaceEntry>();

        foreach (var mref in _config.List())
        {
            var market = await LoadMarketplaceAsync(mref, ct);
            if (market is null) continue;

            foreach (var plugin in market.Plugins)
            {
                if (!seen.Add(plugin.Id)) continue;

                MarketplaceItemState state;
                string? installedVer = installedVersions.GetValueOrDefault(plugin.Id);
                if (installedVer is null)
                    state = MarketplaceItemState.Available;
                else if (PluginVersion.IsUpdateAvailable(installedVer, plugin.Version))
                    state = MarketplaceItemState.UpdateAvailable;
                else
                    state = MarketplaceItemState.Installed;

                entries.Add(new MarketplaceEntry(plugin, market.Name, state, installedVer));
            }
        }
        return entries;
    }

    async Task<Marketplace?> LoadMarketplaceAsync(MarketplaceRef mref, CancellationToken ct)
    {
        var resolved = MarketplaceSource.Resolve(mref.Source);
        var cachePath = CachePathFor(mref.Source);
        try
        {
            string json;
            if (resolved.IsLocal)
                json = await File.ReadAllTextAsync(resolved.Location, ct);
            else
            {
                var resp = await _http.GetAsync(resolved.Location, ct);
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            var market = MarketplaceJson.Parse(json);
            WriteCache(cachePath, json);
            return market;
        }
        catch
        {
            // Offline / malformed: fall back to the last good cached copy if any.
            if (File.Exists(cachePath))
            {
                try { return MarketplaceJson.Parse(await File.ReadAllTextAsync(cachePath, ct)); }
                catch { return null; }
            }
            return null;
        }
    }

    string CachePathFor(string source)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(source)))[..16];
        return Path.Combine(_cacheDir, hash + ".json");
    }

    void WriteCache(string cachePath, string json)
    {
        Directory.CreateDirectory(_cacheDir);
        File.WriteAllText(cachePath, json);
    }

    public async Task InstallAsync(MarketplaceEntry entry, CancellationToken ct = default)
    {
        var src = entry.Plugin.Source;
        if (src.Type == "url" && src.Url is not null)
            await _plugins.InstallFromUrlAsync(src.Url, ct);
        else if (src.Type == "local" && src.Path is not null)
            await _plugins.InstallAsync(src.Path);
        else
            throw new InvalidOperationException($"Unsupported plugin source type '{src.Type}'");

        _installed.Record(entry.Plugin.Id,
            new InstalledInfo(entry.MarketplaceName, src.Url ?? src.Path ?? "", entry.Plugin.Version));
    }

    public async Task UpdateAsync(MarketplaceEntry entry, CancellationToken ct = default)
    {
        var src = entry.Plugin.Source;
        if (src.Type != "url" || src.Url is null)
            throw new InvalidOperationException("Only url-sourced plugins can be updated in place");

        await _plugins.UpdateAsync(entry.Plugin.Id, src.Url, ct);
        _installed.Record(entry.Plugin.Id,
            new InstalledInfo(entry.MarketplaceName, src.Url, entry.Plugin.Version));
    }

    public async Task UninstallAsync(string id, CancellationToken ct = default)
    {
        await _plugins.UninstallAsync(id);
        _installed.Remove(id);
    }
}
