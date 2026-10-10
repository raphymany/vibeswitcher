namespace VibeSwitcher.Compat;

// The UI-thread services the shared (linked) Windows code expects from WPF's Dispatcher.
// The Avalonia app implements this over Dispatcher.UIThread and registers it at startup.
public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
    // Starts a repeating UI-thread timer; disposing the result stops it. (The shim also drops a tick
    // that was already queued when it was stopped, so implementations needn't guarantee that.)
    IDisposable StartTimer(TimeSpan interval, Action tick);
}

// Plays a complete in-memory .wav file and returns when playback ends (System.Media.SoundPlayer.PlaySync).
// Like SoundPlayer, throw on data it can't play — the linked caller logs the failure.
public interface IWavPlayer
{
    void PlayWav(byte[] wav);
}

public static class UiThread
{
    // Makes System.Windows.Application.Current (the shim) non-null so linked code marshals onto the
    // real UI thread. Until then — e.g. in unit tests — linked code runs inline, exactly as the
    // Windows tests do when no WPF Application exists.
    public static void Register(IUiDispatcher dispatcher)
    {
        var shim = new System.Windows.Threading.Dispatcher(dispatcher);
        System.Windows.Application.Current = new System.Windows.Application(shim);
        // Timers that services started before this point (e.g. the scheduler) start ticking now.
        System.Windows.Threading.DispatcherTimer.BindPending(shim);
    }

    // Shutdown / test isolation: forgets timers waiting for a dispatcher and any queued command
    // refresh. Timers already running on the old dispatcher stay attached to it (they die with it).
    public static void Unregister()
    {
        System.Windows.Application.Current = null;
        System.Windows.Threading.DispatcherTimer.ForgetPending();
        System.Windows.Input.CommandManager.ResetQueue();
    }
}

public static class WavPlayback
{
    // Null = sounds are silently skipped (unit tests, or before the platform layer starts).
    public static IWavPlayer? Player { get; set; }
}
