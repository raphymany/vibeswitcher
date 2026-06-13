using System.Windows.Controls;

namespace VibeSwitcher.Views;

// Shared rounded accent overlay used by every dialog/window (replaces a per-file Border).
public partial class AccentFrame : UserControl
{
    public AccentFrame() => InitializeComponent();
}
