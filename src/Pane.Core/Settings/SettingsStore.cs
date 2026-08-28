using System.Text.Json;

namespace Pane.Core.Settings;

public record PaneSettings(
    HashSet<string> DisabledPlugins,
    string Hotkey,
    Dictionary<string, Dictionary<string, string>> PluginSettings);

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    readonly string _path;
    public SettingsStore(string path) => _path = path;

    public PaneSettings Load()
    {
        if (!File.Exists(_path)) return new PaneSettings(new(), DefaultHotkey, new());
        try
        {
            var s = JsonSerializer.Deserialize<PaneSettings>(File.ReadAllText(_path));
            if (s is null) return new PaneSettings(new(), DefaultHotkey, new());
            return s with
            {
                DisabledPlugins = s.DisabledPlugins ?? new(),
                Hotkey = string.IsNullOrEmpty(s.Hotkey) ? DefaultHotkey : s.Hotkey,
                PluginSettings = s.PluginSettings ?? new()
            };
        }
        catch { return new PaneSettings(new(), DefaultHotkey, new()); }
    }

    public void Save(PaneSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(s, Opts));
    }

    public const string DefaultHotkey = "Alt+Space";
}
