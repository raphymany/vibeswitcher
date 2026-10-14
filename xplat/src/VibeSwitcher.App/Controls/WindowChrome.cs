using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.VisualTree;

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

    // How far in from a frameless window's edge the pointer resizes it (WPF's ResizeBorderThickness).
    public const double ResizeBorder = 8;

    // Lets a resizable frameless (Linux) window be resized from its edges and corners, as the
    // system frame would; macOS windows keep the system's own resizing.
    public static void EnableEdgeResize(Window window)
    {
        if (Style == ChromeStyle.Mac) return;
        window.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            var edge = OnButton(e.Source) ? null : EdgeAt(window, e.GetPosition(window));
            window.Cursor = edge is { } where ? Cursors[where] : null;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            // (The title bar's buttons reach the window's edge; they keep their clicks, as in WPF.)
            if (OnButton(e.Source)) return;
            if (EdgeAt(window, e.GetPosition(window)) is { } edge && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            {
                window.BeginResizeDrag(edge, e);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private static bool OnButton(object? source) =>
        source is Visual v && (v is Button || v.FindAncestorOfType<Button>() != null);

    // (Made on first use: a cursor needs the running app.)
    private static Dictionary<WindowEdge, Cursor>? _cursors;
    private static Dictionary<WindowEdge, Cursor> Cursors =>
        _cursors ??= Enum.GetValues<WindowEdge>().ToDictionary(edge => edge, edge => new Cursor(CursorFor(edge)));

    internal static WindowEdge? EdgeAt(Window window, Point p)
    {
        if (!window.CanResize || window.WindowState != WindowState.Normal) return null;
        bool left = p.X < ResizeBorder, right = p.X > window.Bounds.Width - ResizeBorder;
        bool top = p.Y < ResizeBorder, bottom = p.Y > window.Bounds.Height - ResizeBorder;
        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (true, _, _, true) => WindowEdge.NorthEast,
            (_, true, true, _) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.North,
            (_, true, _, _) => WindowEdge.South,
            (_, _, true, _) => WindowEdge.West,
            (_, _, _, true) => WindowEdge.East,
            _ => null,
        };
    }

    private static StandardCursorType CursorFor(WindowEdge edge) => edge switch
    {
        WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
        WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
        WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
        WindowEdge.SouthEast => StandardCursorType.BottomRightCorner,
        WindowEdge.North => StandardCursorType.TopSide,
        WindowEdge.South => StandardCursorType.BottomSide,
        WindowEdge.West => StandardCursorType.LeftSide,
        _ => StandardCursorType.RightSide,
    };
}
