using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/SplashWindow.xaml.cs: the logo animation shown at start-up, then AnimationComplete
// and the window closes (3.75 s, as on Windows). Drawn from SplashTimeline at ~60 frames a second.
public partial class SplashWindow : Window
{
    public event EventHandler? AnimationComplete;

    private readonly bool _play;
    private readonly TranslateTransform _v;
    private readonly TranslateTransform _text;
    private readonly ScaleTransform[] _bars;
    private readonly Control _content;
    private readonly Control _textGroup;
    private DispatcherTimer? _timer;

    public SplashWindow() : this(play: true) { }

    // play: false shows the splash still, everything in place (for screenshots).
    internal SplashWindow(bool play)
    {
        AvaloniaXamlLoader.Load(this);
        _play = play;
        _v = (TranslateTransform)this.FindControl<Path>("VChevron")!.RenderTransform!;
        _bars = Enumerable.Range(1, 5)
            .Select(i => (ScaleTransform)this.FindControl<Rectangle>($"Bar{i}")!.RenderTransform!)
            .ToArray();
        _content = this.FindControl<StackPanel>("RootPanel")!;
        _textGroup = this.FindControl<StackPanel>("TextGroup")!;
        _text = (TranslateTransform)_textGroup.RenderTransform!;
        var clock = new Stopwatch();
        Elapsed = () => clock.Elapsed;
        CanSeeThrough = () => ActualTransparencyLevel != WindowTransparencyLevel.None;
        ShowPose(play ? SplashTimeline.At(0) : SplashTimeline.Rest);
        Opened += (_, _) =>
        {
            FitToSystem();
            if (!_play) return;
            clock.Start();
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 60), DispatcherPriority.Render, (_, _) => Tick());
            _timer.Start();
        };
    }

    // The animation's clock, and whether the window can be see-through (tests stand in for them).
    internal Func<TimeSpan> Elapsed { get; set; }
    internal Func<bool> CanSeeThrough { get; set; }

    internal void Tick()
    {
        var t = Elapsed().TotalSeconds;
        if (t < SplashTimeline.End)
        {
            ShowPose(SplashTimeline.At(t));
            return;
        }
        StopTimer();
        AnimationComplete?.Invoke(this, EventArgs.Empty);
        Close();
    }

    internal void ShowPose(SplashTimeline.Pose pose)
    {
        _content.Opacity = pose.Content;
        _v.Y = pose.V;
        for (int i = 0; i < _bars.Length; i++) _bars[i].ScaleY = pose.Bars[i];
        _textGroup.Opacity = pose.Text;
        _text.Y = pose.TextY;
    }

    internal SplashTimeline.Pose Shown =>
        new(_content.Opacity, _v.Y, _bars.Select(b => b.ScaleY).ToArray(), _textGroup.Opacity, _text.Y);

    internal bool IsPlaying => _timer != null;

    // Without a see-through window (plain X11 with no compositor) the space kept for the shadow
    // would show black: the card fills the window instead, square.
    private void FitToSystem()
    {
        if (CanSeeThrough()) return;
        this.FindControl<Border>("ShadowSpace")!.Margin = default;
        var card = this.FindControl<Border>("Card")!;
        card.CornerRadius = default;
        card.BoxShadow = default;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StopTimer();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}
