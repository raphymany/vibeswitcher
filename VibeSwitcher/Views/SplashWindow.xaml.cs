using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VibeSwitcher.Views;

public partial class SplashWindow : Window
{
    private Storyboard? _loopStoryboard;
    public event EventHandler? AnimationComplete;

    public SplashWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ((Storyboard)Resources["PressStoryboard"]).Begin(this);

        // At t=2060ms: switch bars from press-wave to looping equalizer
        await Task.Delay(2060);
        if (!IsLoaded) return;
        StartLoopingEqualizer();

        // At t=3400ms: stop loop, let press storyboard fade-out run
        await Task.Delay(1340); // 3400 - 2060
        if (!IsLoaded) return;
        _loopStoryboard?.Stop(this);

        // At t=3750ms: close and signal app to open main window
        await Task.Delay(350); // 3750 - 3400
        AnimationComplete?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void StartLoopingEqualizer()
    {
        _loopStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        AddEqAnimation(_loopStoryboard, Bar1Scale, 0.52, new[]
        {
            (0.00, 0.60), (0.50, 2.40), (1.00, 0.60)
        });
        AddEqAnimation(_loopStoryboard, Bar5Scale, 0.48, new[]
        {
            (0.00, 0.55), (0.55, 2.20), (1.00, 0.55)
        });
        AddEqAnimation(_loopStoryboard, Bar2Scale, 0.41, new[]
        {
            (0.00, 0.70), (0.38, 1.85), (0.72, 0.55), (1.00, 0.70)
        });
        AddEqAnimation(_loopStoryboard, Bar4Scale, 0.45, new[]
        {
            (0.00, 0.65), (0.42, 1.90), (0.78, 0.60), (1.00, 0.65)
        });
        AddEqAnimation(_loopStoryboard, Bar3Scale, 0.36, new[]
        {
            (0.00, 0.50), (0.30, 1.70), (0.65, 0.75), (1.00, 0.50)
        });

        _loopStoryboard.Begin(this, true);
    }

    private static void AddEqAnimation(
        Storyboard sb,
        ScaleTransform target,
        double durationSeconds,
        (double pct, double scale)[] frames)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(durationSeconds)
        };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        foreach (var (pct, scale) in frames)
        {
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(scale,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(pct * durationSeconds)))
            {
                EasingFunction = ease
            });
        }
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(ScaleTransform.ScaleYProperty));
        sb.Children.Add(anim);
    }
}
