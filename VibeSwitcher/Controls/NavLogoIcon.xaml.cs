using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VibeSwitcher.Controls;

public partial class NavLogoIcon : UserControl
{
    private static readonly string[] StoryboardKeys =
    {
        "VBreathStoryboard", "Bar1Storyboard", "Bar2Storyboard",
        "Bar3Storyboard", "Bar4Storyboard", "Bar5Storyboard"
    };

    public NavLogoIcon()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var key in StoryboardKeys)
            ((Storyboard)Resources[key]).Begin(this, true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Stop the perpetual (RepeatBehavior=Forever) storyboards when the control detaches so
        // their animation clocks don't accumulate each time it re-attaches — these controls live
        // inside ItemsControl templates that are created/destroyed as profiles change.
        foreach (var key in StoryboardKeys)
            ((Storyboard)Resources[key]).Stop(this);
    }
}
