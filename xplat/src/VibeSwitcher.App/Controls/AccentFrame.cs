using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VibeSwitcher.App.Controls;

// Port of Views/AccentFrame.xaml: the thin orange window outline every dialog draws on top of its
// content (#66F5820A, 1 px), following the window's corners. Doesn't take clicks.
public sealed class AccentFrame : Border
{
    public AccentFrame()
    {
        IsHitTestVisible = false;
        BorderBrush = new SolidColorBrush(Color.Parse("#66F5820A"));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(WindowChrome.FrameCornerRadius);
        ZIndex = 9999;
    }
}
