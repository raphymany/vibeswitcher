using System.Windows.Controls;

namespace VibeSwitcher.Controls;

public partial class AboutLogoIcon : UserControl
{
    public AboutLogoIcon()
    {
        InitializeComponent();
        LogoAnimator.Attach(this);
    }
}
