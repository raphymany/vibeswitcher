using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App.Hosting;

// One banner: shown at the top right of the main screen (under the menu bar), without taking the
// focus, fading in; it goes after a few seconds, or when clicked.
public partial class BannerWindow : Window
{
    private DispatcherTimer? _timer;

    public BannerWindow() : this("", "", TimeSpan.Zero) { } // (for the XAML loader)

    public BannerWindow(string title, string message, TimeSpan showFor)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<Image>("AppIcon")!.Source = AppIconBitmap.Value;
        PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        // Placed (and see-through) before it first appears, so it never shows elsewhere first.
        Opacity = 0;
        PlaceTopRight();
        Opened += (_, _) =>
        {
            PlaceTopRight();
            // macOS: on the desktop in front — also over an app in full screen.
            if (TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac)
                VibeSwitcher.Platform.MacOS.OS.MacWindows.ShowOnEverySpace(mac.NSWindow);
            if (showFor > TimeSpan.Zero)
            {
                _timer = new DispatcherTimer(showFor, DispatcherPriority.Normal, (_, _) => Close());
                _timer.Start();
            }
            // (A fade where the window can be see-through; elsewhere it just appears.)
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }];
            Opacity = 1;
        };
    }

    // The card is 320 + 2 × 12 wide (BannerWindow.axaml), so the place is known before it's laid out.
    private const double Width0 = 344;

    internal string TitleShown => this.FindControl<TextBlock>("TitleText")!.Text ?? "";
    internal string MessageShown => this.FindControl<TextBlock>("MessageText")!.Text ?? "";

    // Top right of the main screen's working area. (Window positions are in DesktopScaling units:
    // pixels on Linux, points on macOS.)
    private void PlaceTopRight()
    {
        if (Screens.Primary is not { } screen) return;
        Position = TopRight(screen.WorkingArea, Bounds.Width > 0 ? Bounds.Width : Width0, DesktopScaling);
    }

    internal static PixelPoint TopRight(PixelRect workingArea, double width, double desktopScaling) =>
        new(workingArea.Right - (int)Math.Ceiling(width * desktopScaling), workingArea.Y);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer?.Stop();
    }

    private static readonly Lazy<Bitmap?> AppIconBitmap = new(() =>
    {
        try { return new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VibeSwitcher/Assets/Icons/vs-icon-256.png"))); }
        catch (Exception) { return null; }
    });
}

// Banners the app draws itself (macOS, until the app is signed and can use the system's): one at a
// time — a new one replaces the one showing, as the Linux banners do.
internal sealed class BannerNotifier(TimeSpan showFor) : IDesktopNotifier
{
    private BannerWindow? _current;

    public BannerNotifier() : this(TimeSpan.FromSeconds(5)) { }

    internal BannerWindow? Current => _current is { IsVisible: true } ? _current : null;

    public Task<bool> ShowAsync(string title, string message) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _current?.Close();
                var banner = new BannerWindow(title, message, showFor);
                banner.Closed += (_, _) => { if (_current == banner) _current = null; };
                _current = banner;
                banner.Show();
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warning("BannerNotifier", $"Couldn't show a banner: {ex.Message}");
                return false;
            }
        }).GetTask();
}
