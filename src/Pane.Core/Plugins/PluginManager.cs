using Pane.Abstractions;
using Pane.Core.Context;
using Pane.Core.Loading;
using Pane.Core.Settings;

namespace Pane.Core.Plugins;

public sealed class PluginManager
{
    sealed class Loaded
    {
        public required string Id;
        public required string DllPath;
        public required PluginMetadata Metadata;
        public PluginState State;
        public string? Error;
        public IPlugin? Instance;
        public PluginLoadContext? Ctx;
    }

    readonly string _dataRoot;
    readonly IFuzzyMatcher _matcher = new FuzzyMatcher();
    readonly Dictionary<string, Loaded> _plugins = new();
    readonly HashSet<string> _disabled = new();     // ids the user disabled
    readonly SettingsStore? _store;
    readonly Dictionary<string, Dictionary<string, string>> _pluginSettings = new();
    readonly PluginFetcher? _fetcher;

    public PluginManager(string dataRoot, SettingsStore? store = null, PluginFetcher? fetcher = null)
    {
        _dataRoot = dataRoot;
        _store = store;
        _fetcher = fetcher;
        if (_store is not null)
        {
            var s = _store.Load();
            foreach (var id in s.DisabledPlugins) _disabled.Add(id);
            foreach (var kv in s.PluginSettings) _pluginSettings[kv.Key] = new Dictionary<string, string>(kv.Value);
        }
    }

    public IReadOnlyList<PluginEntry> List() =>
        _plugins.Values.Select(p => new PluginEntry(p.Metadata, p.State, p.Error, p.Instance)).ToList();

    public IEnumerable<IPlugin> Active() =>
        _plugins.Values.Where(p => p.State == PluginState.Enabled && p.Instance is not null).Select(p => p.Instance!);

    public IReadOnlyDictionary<string, string> GetPluginSettings(string id)
    {
        var meta = _plugins.TryGetValue(id, out var p) ? p.Metadata : null;
        return PluginSettingsMerge.Merge(meta?.Settings, _pluginSettings.GetValueOrDefault(id));
    }

