using VibeSwitcher.Helpers;
using VibeSwitcher.Platform.Linux.Processes;

namespace VibeSwitcher.Platform.Linux.Audio;

// Runs `pactl subscribe` in the background and raises Changed whenever something that can affect the
// device list happens (a sink/source/card appears, disappears or changes, or the server's defaults
// change). Restarts pactl with a growing delay if it exits — e.g. the sound server restarted, or
// hasn't started yet at login — and raises Changed after each restart, since devices may have
// changed while nobody was listening. Callers debounce and diff; this only says "look again".
internal sealed class PulseEventWatcher : IDisposable
{
    private static readonly HashSet<string> Relevant = ["sink", "source", "card", "server"];
    private readonly IProcessRunner _runner;
    private readonly IAppLogger _logger;
    private readonly Func<int, TimeSpan> _backoff;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private IRunningProcess? _current;
    private Task? _loop;

    public event Action? Changed;
    // Raised (with the reason) once listening has failed FailuresBeforeReport times in a row, and
    // again only after a successful run in between — so a missing pactl is reported once, not every
    // retry, and an ordinary sound-server restart (one failed attempt, then fine) isn't reported at all.
    public event Action<string>? ListeningFailed;
    internal const int FailuresBeforeReport = 3;
    private bool _disposed;

    internal PulseEventWatcher(IProcessRunner runner, IAppLogger logger, Func<int, TimeSpan>? backoff = null)
    {
        _runner = runner;
        _logger = logger;
        _backoff = backoff ?? (attempt => TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5))));
    }

    internal void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _loop ??= Task.Run(() => RunAsync(_cts.Token));
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int failures = 0;      // drives the retry delay
        int consecutive = 0;   // failed attempts since the last one that heard from the server
        bool reported = false;
        bool first = true;
        while (!ct.IsCancellationRequested)
        {
            bool gotEvents = false;
            string? error = null;
            try
            {
                var p = _runner.Start(PactlClient.Tool, ["subscribe"]);
                lock (_gate)
                {
                    if (ct.IsCancellationRequested) { p.Dispose(); return; }
                    _current = p;
                }
                using (p)
                {
                    if (!first) Raise(); // anything may have changed while we weren't listening
                    first = false;
                    using var reader = new StreamReader(p.StandardOutput);
                    while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                    {
                        if (PactlParser.ParseSubscribeEvent(line) is { } ev && Relevant.Contains(ev.Facility))
                        {
                            gotEvents = true;
                            Raise();
                        }
                        else if (line.Length > 0) gotEvents = true; // connected and talking
                    }
                    if (ct.IsCancellationRequested) return;
                    p.WaitForExit(TimeSpan.FromSeconds(2));
                    error = p.StandardError.Trim() is { Length: > 0 } e ? e : $"pactl subscribe exited ({SafeExit(p)})";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (ToolNotFoundException) { error = "the 'pactl' tool isn't installed"; }
            catch (Exception ex) when (!ct.IsCancellationRequested) { error = ex.Message; }
            finally { lock (_gate) _current = null; }

            if (gotEvents) { failures = 0; reported = false; }
            if (error != null) consecutive = gotEvents ? 1 : consecutive + 1;
            if (error != null && !reported && consecutive >= FailuresBeforeReport)
            {
                reported = true;
                _logger.Warning("PulseEventWatcher", $"Not receiving device change events: {error}. Retrying in the background.");
                try { ListeningFailed?.Invoke(error); } catch (Exception ex) { _logger.Warning("PulseEventWatcher", ex.Message); }
            }
            try { await Task.Delay(_backoff(failures++), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static string SafeExit(IRunningProcess p)
    {
        try { return $"code {p.ExitCode}"; } catch (InvalidOperationException) { return "still running"; }
    }

    private void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { _logger.Warning("PulseEventWatcher", ex.Message); }
    }

    public void Dispose()
    {
        Task? loop;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _current?.Kill();
            loop = _loop;
        }
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
