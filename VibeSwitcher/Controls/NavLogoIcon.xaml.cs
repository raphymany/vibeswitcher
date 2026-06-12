using System.Windows.Controls;

namespace VibeSwitcher.Controls;

public partial class NavLogoIcon : UserControl
{
    public NavLogoIcon()
    {
        InitializeComponent();
        LogoAnimator.Attach(this);
    }
}
