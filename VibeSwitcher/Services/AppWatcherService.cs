using System.Diagnostics;
using System.IO;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Services;

public sealed class AppWatcherService : IDisposable
{
    // Fired on a ThreadPool thread when a watched exe goes from not-running to running.
    // Parameter is the full exe path as stored in AppTriggers.
    public event Action<string>? ProcessLaunched;

    private volatile IReadOnlyList<string> _watchedPaths = [];
    private volatile HashSet<string> _runningExeNames = new(StringComparer.OrdinalIgnoreCase);
    // Exe names that were already running when UpdateWatchList was last called — skipped on the
    // very next poll tick so pressing Done doesn't immediately fire for an already-running app.
    // Not volatile: Interlocked.Exchange provides the required memory barrier.
    private HashSet<string> _skipOnNextPoll = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private volatile bool _disposed;
    private readonly IAppLogger _logger;

    public AppWatcherService(IAppLogger logger)
    {
        _logger = logger;
        _timer = new Timer(Poll, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void UpdateWatchList(IReadOnlyList<string> paths)
    {
        // Snapshot which of the newly-watched executables are currently running.
        // These will be absorbed into _runningExeNames on the first poll (baseline),
        // but will NOT fire ProcessLaunched — only a close-then-reopen will trigger.
        var alreadyRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var exeName = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (Process.GetProcessesByName(exeName).Length > 0)
                    alreadyRunning.Add(exeName);
            }
            catch { }
        }

        Interlocked.Exchange(ref _skipOnNextPoll, alreadyRunning);
        _watchedPaths = paths;
    }

    private void Poll(object? _)
    {
        if (_disposed) return;

        var paths = _watchedPaths;
        if (paths.Count == 0)
        {
            _runningExeNames = new(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var nowRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var exeName = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (Process.GetProcessesByName(exeName).Length > 0)
                    nowRunning.Add(exeName);
            }
            catch (Exception ex)
            {
                _logger.Warning("AppWatcherService.Poll", $"Process check failed for '{exeName}': {ex.Message}");
            }
        }

        // Consume the skip-set atomically. Any names in it were already running at setup time;
        // absorb them into nowRunning (so they count as baseline) but don't fire for them.
        var skip = Interlocked.Exchange(ref _skipOnNextPoll,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var name in skip)
            nowRunning.Add(name);

        var prev = _runningExeNames;
        _runningExeNames = nowRunning;

        foreach (var exeName in nowRunning)
        {
            if (prev.Contains(exeName)) continue;
            if (skip.Contains(exeName)) continue; // already running at trigger-setup time

            var matchedPath = paths.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), exeName, StringComparison.OrdinalIgnoreCase));

            if (matchedPath != null)
            {
                _logger.Info("AppWatcherService", $"'{exeName}' launched.");
                ProcessLaunched?.Invoke(matchedPath);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
