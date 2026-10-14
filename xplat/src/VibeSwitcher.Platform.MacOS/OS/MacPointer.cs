using System.Runtime.InteropServices;

namespace VibeSwitcher.Platform.MacOS.OS;

// Where the mouse pointer is, in points from the top-left of the main display (Quartz's global
// coordinates) — for opening the menu-bar icon's menu right under it. Null when it can't be read.
public static class MacPointer
{
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport(AppServices)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(AppServices)]
    private static extern CGPoint CGEventGetLocation(IntPtr evt);

    [DllImport(CoreFoundationLib)]
    private static extern void CFRelease(IntPtr cf);

    public static (double X, double Y)? Location()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            // An empty event's location is the pointer's current one.
            var evt = CGEventCreate(IntPtr.Zero);
            if (evt == IntPtr.Zero) return null;
            try
            {
                var p = CGEventGetLocation(evt);
                return (p.X, p.Y);
            }
            finally { CFRelease(evt); }
        }
        catch (Exception) { return null; }
    }
}
