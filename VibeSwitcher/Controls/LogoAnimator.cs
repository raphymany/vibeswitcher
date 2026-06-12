using System.Windows;
using System.Windows.Media.Animation;

namespace VibeSwitcher.Controls;

// Shared begin/stop logic for the animated VibeSwitcher logo controls (NavLogoIcon, AboutLogoIcon),
// whose XAML each defines the same six perpetual (RepeatBehavior=Forever) storyboards. Stopping them
// on Unloaded keeps animation clocks from accumulating when the control re-attaches inside an
// ItemsControl template that is created/destroyed as profiles change.
internal static class LogoAnimator
{
    private static readonly string[] StoryboardKeys =
    {
        "VBreathStoryboard", "Bar1Storyboard", "Bar2Storyboard",
        "Bar3Storyboard", "Bar4Storyboard", "Bar5Storyboard"
    };

    public static void BeginAll(FrameworkElement control)
    {
        foreach (var key in StoryboardKeys)
            ((Storyboard)control.Resources[key]).Begin(control, true);
    }

    public static void StopAll(FrameworkElement control)
    {
        foreach (var key in StoryboardKeys)
            ((Storyboard)control.Resources[key]).Stop(control);
    }
}
