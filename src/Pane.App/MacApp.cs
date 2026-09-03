using System.Runtime.InteropServices;

namespace Pane.App;

/// <summary>
/// Minimal NSApplication interop: bring Pane to the front (so the search box
/// takes keystrokes) and send it back behind whatever the user was using.
/// Photino has no API for either. Must run on the main thread.
/// </summary>
internal static class MacApp
{
    const string Objc = "/usr/lib/libobjc.A.dylib";

    [DllImport(Objc, EntryPoint = "objc_getClass")] static extern IntPtr GetClass(string name);
    [DllImport(Objc, EntryPoint = "sel_registerName")] static extern IntPtr Sel(string name);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr r, IntPtr s);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendByte(IntPtr r, IntPtr s, byte a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint SendNInt(IntPtr r, IntPtr s);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendIndex(IntPtr r, IntPtr s, nuint a);

    static IntPtr SharedApp() => Send(GetClass("NSApplication"), Sel("sharedApplication"));

    /// <summary>Unhide, activate, and make the launcher window key so it receives typing.</summary>
    public static void Activate()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = SharedApp();
            if (app == IntPtr.Zero) return;

            SendPtr(app, Sel("unhide:"), IntPtr.Zero);
            SendByte(app, Sel("activateIgnoringOtherApps:"), 1);

            var windows = Send(app, Sel("windows"));
            if (windows == IntPtr.Zero) return;
            if (SendNInt(windows, Sel("count")) <= 0) return;

            var window = SendIndex(windows, Sel("objectAtIndex:"), 0);
            if (window != IntPtr.Zero) SendPtr(window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
        }
        catch { /* best-effort: activation is a nicety, never fatal */ }
    }

    /// <summary>Hide the app so key focus returns to whatever was in front before.</summary>
    public static void Deactivate()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = SharedApp();
            if (app != IntPtr.Zero) SendPtr(app, Sel("hide:"), IntPtr.Zero);
        }
        catch { /* best-effort */ }
    }
}
