using System.Text.RegularExpressions;

namespace Pane.Core.Features.Scripts;

public static class ScriptHeader
{
    static readonly Regex NameRx = new(@"^#\s*name:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    static readonly Regex IconRx = new(@"^#\s*icon:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static (string name, string icon) Parse(string content)
    {
        var name = NameRx.Match(content) is { Success: true } n ? n.Groups[1].Value.Trim() : "";
        var icon = IconRx.Match(content) is { Success: true } i ? i.Groups[1].Value.Trim() : "📜";
        return (name, icon);
    }
}
