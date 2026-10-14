using System.Runtime.InteropServices;

namespace VibeSwitcher.Platform.Linux.Hotkeys;

// The handful of Xlib/XCB calls global key grabs need (libX11.so.6 and libX11-xcb.so.1, present on
// every X11/XWayland desktop).
internal static class X11
{
    private const string Lib = "libX11.so.6";
    private const string XcbLib = "libxcb.so.1";
    private const string XlibXcbLib = "libX11-xcb.so.1";

    public const int KeyPress = 2;
    public const int MappingNotify = 34;
    public const byte GrabModeAsync = 1;
    public const byte BadAccess = 10;

    // Modifier masks
    public const uint ShiftMask = 1;
    public const uint LockMask = 2;     // Caps Lock
    public const uint ControlMask = 4;
    public const uint Mod1Mask = 8;     // Alt
    public const uint Mod4Mask = 64;    // Super / Windows key

    public const uint XK_Num_Lock = 0xFF7F;

    [DllImport(Lib)] public static extern int XInitThreads();
    [DllImport(Lib)] public static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] public static extern UIntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] public static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keysym);
    [DllImport(Lib)] public static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, UIntPtr grabWindow);
    [DllImport(Lib)] public static extern int XSync(IntPtr display, int discard);
    [DllImport(Lib)] public static extern int XPending(IntPtr display);
    [DllImport(Lib)] public static extern int XNextEvent(IntPtr display, IntPtr eventReturn);
    [DllImport(Lib)] public static extern int XConnectionNumber(IntPtr display);
    [DllImport(Lib)] public static extern int XRefreshKeyboardMapping(IntPtr mappingEvent);
    [DllImport(Lib)] public static extern IntPtr XGetModifierMapping(IntPtr display);
    [DllImport(Lib)] public static extern int XFreeModifiermap(IntPtr modmap);
    [DllImport(Lib)] public static extern int XQueryPointer(IntPtr display, UIntPtr w, out UIntPtr root, out UIntPtr child,
        out int rootX, out int rootY, out int winX, out int winY, out uint mask);

    // Grabs go through XCB's checked requests on the same connection: the server's answer (e.g.
    // BadAccess when another app holds the key) comes back to the caller directly, so no
    // process-wide Xlib error handler has to be swapped in — which could clash with the UI toolkit's.
    [DllImport(XlibXcbLib)] public static extern IntPtr XGetXCBConnection(IntPtr display);
    [DllImport(XcbLib)] public static extern uint xcb_grab_key_checked(IntPtr conn, byte ownerEvents, uint grabWindow,
        ushort modifiers, byte key, byte pointerMode, byte keyboardMode);
    [DllImport(XcbLib)] public static extern IntPtr xcb_request_check(IntPtr conn, uint cookie);

    // XEvent is a union of 24 longs (192 bytes on 64-bit). For a KeyPress (XKeyEvent) on 64-bit:
    // type @0, ... state (unsigned int) @80, keycode (unsigned int) @84.
    public const int XEventSize = 192;
    public const int KeyEventStateOffset = 80;
    public const int KeyEventKeycodeOffset = 84;
    // xcb_generic_error_t: { uint8 response_type; uint8 error_code; … }
    public const int XcbErrorCodeOffset = 1;
    // XModifierKeymap: { int max_keypermod; KeyCode* modifiermap; }
    public const int ModmapKeysOffset = 8;
}

internal static class LibC
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    public const short PollIn = 1;

    [DllImport("libc", SetLastError = true)]
    public static extern int poll([In, Out] PollFd[] fds, nuint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    public static extern int pipe([Out] int[] fds);

    [DllImport("libc", SetLastError = true)]
    public static extern nint write(int fd, byte[] buf, nuint count);

    [DllImport("libc", SetLastError = true)]
    public static extern nint read(int fd, byte[] buf, nuint count);

    [DllImport("libc")]
    public static extern int close(int fd);

    [DllImport("libc")]
    public static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("libc")]
    public static extern void free(IntPtr ptr);

    public const int F_SETFL = 4;
    public const int O_NONBLOCK_LINUX = 0x800;
}
