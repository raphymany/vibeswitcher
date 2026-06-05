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
    private readonly Timer _timer;
    private volatile bool _disposed;

    public AppWatcherService()
    {
        _timer = new Timer(Poll, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void UpdateWatchList(IReadOnlyList<string> paths)
    {
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
                AppLogger.Warning("AppWatcherService.Poll", $"Process check failed for '{exeName}': {ex.Message}");
            }
        }

        var prev = _runningExeNames;
        _runningExeNames = nowRunning;

        foreach (var exeName in nowRunning)
        {
            if (prev.Contains(exeName)) continue;

            var matchedPath = paths.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), exeName, StringComparison.OrdinalIgnoreCase));

            if (matchedPath != null)
            {
                AppLogger.Info("AppWatcherService", $"'{exeName}' launched.");
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
