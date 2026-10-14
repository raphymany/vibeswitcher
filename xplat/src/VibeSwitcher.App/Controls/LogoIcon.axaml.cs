using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using VibeSwitcher.Controls;

namespace VibeSwitcher.App.Controls;

// The animated VibeSwitcher logo (NavLogoIcon / AboutLogoIcon on Windows). The Windows storyboards'
// key frames are kept exactly — the V breathes over 1.8 s, each bar pulses at its own rate, started
// part-way through as the Windows "negative BeginTime"s do — but one shared clock drives every logo
// (the ADR's single-clock LogoAnimator), at full rate, ~24 fps (Reduced) or not at all (Static),
// following Settings → Appearance → Logo animation live. Only logos in a showing window are animated.
public partial class LogoIcon : UserControl
{
    public static readonly StyledProperty<bool> IsAboutProperty =
        AvaloniaProperty.Register<LogoIcon, bool>(nameof(IsAbout));

    // The About version: a stronger outline and glow.
    public bool IsAbout { get => GetValue(IsAboutProperty); set => SetValue(IsAboutProperty, value); }

    private readonly TranslateTransform _v;
    private readonly ScaleTransform[] _bars;

    public LogoIcon()
    {
        AvaloniaXamlLoader.Load(this);
        _v = (TranslateTransform)this.FindControl<Path>("VChevron")!.RenderTransform!;
        _bars = Enumerable.Range(1, 5)
            .Select(i => (ScaleTransform)this.FindControl<Rectangle>($"Bar{i}")!.RenderTransform!)
            .ToArray();
        ApplyVariant();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsAboutProperty) ApplyVariant();
    }

    private void ApplyVariant()
    {
        this.FindControl<Border>("Outline")!.BorderBrush = new SolidColorBrush(Color.Parse(IsAbout ? "#59F5820A" : "#38F5820A"));
        this.FindControl<Border>("Glow")!.Background = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.75, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.75, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.55, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(IsAbout ? "#17F5820A" : "#0DF5820A"), 0),
                new GradientStop(Color.Parse("#00F5820A"), 1),
            },
        };
    }

    private IDisposable? _windowShown;

    // Animated only while its window is showing (closing Settings to the tray hides it, and a hidden
    // window's logos mustn't keep the clock running).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
            _windowShown = top.GetObservable(IsVisibleProperty).Subscribe(new Observer(shown =>
            {
                if (shown) Clock.Add(this);
                else { Clock.Remove(this); ShowRest(); }
            }));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _windowShown?.Dispose();
        _windowShown = null;
        Clock.Remove(this);
        ShowRest();
    }

    // The pose at a moment of the animation.
    internal void Show(TimeSpan t)
    {
        double s = t.TotalSeconds;
        _v.Y = VBreath.At(s);
        for (int i = 0; i < _bars.Length; i++) _bars[i].ScaleY = Bars[i].At(s);
    }

    // The logo at rest (what Static shows, and WPF's stopped storyboards): the V level, every bar
    // at its own height.
    internal void ShowRest()
    {
        _v.Y = 0;
        foreach (var bar in _bars) bar.ScaleY = 1;
    }

    internal double RestY => _v.Y;
    internal double BarScale(int i) => _bars[i].ScaleY;

    private sealed class Observer(Action<bool> onNext) : IObserver<bool>
    {
        public void OnNext(bool value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    // ── The Windows key frames ──────────────────────────────────────────────────────────────────────
    internal sealed record Track(double Duration, double BeginTime, (double Time, double Value)[] Keys)
    {
        // Each segment eases in and out (SineEase EaseInOut) into its key frame. As in WPF, the
        // storyboard repeats every Duration + BeginTime, so with a negative BeginTime each cycle starts
        // part-way through the key frames (and the bar jumps back there).
        public double At(double seconds)
        {
            double period = Duration + BeginTime;
            double local = seconds % period;
            if (local < 0) local += period;
            double p = local - BeginTime;
            for (int i = 1; i < Keys.Length; i++)
            {
                var (t0, v0) = Keys[i - 1];
                var (t1, v1) = Keys[i];
                if (p > t1) continue;
                double x = (p - t0) / (t1 - t0);
                return v0 + (v1 - v0) * (1 - Math.Cos(Math.PI * x)) / 2;
            }
            return Keys[^1].Value;
        }
    }

    internal static readonly Track VBreath = new(1.8, 0, [(0, 0), (0.54, -5), (1.26, 2.5), (1.8, 0)]);

    internal static readonly Track[] Bars =
    [
        new(0.85, -0.22, [(0, 1), (0.34, 2.2), (0.612, 0.55), (0.85, 1)]),
        new(0.68, -0.14, [(0, 1), (0.238, 1.9), (0.462, 0.55), (0.68, 1)]),
        new(0.60, 0, [(0, 1), (0.18, 1.75), (0.39, 0.65), (0.60, 1)]),
        new(0.74, -0.30, [(0, 1), (0.281, 1.85), (0.533, 0.6), (0.74, 1)]),
        new(0.78, -0.40, [(0, 1), (0.351, 2.0), (0.562, 0.55), (0.78, 1)]),
    ];

    // ── The shared clock ───────────────────────────────────────────────────────────────────────────
    internal static class Clock
    {
        private static readonly List<LogoIcon> Shown = [];
        private static readonly System.Diagnostics.Stopwatch Elapsed = System.Diagnostics.Stopwatch.StartNew();
        private static DispatcherTimer? _timer;

        static Clock() => LogoAnimator.ModeChanged += Restart;

        internal static bool IsRunning => _timer != null;
        internal static int ShownCount => Shown.Count;

        public static void Add(LogoIcon logo)
        {
            Shown.Add(logo);
            if (Shown.Count == 1) Restart();
        }

        public static void Remove(LogoIcon logo)
        {
            Shown.Remove(logo);
            if (Shown.Count == 0) Restart();
        }

        private static void Restart()
        {
            _timer?.Stop();
            _timer = null;
            if (Shown.Count == 0 || LogoAnimator.Mode == LogoAnimator.AnimMode.Static)
            {
                foreach (var logo in Shown) logo.ShowRest();
                return;
            }
            var interval = LogoAnimator.Mode == LogoAnimator.AnimMode.Reduced
                ? TimeSpan.FromSeconds(1.0 / LogoAnimator.ReducedFps)
                : TimeSpan.FromSeconds(1.0 / 60);
            _timer = new DispatcherTimer(interval, DispatcherPriority.Render, (_, _) => Tick());
            _timer.Start();
        }

        private static void Tick()
        {
            var t = Elapsed.Elapsed;
            foreach (var logo in Shown) if (logo.IsEffectivelyVisible) logo.Show(t); // (skips hidden ones, e.g. on cards with their own icon)
        }
    }
}
