using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pane.App;

/// <summary>
/// A macOS menu-bar (status bar) item created via the ObjC runtime. Left-click
/// toggles the launcher; right-click opens a menu. No permissions needed. Must run
/// on the main thread.
///
/// The menu is the only way out of the app: Pane runs as an accessory (see
/// MacApp.ConfigureAsLauncher), so there is no Dock tile to right-click and no
/// menu bar for Command-Q. Without a Quit item here the only way to stop Pane
/// would be to kill the process.
/// </summary>
internal static class MacStatusBar
{
    const string Objc = "/usr/lib/libobjc.A.dylib";

    [DllImport(Objc, EntryPoint = "objc_getClass")] static extern IntPtr GetClass(string name);
    [DllImport(Objc, EntryPoint = "sel_registerName")] static extern IntPtr Sel(string name);
    [DllImport(Objc, EntryPoint = "objc_allocateClassPair")] static extern IntPtr AllocateClassPair(IntPtr super, string name, nint extra);
    [DllImport(Objc, EntryPoint = "objc_registerClassPair")] static extern void RegisterClassPair(IntPtr cls);
    [DllImport(Objc, EntryPoint = "class_addMethod")] static extern bool ClassAddMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr r, IntPtr s);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendDouble(IntPtr r, IntPtr s, double a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendVoid(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendWithNUInt(IntPtr r, IntPtr s, nuint a);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendMenuItem(IntPtr r, IntPtr s, IntPtr title, IntPtr action, IntPtr key);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint SendNInt(IntPtr r, IntPtr s);

    // NSEventMaskLeftMouseUp (1 << NSEventTypeLeftMouseUp) | NSEventMaskRightMouseUp.
    // Without this the button only reports left clicks and the menu is unreachable.
    const nuint LeftAndRightMouseUp = (1 << 2) | (1 << 4);
    const nint RightMouseUp = 4;   // NSEventTypeRightMouseUp

    static Action? _onClick;
    static IntPtr _item, _target, _menu;   // kept alive for the process lifetime

    public static void Setup(string title, Action onClick)
    {
        _onClick = onClick;

        var statusBar = Send(GetClass("NSStatusBar"), Sel("systemStatusBar"));
        _item = SendDouble(statusBar, Sel("statusItemWithLength:"), -1.0 /* NSVariableStatusItemLength */);
        Send(_item, Sel("retain"));

        var button = Send(_item, Sel("button"));

        var titlePtr = Marshal.StringToHGlobalAnsi(title);
        var nsTitle = SendPtr(GetClass("NSString"), Sel("stringWithUTF8String:"), titlePtr);
        Marshal.FreeHGlobal(titlePtr);
        SendVoid(button, Sel("setTitle:"), nsTitle);

        // A tiny ObjC class with a handleClick: method that calls back into managed code.
        var cls = AllocateClassPair(GetClass("NSObject"), "PaneStatusTarget", 0);
        if (cls != IntPtr.Zero)
        {
            unsafe
            {
                delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void> imp = &HandleClick;
                ClassAddMethod(cls, Sel("handleClick:"), (IntPtr)imp, "v@:@");
            }
            RegisterClassPair(cls);
        }
        else
        {
            cls = GetClass("PaneStatusTarget"); // already registered (re-entry)
        }

        _target = Send(Send(cls, Sel("alloc")), Sel("init"));
        SendVoid(button, Sel("setTarget:"), _target);
        SendVoid(button, Sel("setAction:"), Sel("handleClick:"));
        SendWithNUInt(button, Sel("sendActionOn:"), LeftAndRightMouseUp);

        _menu = BuildMenu();
    }

    /// <summary>Right-click menu: open the launcher, or quit.</summary>
    static IntPtr BuildMenu()
    {
        var menu = Send(Send(GetClass("NSMenu"), Sel("alloc")), Sel("init"));
        if (menu == IntPtr.Zero) return IntPtr.Zero;
        Send(menu, Sel("retain"));

        // "Open Pane" reuses the click handler, so the menu and the button can
        // never drift apart.
        var open = SendMenuItem(menu, Sel("addItemWithTitle:action:keyEquivalent:"),
            NSString("Open Pane"), Sel("handleClick:"), NSString(""));
        if (open != IntPtr.Zero) SendVoid(open, Sel("setTarget:"), _target);

        var separator = Send(GetClass("NSMenuItem"), Sel("separatorItem"));
        if (separator != IntPtr.Zero) SendVoid(menu, Sel("addItem:"), separator);

        // terminate: is handled by NSApplication itself — no managed callback, so
        // quitting still works even if our own interop is in a bad state.
        var quit = SendMenuItem(menu, Sel("addItemWithTitle:action:keyEquivalent:"),
            NSString("Quit Pane"), Sel("terminate:"), NSString("q"));
        if (quit != IntPtr.Zero)
            SendVoid(quit, Sel("setTarget:"), Send(GetClass("NSApplication"), Sel("sharedApplication")));

        return menu;
    }

    static IntPtr NSString(string value)
    {
        var ptr = Marshal.StringToHGlobalAnsi(value);
        try { return SendPtr(GetClass("NSString"), Sel("stringWithUTF8String:"), ptr); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    static bool IsRightClick()
    {
        var app = Send(GetClass("NSApplication"), Sel("sharedApplication"));
        if (app == IntPtr.Zero) return false;
        var ev = Send(app, Sel("currentEvent"));
        return ev != IntPtr.Zero && SendNInt(ev, Sel("type")) == RightMouseUp;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void HandleClick(IntPtr self, IntPtr sel, IntPtr sender)
    {
        try
        {
            if (_menu != IntPtr.Zero && IsRightClick())
            {
                SendVoid(_item, Sel("popUpStatusItemMenu:"), _menu);
                return;
            }
            _onClick?.Invoke();
        }
        catch { /* ignore */ }
    }
}
