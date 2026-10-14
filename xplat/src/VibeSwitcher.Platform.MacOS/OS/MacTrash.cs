using System.Runtime.InteropServices;

namespace VibeSwitcher.Platform.MacOS.OS;

// Moving a file or app to the Trash, as Finder does (it can be put back from there):
// [[NSFileManager defaultManager] trashItemAtURL:[NSURL fileURLWithPath:path] resultingItemURL:nil error:&error].
public static class MacTrash
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendTrash(IntPtr receiver, IntPtr selector, IntPtr url, IntPtr resulting, out IntPtr error);

    // false with why not (macOS's own words) when it couldn't be moved.
    public static bool MoveToTrash(string path, out string? error)
    {
        error = "not macOS";
        if (!OperatingSystem.IsMacOS()) return false;
        var utf8 = Marshal.StringToCoTaskMemUTF8(path);
        // (Called from a thread-pool thread, which has no autorelease pool of its own.)
        var pool = objc_autoreleasePoolPush();
        try
        {
            var str = Send(Send(objc_getClass("NSString"), sel_registerName("alloc")), sel_registerName("initWithUTF8String:"), utf8);
            try
            {
                var url = Send(objc_getClass("NSURL"), sel_registerName("fileURLWithPath:"), str);
                var manager = Send(objc_getClass("NSFileManager"), sel_registerName("defaultManager"));
                if (url == IntPtr.Zero || manager == IntPtr.Zero) { error = "no file manager"; return false; }
                if (SendTrash(manager, sel_registerName("trashItemAtURL:resultingItemURL:error:"), url, IntPtr.Zero, out var nsError) != 0)
                {
                    error = null;
                    return true;
                }
                error = Describe(nsError) ?? "unknown error";
                return false;
            }
            finally { Send(str, sel_registerName("release")); }
        }
        catch (Exception ex) { error = ex.Message; return false; }
        finally
        {
            objc_autoreleasePoolPop(pool);
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    // An NSError's localizedDescription (read before the pool is emptied).
    private static string? Describe(IntPtr nsError)
    {
        if (nsError == IntPtr.Zero) return null;
        var description = Send(nsError, sel_registerName("localizedDescription"));
        return description == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(description, sel_registerName("UTF8String")));
    }
}
