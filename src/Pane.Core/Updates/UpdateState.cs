using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pane.Core.Updates;

/// <summary>
/// When we last asked GitHub. Deliberately separate from settings.json: this is
/// machine state, not a user preference, and writing it on every check would
/// churn a file the user edits by hand.
/// </summary>
public sealed class UpdateState
{
    const string LastCheckKey = "lastCheckUtc";
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    readonly string _path;
    public UpdateState(string path) => _path = path;

    public DateTimeOffset? LoadLastCheck()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject root) return null;
            var raw = root[LastCheckKey]?.GetValue<string>();
            return DateTimeOffset.TryParse(raw, out var when) ? when : null;
        }
        catch { return null; }
    }

    public void SaveLastCheck(DateTimeOffset when)
    {
        try
        {
            var root = new JsonObject { [LastCheckKey] = when.ToString("O") };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, root.ToJsonString(Opts));
        }
        catch { /* a state file we cannot write must not break update checking */ }
    }
}
