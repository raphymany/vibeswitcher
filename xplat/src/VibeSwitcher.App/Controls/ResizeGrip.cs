using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace VibeSwitcher.App.Controls;

// The dotted triangle WPF draws in a resizable window's bottom-right corner (CanResizeWithGrip).
// Dragging it resizes the window (Linux, where the app draws its own frame; macOS resizes natively).
public sealed class ResizeGrip : Control
{
    private static readonly (double X, double Y)[] Dots = [(9, 1), (5, 5), (9, 5), (1, 9), (5, 9), (9, 9)];

    public ResizeGrip()
    {
        Width = 12;
        Height = 12;
        Margin = new Thickness(0, 0, 2, 2);
        Cursor = new Cursor(StandardCursorType.BottomRightCorner);
        Res.Bind(this, ForegroundProperty, "DisabledText");
    }

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ResizeGrip, IBrush?>(nameof(Foreground));

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    static ResizeGrip() => AffectsRender<ResizeGrip>(ForegroundProperty);

    public override void Render(DrawingContext context)
    {
        if (Foreground == null) return;
        foreach (var (x, y) in Dots) context.FillRectangle(Foreground, new Rect(x, y, 2, 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TopLevel.GetTopLevel(this) is Window { CanResize: true, WindowState: WindowState.Normal } window
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        }
    }
}
