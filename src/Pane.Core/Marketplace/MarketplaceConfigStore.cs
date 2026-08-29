using System.Text.Json;

namespace Pane.Core.Marketplace;

public record MarketplaceRef(string Name, string Source, bool BuiltIn);

public sealed class MarketplaceConfigStore
{
    sealed class File_ { public List<MarketplaceRef> Marketplaces { get; set; } = new(); }

    static readonly JsonSerializerOptions Opts =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    readonly string _path;
    readonly string _defaultName;
    readonly string _defaultSource;

    public MarketplaceConfigStore(string path, string defaultName, string defaultSource)
    {
        _path = path;
        _defaultName = defaultName;
        _defaultSource = defaultSource;
    }

    public IReadOnlyList<MarketplaceRef> List()
    {
        var file = Load();
        if (!file.Marketplaces.Any(m => m.Source == _defaultSource))
            file.Marketplaces.Insert(0, new MarketplaceRef(_defaultName, _defaultSource, BuiltIn: true));
        return file.Marketplaces;
    }

    public void Add(string name, string source)
    {
        var file = Load();
        if (file.Marketplaces.Any(m => m.Source == source)) return;
        file.Marketplaces.Add(new MarketplaceRef(name, source, BuiltIn: false));
        Save(file);
    }

    public void Remove(string source)
    {
        if (source == _defaultSource)
            throw new InvalidOperationException("The built-in marketplace cannot be removed.");
        var file = Load();
        file.Marketplaces.RemoveAll(m => m.Source == source);
        Save(file);
    }

    File_ Load()
    {
        if (!System.IO.File.Exists(_path)) return new File_();
        try { return JsonSerializer.Deserialize<File_>(System.IO.File.ReadAllText(_path), Opts) ?? new File_(); }
        catch { return new File_(); }
    }

    void Save(File_ file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        System.IO.File.WriteAllText(_path, JsonSerializer.Serialize(file, Opts));
    }
}
