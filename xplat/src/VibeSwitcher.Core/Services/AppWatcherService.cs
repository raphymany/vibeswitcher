using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;

namespace VibeSwitcher.Services;

// FORK of VibeSwitcher/Services/AppWatcherService.cs (tracked in xplat/forks.txt). Same behaviour and
// API: polled every 2 seconds, fires only when a watched app goes from not running to running, and
// apps already running when the list changes don't fire until they're closed and opened again. What
// changed: Windows compares process names; here each trigger is matched against the system's process
// list with AppTriggerMatch (program location, Mac bundle, Flatpak/Snap id, then name), and the state
// is kept per trigger rather than per name — so one launch matched by several triggers still fires
// once (the first in the list), as one program name does on Windows. Also: a process list that
// couldn't be read completely is skipped instead of counting every app in it as closed.
public sealed class AppWatcherService : IDisposable
{
    // Fired on a ThreadPool thread when a watched app goes from not-running to running.
    // Parameter is the trigger exactly as stored in AppTriggers.
    public event Action<string>? ProcessLaunched;

    private volatile IReadOnlyList<string> _watched = [];
    private volatile HashSet<string> _running = new(StringComparer.Ordinal);
    // Triggers that were already running when UpdateWatchList was last called — skipped on the
    // very next poll tick so pressing Done doesn't immediately fire for an already-running app.
    // Not volatile: Interlocked.Exchange provides the required memory barrier.
    private HashSet<string> _skipOnNextPoll = new(StringComparer.Ordinal);
    private readonly Timer _timer;
    private volatile bool _disposed;
    // Re-entrancy gate: a slow poll can run past the period; without this, two concurrent polls
    // race the _running read-modify-write.
    private int _polling;
    private readonly IAppLogger _logger;
    private readonly IRunningProcessSource _processes;

    public AppWatcherService(IAppLogger logger, IRunningProcessSource processes)
        : this(logger, processes, TimeSpan.FromSeconds(2)) { }

    internal AppWatcherService(IAppLogger logger, IRunningProcessSource processes, TimeSpan period)
    {
        _logger = logger;
        _processes = processes;
        _timer = new Timer(Poll, null, period, period);
    }

    public void UpdateWatchList(IReadOnlyList<string> paths)
    {
        // Snapshot which of the newly-watched apps are currently running. These will be absorbed
        // into _running on the first poll (baseline), but will NOT fire ProcessLaunched — only a
        // close-then-reopen will trigger.
        // (If the process list can't be read right now, every app counts as already running: at
        // worst a launch in the next 2 seconds is missed, never a switch for an app that was open.)
        var processes = Snapshot();
        var alreadyRunning = processes == null
            ? new HashSet<string>(paths, StringComparer.Ordinal)
            : new HashSet<string>(MatchesOf(paths, processes).Keys, StringComparer.Ordinal);
        Interlocked.Exchange(ref _skipOnNextPoll, alreadyRunning);
        _watched = paths;
    }

    private void Poll(object? _)
    {
        if (_disposed) return;
        // Skip this tick if the previous one is still running (no overlapping polls).
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            PollCore();
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    // Internal so tests can tick it directly.
    internal void PollCore()
    {
        var watched = _watched;
        if (watched.Count == 0)
        {
            _running = new(StringComparer.Ordinal);
            return;
        }

        // A list that couldn't be read completely says nothing about what closed: skip this tick
        // rather than see the apps missing from it "launch" again on the next one.
        var processes = Snapshot();
        if (processes == null) return;
        var matches = MatchesOf(watched, processes);
        var nowRunning = new HashSet<string>(matches.Keys, StringComparer.Ordinal);

        // Consume the skip-set atomically. Triggers in it were already running at setup time;
        // absorb them into nowRunning (so they count as baseline) but don't fire for them.
        var skip = Interlocked.Exchange(ref _skipOnNextPoll, new HashSet<string>(StringComparer.Ordinal));
        foreach (var t in skip)
            nowRunning.Add(t);

        var prev = _running;
        _running = nowRunning;

        // One launch fires once, as on Windows (which fires per program name): when several watched
        // triggers match the same newly started processes — e.g. one profile lists "/usr/bin/steam",
        // another an imported "steam.exe" — the first in the list wins. The most specific triggers
        // (fewest processes) claim first, so a broad one (everything in a Flatpak's sandbox) can't
        // swallow a specific launch inside it; a trigger only stays quiet when all its processes are
        // already claimed. They fire broadest first, so the most specific launch is the last switch.
        var launched = watched.Distinct().Where(t => matches.ContainsKey(t) && !prev.Contains(t) && !skip.Contains(t));
        var claimed = new HashSet<RunningProcess>(ReferenceEqualityComparer.Instance);
        var fire = new List<string>();
        foreach (var trigger in launched.OrderBy(t => matches[t].Count)) // stable: list order among equals
        {
            var matched = matches[trigger];
            if (matched.All(claimed.Contains)) continue;
            claimed.UnionWith(matched);
            fire.Add(trigger);
        }
        foreach (var trigger in fire.OrderByDescending(t => matches[t].Count))
        {
            _logger.Info("AppWatcherService", $"'{AppTriggerMatch.DisplayName(trigger)}' launched.");
            ProcessLaunched?.Invoke(trigger);
        }
    }

    // Each running trigger → the processes it matches.
    private static Dictionary<string, List<RunningProcess>> MatchesOf(IReadOnlyList<string> triggers, IReadOnlyList<RunningProcess> processes)
    {
        var result = new Dictionary<string, List<RunningProcess>>(StringComparer.Ordinal);
        foreach (var t in triggers)
        {
            if (result.ContainsKey(t)) continue;
            var matched = processes.Where(p => AppTriggerMatch.Matches(t, p)).ToList();
            if (matched.Count > 0) result[t] = matched;
        }
        return result;
    }

    // One process-table snapshot per tick; null if it couldn't be read.
    private IReadOnlyList<RunningProcess>? Snapshot()
    {
        try { return _processes.Snapshot(); }
        catch (Exception ex)
        {
            _logger.Warning("AppWatcherService.Poll", $"Process enumeration failed: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
