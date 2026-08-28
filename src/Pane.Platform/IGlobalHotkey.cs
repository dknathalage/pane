namespace Pane.Platform;

public interface IGlobalHotkey : IDisposable
{
    event Action? Pressed;
    void Register(string combo);
}

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
}
