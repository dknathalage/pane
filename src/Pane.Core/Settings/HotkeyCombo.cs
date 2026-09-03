namespace Pane.Core.Settings;

/// <summary>Parses and validates the global hotkey stored in <see cref="PaneSettings"/>.</summary>
public static class HotkeyCombo
{
    public static (bool alt, bool ctrl, bool shift, bool meta, string key) Parse(string combo)
    {
        bool alt = false, ctrl = false, shift = false, meta = false;
        string key = "";
        foreach (var partRaw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (partRaw.ToLowerInvariant())
            {
                case "alt" or "option": alt = true; break;
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "cmd" or "meta" or "win" or "super": meta = true; break;
                default: key = partRaw; break;
            }
        }
        return (alt, ctrl, shift, meta, key);
    }

    /// <summary>
    /// True when the combo has at least one modifier and a key. Parse is lenient —
    /// it yields an empty key for junk like "Alt+" — so anything that can register
    /// a hotkey must check this first rather than saving a combo that never fires.
    /// </summary>
    public static bool IsValid(string? combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return false;
        var (alt, ctrl, shift, meta, key) = Parse(combo);
        return (alt || ctrl || shift || meta) && key.Length > 0;
    }
}
