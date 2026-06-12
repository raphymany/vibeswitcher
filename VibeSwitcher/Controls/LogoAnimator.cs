using System.Windows;
using System.Windows.Media.Animation;

namespace VibeSwitcher.Controls;

// Shared begin/stop logic for the animated VibeSwitcher logo controls (NavLogoIcon, AboutLogoIcon),
// whose XAML each defines the same six perpetual (RepeatBehavior=Forever) storyboards.
//
// The animation runs continuously while the window is open, so it has a real per-frame CPU cost.
// The user can pick how much of that to pay via Settings → Appearance → Logo animation:
//   Full    — default 60fps
//   Reduced — capped to ~24fps (roughly half the CPU)
//   Static  — no animation at all
// Mode changes apply live to every loaded logo control via the ModeChanged event.
internal static class LogoAnimator
{
    public enum AnimMode { Full, Reduced, Static }

    private const int ReducedFps = 24;

    private static readonly string[] StoryboardKeys =
    {
        "VBreathStoryboard", "Bar1Storyboard", "Bar2Storyboard",
        "Bar3Storyboard", "Bar4Storyboard", "Bar5Storyboard"
    };

    private static AnimMode _mode = AnimMode.Full;
    public static event Action? ModeChanged;

    public static AnimMode Mode
    {
        get => _mode;
        set { if (_mode == value) return; _mode = value; ModeChanged?.Invoke(); }
    }

    // Maps the persisted config string to a mode (unknown -> Full).
    public static AnimMode Parse(string? value) => value switch
    {
        "Reduced" => AnimMode.Reduced,
        "Static"  => AnimMode.Static,
        _         => AnimMode.Full,
    };

    // Wire a logo control to the current mode: applies on load, re-applies live on mode changes,
    // and stops + detaches on unload (so the perpetual storyboards don't keep ticking off-screen).
    public static void Attach(FrameworkElement control)
    {
        Action apply = () => Apply(control);
        control.Loaded   += (_, _) => { Apply(control); ModeChanged += apply; };
        control.Unloaded += (_, _) => { ModeChanged -= apply; StopAll(control); };
    }

    private static void Apply(FrameworkElement control)
    {
        StopAll(control);
        if (_mode == AnimMode.Static) return;   // leave the logo at its static rest pose
        foreach (var key in StoryboardKeys)
        {
            var sb = (Storyboard)control.Resources[key];
            // null clears the cap (full/native frame rate); a value caps it.
            Timeline.SetDesiredFrameRate(sb, _mode == AnimMode.Reduced ? ReducedFps : (int?)null);
            sb.Begin(control, true);
        }
    }

    private static void StopAll(FrameworkElement control)
    {
        foreach (var key in StoryboardKeys)
            ((Storyboard)control.Resources[key]).Stop(control);
    }
}
