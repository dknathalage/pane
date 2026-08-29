using System.Text.Json;

namespace Pane.Core.Marketplace;

public record InstalledInfo(string Marketplace, string SourceUrl, string Version);

public sealed class InstalledStore
{
    sealed class File_ { public Dictionary<string, InstalledInfo> Plugins { get; set; } = new(); }

    static readonly JsonSerializerOptions Opts =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    readonly string _path;
    public InstalledStore(string path) => _path = path;

    public void Record(string id, InstalledInfo info)
    {
        var file = Load();
        file.Plugins[id] = info;
        Save(file);
    }

    public InstalledInfo? Get(string id) => Load().Plugins.GetValueOrDefault(id);

    public void Remove(string id)
    {
        var file = Load();
        if (file.Plugins.Remove(id)) Save(file);
    }

    public IReadOnlyDictionary<string, InstalledInfo> All() => Load().Plugins;

    File_ Load()
    {
        if (!File.Exists(_path)) return new File_();
        try { return JsonSerializer.Deserialize<File_>(File.ReadAllText(_path), Opts) ?? new File_(); }
        catch { return new File_(); }
    }

    void Save(File_ file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(file, Opts));
    }
}
