using System.Diagnostics;

namespace VibeSwitcher.Platform;

// Notices the computer waking from sleep — the counterpart of Windows' PowerModeChanged(Resume),
// which the app uses to re-check schedules and re-apply the active profile.
//
// Works the same on macOS and Linux without any system API: the monotonic clock (Stopwatch —
// CLOCK_UPTIME_RAW on macOS, CLOCK_MONOTONIC on Linux) does not advance while the machine sleeps,
// but the wall clock does. A check every few seconds that finds the wall clock jumped well past the
// monotonic one means the machine was asleep.
// Side effects worth knowing: macOS App Nap can delay the check for a background app, so the
// reaction may come a little after waking; and a machine without a clock battery whose time is set
// by the network just after login sees a forward jump that looks like a wake — harmless, since all
// a "resume" does is re-check schedules and re-apply the active profile.
public sealed class ResumeDetector : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _threshold;
    private readonly Func<DateTime> _wall;
    private readonly Func<TimeSpan> _mono;
    private readonly object _gate = new();
    private DateTime _lastWall;
    private TimeSpan _lastMono;
    private Timer? _timer;

    // Raised (on a timer thread) with roughly how long the machine slept.
    public event Action<TimeSpan>? Resumed;

    public ResumeDetector() : this(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), () => DateTime.UtcNow, MonotonicNow, startTimer: true) { }

    internal ResumeDetector(TimeSpan interval, TimeSpan threshold, Func<DateTime> wall, Func<TimeSpan> mono, bool startTimer)
    {
        _interval = interval;
        _threshold = threshold;
        _wall = wall;
        _mono = mono;
        _lastWall = wall();
        _lastMono = mono();
        if (startTimer) _timer = new Timer(_ => Check(), null, interval, interval);
    }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static TimeSpan MonotonicNow() => Clock.Elapsed;

    // One check (the timer calls this; tests call it directly).
    internal void Check()
    {
        TimeSpan slept;
        lock (_gate)
        {
            var wall = _wall();
            var mono = _mono();
            slept = (wall - _lastWall) - (mono - _lastMono);
            _lastWall = wall;
            _lastMono = mono;
        }
        // A user changing the clock by hand can look similar; requiring a gap well beyond the timer
        // interval keeps NTP nudges and slow ticks from counting, and a backwards change never does.
        if (slept > _threshold && Resumed is { } handlers)
        {
            // Each subscriber on its own, so one that throws can't keep the others from hearing it.
            foreach (Action<TimeSpan> h in handlers.GetInvocationList())
            {
                try { h(slept); }
                catch (Exception) { /* subscribers log their own errors */ }
            }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
