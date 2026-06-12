using System.Windows.Controls;

namespace VibeSwitcher.Controls;

public partial class NavLogoIcon : UserControl
{
    public NavLogoIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => LogoAnimator.BeginAll(this);
        Unloaded += (_, _) => LogoAnimator.StopAll(this);
    }
}
