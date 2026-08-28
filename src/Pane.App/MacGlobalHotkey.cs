using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pane.App;

/// <summary>
/// Global hotkey via Carbon's RegisterEventHotKey. Unlike a keyboard event-tap
/// (SharpHook/libuiohook), this needs NO Accessibility permission. Must be
/// registered on the main thread; the handler fires on the main run loop.
/// </summary>
internal static class MacGlobalHotkey
{
    const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [StructLayout(LayoutKind.Sequential)] struct EventTypeSpec { public uint eventClass; public uint eventKind; }
    [StructLayout(LayoutKind.Sequential)] struct EventHotKeyID { public uint signature; public uint id; }

    [DllImport(Carbon)] static extern IntPtr GetEventDispatcherTarget();
    [DllImport(Carbon)] static extern int InstallEventHandler(
        IntPtr target, IntPtr handler, nuint numTypes, ref EventTypeSpec typeList, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] static extern int RegisterEventHotKey(
        uint hotKeyCode, uint hotKeyModifiers, EventHotKeyID hotKeyID, IntPtr target, uint options, out IntPtr hotKeyRef);

    static Action? _onPressed;
    static IntPtr _handlerRef, _hotKeyRef;

    public static bool Register(string combo, Action onPressed)
    {
        _onPressed = onPressed;
        var (mods, code) = Parse(combo);
        if (code < 0) return false;

        var target = GetEventDispatcherTarget();
        var type = new EventTypeSpec { eventClass = 0x6B657962 /* 'keyb' */, eventKind = 6 /* kEventHotKeyPressed */ };
        unsafe
        {
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int> handler = &HotKeyHandler;
            if (InstallEventHandler(target, (IntPtr)handler, 1, ref type, IntPtr.Zero, out _handlerRef) != 0)
                return false;
        }
        var id = new EventHotKeyID { signature = 0x50414E45 /* 'PANE' */, id = 1 };
        return RegisterEventHotKey((uint)code, mods, id, target, 0, out _hotKeyRef) == 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static int HotKeyHandler(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        try { _onPressed?.Invoke(); } catch { /* never break the event chain */ }
        return 0; // noErr
    }

    // Carbon modifier masks: cmd=0x0100, shift=0x0200, option=0x0800, control=0x1000.
    static (uint mods, int code) Parse(string combo)
    {
        uint mods = 0; int code = -1;
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": case "option": mods |= 0x0800; break;
                case "ctrl": case "control": mods |= 0x1000; break;
                case "shift": mods |= 0x0200; break;
                case "cmd": case "meta": case "win": case "super": mods |= 0x0100; break;
                default: code = KeyCode(part); break;
            }
        }
        return (mods, code);
    }

    // A subset of macOS virtual key codes (enough for common launcher hotkeys).
    static int KeyCode(string key) => key.ToUpperInvariant() switch
    {
        "SPACE" => 49,
        "A" => 0, "S" => 1, "D" => 2, "F" => 3, "H" => 4, "G" => 5, "Z" => 6, "X" => 7,
        "C" => 8, "V" => 9, "B" => 11, "Q" => 12, "W" => 13, "E" => 14, "R" => 15,
        "Y" => 16, "T" => 17, "O" => 31, "U" => 32, "I" => 34, "P" => 35, "L" => 37,
        "J" => 38, "K" => 40, "N" => 45, "M" => 46,
        _ => -1,
    };
}
