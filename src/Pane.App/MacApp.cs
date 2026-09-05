using System.Runtime.InteropServices;

namespace Pane.App;

/// <summary>
/// Minimal NSApplication/NSWindow interop: bring Pane to the front (so the search
/// box takes keystrokes), send it back behind whatever the user was using, and
/// keep it shaped like a launcher rather than a regular windowed app. Photino has
/// no API for any of it. Must run on the main thread.
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
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendWithNInt(IntPtr r, IntPtr s, nint a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendWithNUInt(IntPtr r, IntPtr s, nuint a);

    // NSApplicationActivationPolicyAccessory: runs without a Dock tile or a menu
    // bar, but may still show windows and take key focus.
    const nint ActivationPolicyAccessory = 1;

    // NSWindowCollectionBehaviorCanJoinAllSpaces (1 << 0) — show on whichever
    // Space is current instead of switching the user to ours.
    // NSWindowCollectionBehaviorFullScreenAuxiliary (1 << 8) — allowed to sit over
    // another app that is full-screen, rather than being hidden behind it.
    const nuint CollectionBehaviorLauncher = (1 << 0) | (1 << 8);

    // NSStatusWindowLevel: above ordinary and floating windows, so the launcher is
    // not covered by whatever the user was working in.
    const nint StatusWindowLevel = 25;

    static IntPtr SharedApp() => Send(GetClass("NSApplication"), Sel("sharedApplication"));

    /// <summary>
    /// Drop the app out of the Dock and the Command-Tab switcher, and let the
    /// launcher window follow the user across Spaces and over full-screen apps.
    ///
    /// The bundle's LSUIElement only sets the *initial* policy: Photino calls
    /// setActivationPolicy: when it creates the window, which overrides it. So this
    /// must run after the message loop is up, and is re-asserted on every show.
    /// </summary>
    public static void ConfigureAsLauncher()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = SharedApp();
            if (app == IntPtr.Zero) return;

            SendWithNInt(app, Sel("setActivationPolicy:"), ActivationPolicyAccessory);
            ApplyWindowStyle(app);
        }
        catch { /* best-effort: never fatal */ }
    }

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

            // Photino resets the window level whenever it re-applies topmost, so
            // the launcher styling is re-asserted here rather than set once.
            ApplyWindowStyle(app);

            var window = FirstWindow(app);
            if (window != IntPtr.Zero) SendPtr(window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
        }
        catch { /* best-effort: activation is a nicety, never fatal */ }
    }

    static void ApplyWindowStyle(IntPtr app)
    {
        var window = FirstWindow(app);
        if (window == IntPtr.Zero) return;

        SendWithNUInt(window, Sel("setCollectionBehavior:"), CollectionBehaviorLauncher);
        SendWithNInt(window, Sel("setLevel:"), StatusWindowLevel);
    }

    static IntPtr FirstWindow(IntPtr app)
    {
        var windows = Send(app, Sel("windows"));
        if (windows == IntPtr.Zero) return IntPtr.Zero;
        if (SendNInt(windows, Sel("count")) <= 0) return IntPtr.Zero;
        return SendIndex(windows, Sel("objectAtIndex:"), 0);
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

    /// <summary>
    /// Quits via NSApplication so the app tears down the way the Quit menu item
    /// does. Used by the updater to get out of the way of the swap helper.
    /// </summary>
    public static void Terminate()
    {
        try
        {
            // SharedApp() and SendPtr are the helpers this class already
            // declares; terminate: returns void, and objc_msgSend's IntPtr
            // return is simply ignored. Do NOT add a SendVoid overload — that
            // one belongs to MacStatusBar.
            var app = SharedApp();
            if (app != IntPtr.Zero) SendPtr(app, Sel("terminate:"), IntPtr.Zero);
            else Environment.Exit(0);
        }
        catch { Environment.Exit(0); }
    }
}
