using System.Windows.Controls;

namespace VibeSwitcher.Controls;

public partial class AboutLogoIcon : UserControl
{
    public AboutLogoIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => LogoAnimator.BeginAll(this);
        Unloaded += (_, _) => LogoAnimator.StopAll(this);
    }
}
