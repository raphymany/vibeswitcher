using System.Runtime.InteropServices;

namespace VibeSwitcher.NativeMethods;

internal static class WinApi
{
    public const int WM_HOTKEY = 0x0312;

    public const int MOD_ALT   = 0x0001;
    public const int MOD_CTRL  = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN   = 0x0008;

    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort GlobalAddAtom(string lpString);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern ushort GlobalDeleteAtom(ushort nAtom);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // Gives the process a stable identity so the taskbar groups it correctly and Windows resolves
    // toast/notification attribution to our icon (instead of a stale cached one).
    // Returns an HRESULT (S_OK == 0); a non-zero value means the identity wasn't applied.
    [DllImport("shell32.dll")]
    public static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);
}
