using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VibeSwitcher.Controls;

public partial class NavLogoIcon : UserControl
{
    public NavLogoIcon()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ((Storyboard)Resources["VBreathStoryboard"]).Begin(this, true);
        ((Storyboard)Resources["Bar1Storyboard"]).Begin(this, true);
        ((Storyboard)Resources["Bar2Storyboard"]).Begin(this, true);
        ((Storyboard)Resources["Bar3Storyboard"]).Begin(this, true);
        ((Storyboard)Resources["Bar4Storyboard"]).Begin(this, true);
        ((Storyboard)Resources["Bar5Storyboard"]).Begin(this, true);
    }
}
