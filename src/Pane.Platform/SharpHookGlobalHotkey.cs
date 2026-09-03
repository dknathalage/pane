using SharpHook;
using SharpHook.Data;

using Pane.Core.Settings;

namespace Pane.Platform;

/// <summary>
/// Global hotkey implementation using SharpHook (libuiohook).
/// Requires a GUI session and macOS Accessibility permission at runtime.
/// </summary>
public sealed class SharpHookGlobalHotkey : IGlobalHotkey
{
    // SimpleGlobalHook(null) uses the default UioHookProvider.
    // RunAsync requires (GlobalHookType, bool); we use Keyboard-only and
    // runAsCurrentThread=false so the hook runs on a background thread.
    private readonly SimpleGlobalHook _hook = new(null);
    private (bool alt, bool ctrl, bool shift, bool meta, string key) _combo;

    public event Action? Pressed;

    public SharpHookGlobalHotkey()
    {
        _hook.KeyPressed += OnKeyPressed;
        _ = _hook.RunAsync(GlobalHookType.Keyboard, false);
    }

    public void Register(string combo) => _combo = HotkeyCombo.Parse(combo);

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        // EventMask (on the raw event) carries modifier state.
        // EventMaskExtensions provides HasAlt/HasCtrl/HasShift/HasMeta helpers.
        var mask = e.RawEvent.Mask;
        bool alt   = mask.HasAlt();
        bool ctrl  = mask.HasCtrl();
        bool shift = mask.HasShift();
        bool meta  = mask.HasMeta();

        // KeyCode enum values are prefixed "Vc" (e.g. VcSpace, VcP).
        // Strip the prefix and compare case-insensitively against the parsed key name.
        var keyCodeName = e.Data.KeyCode.ToString();
        var key = keyCodeName.StartsWith("Vc", StringComparison.Ordinal)
            ? keyCodeName[2..]
            : keyCodeName;

        if (alt   == _combo.alt   &&
            ctrl  == _combo.ctrl  &&
            shift == _combo.shift &&
            meta  == _combo.meta  &&
            key.Equals(_combo.key, StringComparison.OrdinalIgnoreCase))
        {
            Pressed?.Invoke();
        }
    }

    public void Dispose()
    {
        _hook.KeyPressed -= OnKeyPressed;
        _hook.Dispose();
    }
}
