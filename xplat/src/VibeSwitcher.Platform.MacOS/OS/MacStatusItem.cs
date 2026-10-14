using System.Runtime.InteropServices;

namespace VibeSwitcher.Platform.MacOS.OS;

// The app's own menu-bar item (NSStatusItem), through the Objective-C runtime: Avalonia's tray icon
// never reports clicks on macOS, and the app opens its own styled menu on one. It shows the icon and
// the hover text, raises Clicked on a left or right press (as menu-bar menus open), and says where it
// is so the menu can open right under it. AppKit: main thread only (Avalonia's UI thread on macOS).
public sealed class MacStatusItem : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize { public double Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);
    [DllImport(ObjC)] private static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg, nuint length);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendNInt(IntPtr receiver, IntPtr selector, nint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendDouble(IntPtr receiver, IntPtr selector, double arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr selector, byte arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendSize(IntPtr receiver, IntPtr selector, CGSize arg);
    // A rect comes back in registers on Apple silicon, through a hidden pointer on Intel (_stret).
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CGRect SendRect(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] private static extern void SendRectStret(out CGRect result, IntPtr receiver, IntPtr selector);

    private static IntPtr Sel(string name) => sel_registerName(name);

    private static CGRect Rect(IntPtr receiver, string selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            SendRectStret(out var rect, receiver, Sel(selector));
            return rect;
        }
        return SendRect(receiver, Sel(selector));
    }

    // The click target: one small Objective-C class with an action method that calls back here.
    private const string TargetClassName = "VibeSwitcherStatusItemTarget";
    private static readonly Dictionary<IntPtr, MacStatusItem> Items = new();
    private static IntPtr _targetClass;

    private static unsafe IntPtr TargetClass()
    {
        if (_targetClass != IntPtr.Zero) return _targetClass;
        var cls = objc_allocateClassPair(objc_getClass("NSObject"), TargetClassName, 0);
        if (cls == IntPtr.Zero) return _targetClass = objc_getClass(TargetClassName); // (registered already)
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> action = &OnAction;
        class_addMethod(cls, Sel("vsClicked:"), (IntPtr)action, "v@:@");
        objc_registerClassPair(cls);
        return _targetClass = cls;
    }

    [UnmanagedCallersOnly]
    private static void OnAction(IntPtr self, IntPtr selector, IntPtr sender)
    {
        // (Nothing may escape into Objective-C.)
        try
        {
            MacStatusItem? item;
            lock (Items) Items.TryGetValue(self, out item);
            item?.Clicked?.Invoke();
        }
        catch (Exception) { }
    }

    private IntPtr _item;    // NSStatusItem (retained)
    private IntPtr _button;  // its NSStatusBarButton
    private IntPtr _target;  // our target object (owned)

    public event Action? Clicked;

    private MacStatusItem() { }

    // Null when it can't be made (not macOS, or AppKit refused): the app then uses Avalonia's tray
    // icon with the system's own menu.
    public static MacStatusItem? TryCreate()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
            var bar = Send(objc_getClass("NSStatusBar"), Sel("systemStatusBar"));
            var statusItem = SendDouble(bar, Sel("statusItemWithLength:"), -1.0); // (NSVariableStatusItemLength)
            if (statusItem == IntPtr.Zero) return null;
            Send(statusItem, Sel("retain"));
            var button = Send(statusItem, Sel("button"));
            var target = Send(Send(TargetClass(), Sel("alloc")), Sel("init"));
            if (button == IntPtr.Zero || target == IntPtr.Zero) return null;
            var created = new MacStatusItem { _item = statusItem, _button = button, _target = target };
            lock (Items) Items[target] = created;
            Send(button, Sel("setTarget:"), target);
            Send(button, Sel("setAction:"), Sel("vsClicked:"));
            // Opens on the press of either button, as menu-bar menus do.
            const nint LeftMouseDown = 1 << 1, RightMouseDown = 1 << 3;
            SendNInt(button, Sel("sendActionOn:"), LeftMouseDown | RightMouseDown);
            created.SetVisible(false);
            return created;
        }
        catch (Exception) { return null; }
    }

    // A picture (PNG or any format macOS reads), drawn at 18 × 18 points (menu-bar size).
    public void SetImage(byte[] imageData)
    {
        if (_button == IntPtr.Zero) return;
        var handle = GCHandle.Alloc(imageData, GCHandleType.Pinned);
        try
        {
            var data = Send(objc_getClass("NSData"), Sel("dataWithBytes:length:"), handle.AddrOfPinnedObject(), (nuint)imageData.Length);
            var image = Send(Send(objc_getClass("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
            if (image == IntPtr.Zero) return;
            SendSize(image, Sel("setSize:"), new CGSize { Width = 18, Height = 18 });
            Send(_button, Sel("setImage:"), image);
            Send(image, Sel("release"));
        }
        finally { handle.Free(); }
    }

    public void SetToolTip(string text)
    {
        if (_button == IntPtr.Zero) return;
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            var str = Send(Send(objc_getClass("NSString"), Sel("alloc")), Sel("initWithUTF8String:"), utf8);
            Send(_button, Sel("setToolTip:"), str);
            Send(str, Sel("release"));
        }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    public void SetVisible(bool visible)
    {
        if (_item != IntPtr.Zero) SendBool(_item, Sel("setVisible:"), visible ? (byte)1 : (byte)0);
    }

    // The point just under the item's left edge, in points from the main display's top-left (where
    // the app's windows are placed); null when it can't be told.
    public (double X, double Y)? BelowItem()
    {
        if (_button == IntPtr.Zero) return null;
        try
        {
            var window = Send(_button, Sel("window"));
            var main = Send(Send(objc_getClass("NSScreen"), Sel("screens")), Sel("firstObject"));
            if (window == IntPtr.Zero || main == IntPtr.Zero) return null;
            var frame = Rect(window, "frame");     // (screen coordinates: from the main display's bottom-left)
            var screen = Rect(main, "frame");
            return (frame.X, screen.Height - frame.Y);
        }
        catch (Exception) { return null; }
    }

    // As if the user pressed it (the CI check, and a keyboard way in later).
    public void PerformClick()
    {
        if (_button != IntPtr.Zero) Send(_button, Sel("performClick:"), IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_item == IntPtr.Zero) return;
        try
        {
            Send(Send(objc_getClass("NSStatusBar"), Sel("systemStatusBar")), Sel("removeStatusItem:"), _item);
            Send(_item, Sel("release"));
            Send(_target, Sel("release"));
        }
        catch (Exception) { }
        lock (Items) Items.Remove(_target);
        _item = _button = _target = IntPtr.Zero;
    }
}
