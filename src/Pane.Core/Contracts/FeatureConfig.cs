using System.Text.Json.Nodes;

namespace Pane.Core.Contracts;

/// <summary>
/// A feature's settings, resolved once against its declared schema: stored values
/// win, anything missing or malformed falls back to the declared default. Features
/// read their config through this rather than reaching for the settings file, so a
/// feature is exercisable in a test by handing it a config.
/// </summary>
public sealed class FeatureConfig
{
    public const string EnabledKey = "enabled";
    public const string KeywordKey = "keyword";
    public const string PriorityKey = "priority";

    readonly IReadOnlyDictionary<string, SettingDefinition> _schema;
    readonly JsonObject _values;

    FeatureConfig(
        IReadOnlyDictionary<string, SettingDefinition> schema, JsonObject values,
        bool enabled, string? keyword, int priority)
    {
        _schema = schema;
        _values = values;
        Enabled = enabled;
        Keyword = keyword;
        Priority = priority;
    }

    /// <summary>The user's on/off choice. Availability is a separate question — see IPaneFeature.</summary>
    public bool Enabled { get; }

    /// <summary>Optional prefix that scopes a query to this feature; null when cleared.</summary>
    public string? Keyword { get; }

    /// <summary>Ranking bias applied to every result this feature returns.</summary>
    public int Priority { get; }

    /// <summary>The three knobs every feature gets for free, so the UI renders them uniformly.</summary>
    public static IReadOnlyList<SettingDefinition> UniversalSettings(FeatureDescriptor desc) => new SettingDefinition[]
    {
        new BoolSetting(EnabledKey, "Enabled", Default: true),
        new TextSetting(KeywordKey, "Keyword", Default: desc.Keyword ?? "",
            "Optional prefix that shows only this feature's results. Leave blank for none."),
        new IntSetting(PriorityKey, "Priority", desc.Priority, -100, 100,
            "Bias applied to this feature's results. Higher sorts earlier."),
    };

    public static FeatureConfig Resolve(
        FeatureDescriptor desc, IReadOnlyList<SettingDefinition> schema, JsonObject values)
    {
        // The universal settings live in the same lookup as the declared ones: the
        // settings UI renders both through the typed accessors, so both must
        // resolve there. Only their *defaults* come from the descriptor.
        var byKey = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        foreach (var s in UniversalSettings(desc))
            byKey[s.Key] = s;

        foreach (var s in schema)
        {
            if (s.Key is EnabledKey or KeywordKey or PriorityKey)
                throw new ArgumentException(
                    $"Feature '{desc.Id}' redeclares reserved setting '{s.Key}'.", nameof(schema));
            byKey[s.Key] = s;
        }

        var keyword = values[KeywordKey] is JsonValue k && k.TryGetValue<string>(out var kw)
            ? kw.Trim()
            : desc.Keyword;

        return new FeatureConfig(
            byKey, values,
            enabled: Bool(values[EnabledKey]) ?? true,
            keyword: string.IsNullOrEmpty(keyword) ? null : keyword,
            priority: Int(values[PriorityKey]) ?? desc.Priority);
    }

    /// <summary>Every value at its declared default — the shape a feature starts life with.</summary>
    public static FeatureConfig Defaults(FeatureDescriptor desc, IReadOnlyList<SettingDefinition> schema)
        => Resolve(desc, schema, new JsonObject());

    public bool GetBool(string key) => Bool(_values[key]) ?? Definition<BoolSetting>(key).Default;

    public int GetInt(string key)
    {
        var def = Definition<IntSetting>(key);
        return Math.Clamp(Int(_values[key]) ?? def.Default, def.Min, def.Max);
    }

    public string GetText(string key)
    {
        var def = Definition<TextSetting>(key);
        if (_values[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            return s.Trim();
        return def.Default;
    }

    public IReadOnlyList<string> GetPaths(string key)
    {
        var def = Definition<PathsSetting>(key);
        if (_values[key] is not JsonArray arr) return def.Default;

        var paths = arr
            .OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var s) ? s.Trim() : "")
            .Where(s => s.Length > 0)
            .ToArray();

        return paths.Length > 0 ? paths : def.Default;
    }

    T Definition<T>(string key) where T : SettingDefinition
    {
        if (!_schema.TryGetValue(key, out var def))
            throw new KeyNotFoundException($"No setting '{key}' is declared by this feature.");
        if (def is not T typed)
            throw new InvalidOperationException(
                $"Setting '{key}' is a {def.GetType().Name}, not a {typeof(T).Name}.");
        return typed;
    }

    static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
