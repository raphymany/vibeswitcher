using Avalonia;
using Avalonia.Controls;

namespace VibeSwitcher.App.Controls;

// The base of every VibeSwitcher dialog — what each Windows dialog sets in its XAML: titled
// "VibeSwitcher", not resizable, as tall as its content, centred on the window that opened it, kept
// out of the taskbar/Dock, the app background, and the system's frame (WindowChrome).
public class VsDialog : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    public VsDialog()
    {
        Title = "VibeSwitcher";
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Res.Bind(this, BackgroundProperty, "AppBg");
        Icon = AppIcon.Value;
        WindowChrome.Apply(this);
    }

    // The app icon for the taskbar/Dock and window switcher (Windows takes it from the .exe).
    private static readonly Lazy<WindowIcon?> AppIcon = new(() =>
    {
        try { return new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VibeSwitcher/Assets/Icons/vs-icon-256.png"))); }
        catch (Exception) { return null; }
    });

    // DialogResult on Windows: true only when closed with the dialog's "yes" button (closing it any
    // other way — the ✕, Escape, Cancel — leaves it false).
    public bool Result { get; private set; }

    protected void CloseWith(bool result)
    {
        Result = result;
        Close(result);
    }
}
