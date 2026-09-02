using System.Text.Json;

namespace Pane.Core.Settings;

public record PaneSettings(HashSet<string> DisabledPlugins, string Hotkey);

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    readonly string _path;
    public SettingsStore(string path) => _path = path;

    public PaneSettings Load()
    {
        if (!File.Exists(_path)) return new PaneSettings(new(), DefaultHotkey);
        try
        {
            var s = JsonSerializer.Deserialize<PaneSettings>(File.ReadAllText(_path), Opts);
            if (s is null) return new PaneSettings(new(), DefaultHotkey);
            return s with
            {
                DisabledPlugins = s.DisabledPlugins ?? new(),
                Hotkey = string.IsNullOrEmpty(s.Hotkey) ? DefaultHotkey : s.Hotkey,
            };
        }
        catch { return new PaneSettings(new(), DefaultHotkey); }
    }

    public void Save(PaneSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(s, Opts));
    }

    public const string DefaultHotkey = "Alt+Space";
}
