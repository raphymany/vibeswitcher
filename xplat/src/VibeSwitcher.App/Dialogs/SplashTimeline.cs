namespace VibeSwitcher.App.Dialogs;

// The start-up splash's animation, worked out from the Windows storyboards (Views/SplashWindow.xaml
// "PressStoryboard" and the looping equalizer in SplashWindow.xaml.cs) with their key times, values
// and easings kept exactly. One function of time, so the window just draws Pose.At(t) each frame:
//   0–0.3 s       the content fades in
//   0.42–2.02 s   the V presses down and springs back while the bars ripple outwards
//   1.82–2.07 s   the text rises in
//   2.06–3.4 s    the bars loop like an equalizer (the Windows code starts it 2060 ms after load)
//   3.4–3.75 s    the content fades out; the window closes at 3.75 s
internal static class SplashTimeline
{
    public const double LoopStart = 2.06, LoopEnd = 3.4, End = 3.75;

    // The moment's values: content opacity, the V's drop, the five bars' heights (×), text opacity
    // and drop.
    public sealed record Pose(double Content, double V, double[] Bars, double Text, double TextY);

    // Everything shown and still (what the splash looks like between the moves).
    public static Pose Rest { get; } = new(1, 0, [1, 1, 1, 1, 1], 1, 0);

    public static Pose At(double t)
    {
        var content = t < LoopEnd ? FadeIn.At(t) : FadeOut.At(t);
        var bars = new double[5];
        for (int i = 0; i < 5; i++)
            bars[i] = t >= LoopStart && t < LoopEnd ? Loop[i].At(t - LoopStart) : Press[i].At(t);
        return new Pose(content, VPress.At(t), bars, TextIn.At(t), TextRise.At(t));
    }

    internal enum Ease { Linear, CubicIn, CubicOut, CubicInOut, SineInOut }

    internal static double Apply(Ease ease, double x) => ease switch
    {
        Ease.CubicIn => x * x * x,
        Ease.CubicOut => 1 - Math.Pow(1 - x, 3),
        Ease.CubicInOut => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2,
        Ease.SineInOut => (1 - Math.Cos(Math.PI * x)) / 2,
        _ => x,
    };

    // A WPF key-frame animation: before BeginTime the property keeps its own value (Base); each key
    // frame eases from the previous value to its own by its key time; after the last it holds.
    internal sealed record Track(double Base, double BeginTime, (double Time, double Value, Ease Ease)[] Keys)
    {
        public double At(double t)
        {
            var local = t - BeginTime;
            if (local < 0) return Base;
            var (prevTime, prevValue) = (0.0, Base);
            foreach (var (time, value, ease) in Keys)
            {
                if (local <= time)
                    return time <= prevTime ? value : prevValue + (value - prevValue) * Apply(ease, (local - prevTime) / (time - prevTime));
                (prevTime, prevValue) = (time, value);
            }
            return prevValue;
        }
    }

    // A From → To animation over Duration, from BeginTime.
    private static Track Tween(double from, double to, double begin, double duration, Ease ease) =>
        new(from, begin, [(0, from, Ease.Linear), (duration, to, ease)]);

    private static readonly Track FadeIn = Tween(0, 1, 0, 0.3, Ease.CubicOut);
    private static readonly Track FadeOut = Tween(1, 0, 3.4, 0.35, Ease.CubicIn);
    private static readonly Track TextIn = Tween(0, 1, 1.82, 0.25, Ease.CubicOut);
    private static readonly Track TextRise = Tween(6, 0, 1.82, 0.25, Ease.CubicOut);

    private static readonly Track VPress = new(0, 0.42,
    [
        (0, 0, Ease.Linear), (0.416, 0, Ease.Linear), (0.624, -16, Ease.CubicOut), (0.832, 11, Ease.CubicIn),
        (1.024, -5, Ease.CubicOut), (1.216, 1.5, Ease.CubicInOut), (1.408, -0.5, Ease.Linear), (1.6, 0, Ease.CubicOut),
    ]);

    // Bars 1–5 (outer left → outer right): the press ripple, from the centre outwards.
    private static readonly Track Outer = new(1, 0.42,
    [
        (0, 1, Ease.Linear), (0.864, 1, Ease.Linear), (0.976, 0.5, Ease.CubicIn), (1.136, 1.7, Ease.CubicOut),
        (1.280, 0.8, Ease.Linear), (1.424, 1.25, Ease.Linear), (1.536, 0.96, Ease.Linear), (1.6, 1, Ease.Linear),
    ]);
    private static readonly Track Inner = new(1, 0.42,
    [
        (0, 1, Ease.Linear), (0.816, 1, Ease.Linear), (0.928, 0.38, Ease.CubicIn), (1.088, 2.1, Ease.CubicOut),
        (1.232, 0.7, Ease.Linear), (1.376, 1.42, Ease.Linear), (1.504, 0.92, Ease.Linear), (1.6, 1, Ease.Linear),
    ]);
    private static readonly Track Centre = new(1, 0.42,
    [
        (0, 1, Ease.Linear), (0.768, 1, Ease.Linear), (0.880, 0.28, Ease.CubicIn), (1.040, 2.6, Ease.CubicOut),
        (1.184, 0.62, Ease.Linear), (1.328, 1.6, Ease.Linear), (1.472, 0.88, Ease.Linear), (1.6, 1, Ease.Linear),
    ]);
    private static readonly Track[] Press = [Outer, Inner, Centre, Inner, Outer];

    // The equalizer: each bar's move (seconds, then the key points as fractions of it), easing in and
    // out between them. As in WPF, the storyboard holding them repeats at the pace of the longest
    // (0.52 s): a shorter move holds its last value until they all start again.
    internal const double LoopPeriod = 0.52;

    internal sealed record Cycle(double Duration, (double Fraction, double Value)[] Keys)
    {
        public double At(double t)
        {
            var local = t % LoopPeriod / Duration;
            if (local >= 1) return Keys[^1].Value;
            for (int i = 1; i < Keys.Length; i++)
            {
                var (f0, v0) = Keys[i - 1];
                var (f1, v1) = Keys[i];
                if (local <= f1) return v0 + (v1 - v0) * Apply(Ease.SineInOut, (local - f0) / (f1 - f0));
            }
            return Keys[^1].Value;
        }
    }

    private static readonly Cycle[] Loop =
    [
        new(0.52, [(0.00, 0.60), (0.50, 2.40), (1.00, 0.60)]),
        new(0.41, [(0.00, 0.70), (0.38, 1.85), (0.72, 0.55), (1.00, 0.70)]),
        new(0.36, [(0.00, 0.50), (0.30, 1.70), (0.65, 0.75), (1.00, 0.50)]),
        new(0.45, [(0.00, 0.65), (0.42, 1.90), (0.78, 0.60), (1.00, 0.65)]),
        new(0.48, [(0.00, 0.55), (0.55, 2.20), (1.00, 0.55)]),
    ];
}
