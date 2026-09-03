using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pane.Core.Settings;

/// <summary>
/// The whole persisted settings document. <see cref="Features"/> maps a feature id
/// to its raw settings object; the schema each feature declares turns that into a
/// typed <see cref="FeatureConfig"/>, so the store itself stays schema-agnostic.
/// </summary>
public sealed record PaneSettings(string Hotkey, Dictionary<string, JsonObject> Features)
{
    public JsonObject FeatureValues(string id) =>
        Features.TryGetValue(id, out var o) ? o : new JsonObject();

    /// <summary>Returns a copy with <paramref name="values"/> stored for <paramref name="id"/>.</summary>
    public PaneSettings WithFeature(string id, JsonObject values)
    {
        var next = new Dictionary<string, JsonObject>(Features) { [id] = values };
        return this with { Features = next };
    }
}

public sealed class SettingsStore
{
    public const string DefaultHotkey = "Alt+Space";
    const string HotkeyKey = "hotkey";
    const string FeaturesKey = "features";
    const string LegacyDisabledKey = "disabledPlugins";
    const string EnabledKey = "enabled";

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    readonly string _path;
    public SettingsStore(string path) => _path = path;

    public PaneSettings Load()
    {
        if (!File.Exists(_path)) return Defaults();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject root) return Defaults();

            var hotkey = root[HotkeyKey]?.GetValue<string>();
            var features = ReadFeatures(root);
            MigrateDisabledPlugins(root, features);

            return new PaneSettings(string.IsNullOrEmpty(hotkey) ? DefaultHotkey : hotkey, features);
        }
        catch { return Defaults(); }
    }

    public void Save(PaneSettings s)
    {
        var features = new JsonObject();
        foreach (var (id, values) in s.Features)
            features[id] = values.DeepClone();

        var root = new JsonObject
        {
            [HotkeyKey] = s.Hotkey,
            [FeaturesKey] = features,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, root.ToJsonString(Opts));
    }

    static PaneSettings Defaults() => new(DefaultHotkey, new());

    static Dictionary<string, JsonObject> ReadFeatures(JsonObject root)
    {
        var features = new Dictionary<string, JsonObject>();
        if (root[FeaturesKey] is not JsonObject stored) return features;
        foreach (var (id, node) in stored)
            if (node is JsonObject values)
                features[id] = (JsonObject)values.DeepClone();
        return features;
    }

    // Pre-schema settings files listed disabled features in a flat "disabledPlugins"
    // array. Fold each one into its feature's "enabled" flag so upgrades keep the
    // user's choices; the key is dropped on the next Save.
    static void MigrateDisabledPlugins(JsonObject root, Dictionary<string, JsonObject> features)
    {
        if (root[LegacyDisabledKey] is not JsonArray disabled) return;
        foreach (var node in disabled)
        {
            if (node?.GetValue<string>() is not { Length: > 0 } id) continue;
            if (!features.TryGetValue(id, out var values)) features[id] = values = new JsonObject();
            values[EnabledKey] ??= false;
        }
    }
}
