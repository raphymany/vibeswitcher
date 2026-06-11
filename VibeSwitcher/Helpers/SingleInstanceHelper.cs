namespace VibeSwitcher.Helpers;

public class SingleInstanceHelper : IDisposable
{
    // Local\ prefix scopes to the current session (Fast User Switching / RDP safe).
    private static readonly string MutexName =
        $"Local\\VibeSwitcher_SingleInstance_v1_{Environment.UserName}";
    private static readonly string EventName =
        $"Local\\VibeSwitcher_Activate_v1_{Environment.UserName}";
    // Fixed name (no username) referenced by the installer's AppMutex directive so
    // setup/uninstall can detect a running instance. Never used for single-instancing.
    private const string InstallerDetectMutexName = "Local\\VibeSwitcher_App";

    private Mutex?           _mutex;
    private Mutex?           _installerDetectMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _listenerCts;
    private bool _owned;

    /// <summary>
    /// Returns true when this is the first instance.
    /// Passes an optional callback that is invoked on the UI thread whenever a
    /// subsequent launch (e.g. the pinned taskbar button) signals this instance.
    /// Returns false when another instance is already running; the caller should
    /// call Shutdown() and return immediately.
    /// </summary>
    public bool TryAcquire(Action? onActivationRequested = null)
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out _owned);

        if (!_owned)
        {
            // Signal the first instance to show its window, then let the caller exit.
            if (EventWaitHandle.TryOpenExisting(EventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            return false;
        }

        // First instance: create the named event and start listening for signals.
        // The detect mutex is best-effort — if the name is somehow taken by another
        // kernel object type, the app must still start (installer detection degrades).
        try { _installerDetectMutex = new Mutex(initiallyOwned: false, name: InstallerDetectMutexName); }
        catch (WaitHandleCannotBeOpenedException) { }
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (onActivationRequested != null)
            StartListener(onActivationRequested);

        return true;
    }

    private void StartListener(Action onActivated)
    {
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        var evt   = _activationEvent!;

        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                // 500 ms timeout so the loop can notice cancellation promptly.
                if (evt.WaitOne(500) && !token.IsCancellationRequested)
                    onActivated();
            }
        });
    }

    public void Dispose()
    {
        _listenerCts?.Cancel();
        _activationEvent?.Set(); // unblock WaitOne so the listener thread exits promptly
        _listenerCts?.Dispose();
        _activationEvent?.Dispose();

        if (_owned && _mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        _mutex = null;
        _installerDetectMutex?.Dispose();
        _installerDetectMutex = null;
    }
}
