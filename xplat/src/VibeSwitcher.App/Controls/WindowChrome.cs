using Avalonia.Controls;
using Avalonia.Platform;

namespace VibeSwitcher.App.Controls;

// How windows are framed on each system. Everything inside a window is the same everywhere; the
// frame follows the system:
//   macOS  — the native red/yellow/green buttons over the window's own title bar (the app's title
//            moves right to make room), native rounded corners and shadow
//   Linux  — no system frame: the app draws the Windows app's title bar and buttons itself (Linux
//            desktops differ too much for one native look), with the accent border
public enum ChromeStyle { Custom, Mac }

public static class WindowChrome
{
    // Settable so screenshots can show each system's frame on any machine.
    public static ChromeStyle Style { get; set; } = OperatingSystem.IsMacOS() ? ChromeStyle.Mac : ChromeStyle.Custom;

    public const double TitleBarHeight = 32;
    // Room the macOS buttons need on the left of the title bar.
    public const double MacButtonsWidth = 72;

    public static void Apply(Window window)
    {
        if (Style == ChromeStyle.Mac)
        {
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            window.ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
        }
        else
        {
            window.SystemDecorations = SystemDecorations.None;
        }
    }

    // The accent frame's corners: macOS windows are rounded; frameless Linux windows are square.
    public static double FrameCornerRadius => Style == ChromeStyle.Mac ? 10 : 0;
}