    public static string? FindPluginDll(string dir)
    {
        var dlls = Directory.GetFiles(dir, "*.dll");
        return dlls.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).StartsWith("Pane.Plugins"))
               ?? dlls.FirstOrDefault();
    }

    public async Task LoadAllAsync(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot)) return;
        foreach (var dir in Directory.GetDirectories(pluginsRoot))
        {
            var dll = FindPluginDll(dir);
            if (dll is null) continue;
            await LoadOneAsync(dll);
        }
    }

    public async Task<PluginEntry> InstallFromUrlAsync(string url, CancellationToken ct = default)
    {
        if (_fetcher is null)
            throw new InvalidOperationException("PluginManager has no fetcher configured");

        var temp = await _fetcher.DownloadAndExtractAsync(url, ct);
        try
        {
            var dll = FindPluginDll(temp)
                ?? throw new PluginFetchException("No plugin dll found in the downloaded archive");

            // Read the plugin id from a throwaway load, then unload before copying.
            var (probe, pctx) = PluginLoader.CreateContext(dll);
            var id = probe.Metadata.Id;
            await probe.DisposeAsync();
            pctx.Unload();

            var pluginsRoot = Path.Combine(_dataRoot, "plugins");
            var dst = Path.Combine(pluginsRoot, id);
            Directory.CreateDirectory(dst);
            CopyDir(temp, dst);

            var installedDll = Path.Combine(dst, Path.GetFileName(dll));
            await LoadOneAsync(installedDll);
            return List().First(e => e.Metadata.Id == id);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    async Task LoadOneAsync(string dllPath)
    {
        // Read metadata even for disabled plugins by instantiating; on failure record Errored.
        PluginMetadata? meta = null;
        try
        {
            var (plugin, ctx) = PluginLoader.CreateContext(dllPath);
            meta = plugin.Metadata;
            var id = meta.Id;

            if (_disabled.Contains(id))
            {
                // Reject duplicate id from a different folder; keep the first-loaded entry.
                if (_plugins.TryGetValue(id, out var existingDisabled) && existingDisabled.DllPath != dllPath)
                {
                    await plugin.DisposeAsync();
                    ctx.Unload();
                    return;
                }
                await plugin.DisposeAsync();
                ctx.Unload();
                _plugins[id] = new Loaded { Id = id, DllPath = dllPath, Metadata = meta, State = PluginState.Disabled };
                return;
            }

            // Reject duplicate id from a different folder; keep the first-loaded entry.
            if (_plugins.TryGetValue(id, out var existing) && existing.DllPath != dllPath)
            {
                await plugin.DisposeAsync();
                ctx.Unload();
                return;
            }

            var settings = PluginSettingsMerge.Merge(
                meta.Settings, _pluginSettings.GetValueOrDefault(id));
            var pctx = new PluginContext(id, Path.Combine(_dataRoot, "data", id), settings, _matcher);
            await plugin.InitializeAsync(pctx);
            _plugins[id] = new Loaded
            {
                Id = id, DllPath = dllPath, Metadata = meta,
                State = PluginState.Enabled, Instance = plugin, Ctx = ctx
            };
        }
        catch (Exception ex)
        {
            var id = meta?.Id ?? Path.GetFileNameWithoutExtension(dllPath);
            _plugins[id] = new Loaded
            {
                Id = id, DllPath = dllPath,
                Metadata = meta ?? new PluginMetadata(id, id, "⚠️", "?", "failed to load", Array.Empty<string>()),
                State = PluginState.Errored, Error = ex.Message
            };
        }
    }

    public async Task DisableAsync(string id)
    {
        if (!_plugins.TryGetValue(id, out var p)) return;
        _disabled.Add(id);
        if (p.Instance is not null) { await p.Instance.DisposeAsync(); p.Instance = null; }
        p.Ctx?.Unload(); p.Ctx = null;
        p.State = PluginState.Disabled; p.Error = null;
        Persist();
    }

    public async Task EnableAsync(string id)
    {
        if (!_plugins.TryGetValue(id, out var p)) return;
        _disabled.Remove(id);
        Persist();
        await LoadOneAsync(p.DllPath);   // re-instantiate fresh
    }

    void Persist()
    {
        if (_store is null) return;
        var current = _store.Load();
        _store.Save(current with
        {
            DisabledPlugins = new HashSet<string>(_disabled),
            PluginSettings = _pluginSettings.ToDictionary(
                e => e.Key, e => new Dictionary<string, string>(e.Value))
        });
    }

    public async Task UpdatePluginSettingsAsync(string id, IReadOnlyDictionary<string, string> values)
    {
        _pluginSettings[id] = new Dictionary<string, string>(values);
        Persist();

        // Reload an enabled plugin so InitializeAsync sees the new settings.
        if (_plugins.TryGetValue(id, out var p) && p.State == PluginState.Enabled)
        {
            if (p.Instance is not null) { await p.Instance.DisposeAsync(); p.Instance = null; }
            p.Ctx?.Unload(); p.Ctx = null;
            await LoadOneAsync(p.DllPath);
        }
    }

    public async Task<PluginEntry> InstallAsync(string sourcePath)
    {
        // sourcePath is a folder or a single dll; copy into pluginsRoot/<name>/
        var pluginsRoot = Path.Combine(_dataRoot, "plugins");
        Directory.CreateDirectory(pluginsRoot);
        string dll;
        if (Directory.Exists(sourcePath))
        {
            var name = new DirectoryInfo(sourcePath).Name;
            var dst = Path.Combine(pluginsRoot, name);
            CopyDir(sourcePath, dst);
            dll = Directory.GetFiles(dst, "*.dll").First();
        }
        else
        {
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var dst = Path.Combine(pluginsRoot, name);
            Directory.CreateDirectory(dst);
            dll = Path.Combine(dst, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, dll, true);
        }
        await LoadOneAsync(dll);
        var id = _plugins.Values.First(p => p.DllPath == dll).Id;
        return List().First(e => e.Metadata.Id == id);
    }

    public async Task UninstallAsync(string id)
    {
        if (!_plugins.TryGetValue(id, out var p)) return;
        if (p.Instance is not null) { await p.Instance.DisposeAsync(); p.Instance = null; }
        p.Ctx?.Unload(); p.Ctx = null;
        _plugins.Remove(id);
        if (_disabled.Remove(id)) Persist();
        var dir = Path.GetDirectoryName(p.DllPath)!;
        TryDelete(dir);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
    }

    static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* file locks after unload GC; ignore */ }
    }
}
