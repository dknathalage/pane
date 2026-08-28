using Pane.Abstractions;

namespace Pane.Core.Plugins;

public static class PluginSettingsMerge
{
    public static Dictionary<string, string> Merge(
        IReadOnlyList<PluginSettingSpec>? schema,
        IReadOnlyDictionary<string, string>? stored)
    {
        var result = new Dictionary<string, string>();
        if (schema is not null)
            foreach (var spec in schema)
                if (spec.Default is not null)
                    result[spec.Key] = spec.Default;
        if (stored is not null)
            foreach (var kv in stored)
                result[kv.Key] = kv.Value;
        return result;
    }
}
