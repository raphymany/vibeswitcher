using System.Runtime.InteropServices;

namespace VibeSwitcher.Platform.MacOS.OS;

// Window behaviour Avalonia doesn't offer on macOS. Main thread only.
public static class MacWindows
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendNInt(IntPtr receiver, IntPtr selector, nint arg);

    // A banner shows on whichever desktop (Space) is in front — also over an app in full screen, as
    // system banners do: NSWindowCollectionBehaviorCanJoinAllSpaces | FullScreenAuxiliary.
    public static void ShowOnEverySpace(IntPtr nsWindow)
    {
        if (!OperatingSystem.IsMacOS() || nsWindow == IntPtr.Zero) return;
        const nint CanJoinAllSpaces = 1 << 0, FullScreenAuxiliary = 1 << 8;
        try { SendNInt(nsWindow, sel_registerName("setCollectionBehavior:"), CanJoinAllSpaces | FullScreenAuxiliary); }
        catch (Exception) { /* (shown on the current desktop only) */ }
    }
}
