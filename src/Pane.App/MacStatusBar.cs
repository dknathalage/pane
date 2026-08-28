using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pane.App;

/// <summary>
/// A macOS menu-bar (status bar) item created via the ObjC runtime. Clicking it
/// toggles the launcher — no permissions needed. Must run on the main thread.
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

    static Action? _onClick;
    static IntPtr _item, _target;   // kept alive for the process lifetime

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
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void HandleClick(IntPtr self, IntPtr sel, IntPtr sender)
    {
        try { _onClick?.Invoke(); } catch { /* ignore */ }
    }
}
