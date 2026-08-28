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

    public PluginManager(string dataRoot, SettingsStore? store = null)
    {
        _dataRoot = dataRoot;
        _store = store;
        if (_store is not null)
        {
            foreach (var id in _store.Load().DisabledPlugins)
                _disabled.Add(id);
        }
    }

    public IReadOnlyList<PluginEntry> List() =>
        _plugins.Values.Select(p => new PluginEntry(p.Metadata, p.State, p.Error, p.Instance)).ToList();

    public IEnumerable<IPlugin> Active() =>
        _plugins.Values.Where(p => p.State == PluginState.Enabled && p.Instance is not null).Select(p => p.Instance!);

    public async Task LoadAllAsync(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot)) return;
        foreach (var dir in Directory.GetDirectories(pluginsRoot))
        {
            var dlls = Directory.GetFiles(dir, "*.dll");
            var dll = dlls.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).StartsWith("Pane.Plugins"))
                      ?? dlls.FirstOrDefault();
            if (dll is null) continue;
            await LoadOneAsync(dll);
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
                await plugin.DisposeAsync();
                ctx.Unload();
                _plugins[id] = new Loaded { Id = id, DllPath = dllPath, Metadata = meta, State = PluginState.Disabled };
                return;
            }

            var pctx = new PluginContext(id, Path.Combine(_dataRoot, "data", id),
                new Dictionary<string, string>(), _matcher);
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
        PersistDisabled();
    }

    public async Task EnableAsync(string id)
    {
        if (!_plugins.TryGetValue(id, out var p)) return;
        _disabled.Remove(id);
        PersistDisabled();
        await LoadOneAsync(p.DllPath);   // re-instantiate fresh
    }

    void PersistDisabled()
    {
        if (_store is null) return;
        var hotkey = _store.Load().Hotkey;
        _store.Save(new PaneSettings(new HashSet<string>(_disabled), hotkey));
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
