// Minimal stand-ins for the handful of WPF / System.Media APIs that the linked Windows files call
// by name, so those files compile here unchanged. Deliberately tiny and internal: essentially only
// what the linked code uses exists, so if a Windows file starts using another WPF API the xplat
// build fails — drift shows up as a compile error instead of a silent behaviour difference.

using VibeSwitcher.Compat;

namespace System.Windows
{
    internal sealed class Application
    {
        private static volatile Application? _current;

        internal Application(Threading.Dispatcher dispatcher) => Dispatcher = dispatcher;

        public static Application? Current
        {
            get => _current;
            internal set => _current = value;
        }

        public Threading.Dispatcher Dispatcher { get; }
    }
}

namespace System.Windows.Threading
{
    internal sealed class Dispatcher
    {
        private readonly IUiDispatcher _ui;

        internal Dispatcher(IUiDispatcher ui) => _ui = ui;

        public bool CheckAccess() => _ui.CheckAccess();

        // Like WPF, an exception thrown by the callback is captured in the returned task.
        public Task InvokeAsync(Action callback)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ui.Post(() =>
            {
                try { callback(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> callback)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ui.Post(() =>
            {
                try { tcs.SetResult(callback()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        // Like WPF, the original exception surfaces (not a reflection TargetInvocationException).
        public void BeginInvoke(Delegate method, params object?[] args) =>
            _ui.Post(() =>
            {
                if (method is Action action && (args == null || args.Length == 0)) { action(); return; }
                try { method.DynamicInvoke(args); }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }
            });

        internal IDisposable StartTimer(TimeSpan interval, Action tick) => _ui.StartTimer(interval, tick);
    }

    internal sealed class DispatcherTimer
    {
        // Timers started before the UI dispatcher was registered; bound as soon as it is (a WPF timer
        // binds to its thread's dispatcher, so it can't miss its start like this).
        private static readonly List<DispatcherTimer> Unbound = new();
        private static readonly object UnboundGate = new();

        private IDisposable? _running;
        private bool _started;
        private int _generation;

        public TimeSpan Interval { get; set; }

        public event EventHandler? Tick;

        public bool IsEnabled => _started;

        // Without a registered UI dispatcher (unit tests) the timer is "started" but never ticks —
        // the same as a WPF DispatcherTimer on a thread whose dispatcher isn't pumping — until
        // UiThread.Register binds it.
        // Like WPF, Start/Stop belong on the UI thread (all linked callers use them there).
        public void Start()
        {
            if (_started) return;
            _started = true;
            Dispatcher? dispatcher;
            // Read under the same lock BindPending takes, so a Start racing UiThread.Register either
            // sees the dispatcher or is queued before BindPending empties the list — never lost.
            lock (UnboundGate)
            {
                dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) { Unbound.Add(this); return; }
            }
            Bind(dispatcher);
        }

        public void Stop()
        {
            _started = false;
            _generation++;
            _running?.Dispose();
            _running = null;
            lock (UnboundGate) Unbound.Remove(this);
        }

        internal static void ForgetPending()
        {
            lock (UnboundGate) Unbound.Clear();
        }

        internal static void BindPending(Dispatcher dispatcher)
        {
            List<DispatcherTimer> pending;
            lock (UnboundGate) { pending = new(Unbound); Unbound.Clear(); }
            foreach (var timer in pending) if (timer._started && timer._running == null) timer.Bind(dispatcher);
        }

        private void Bind(Dispatcher dispatcher)
        {
            // A tick already queued when Stop() ran must not fire (WPF guarantees this) — it would
            // act on whatever timer the caller has created since.
            int generation = _generation;
            _running = dispatcher.StartTimer(Interval, () =>
            {
                if (_started && generation == _generation) Tick?.Invoke(this, EventArgs.Empty);
            });
        }
    }
}

namespace System.Windows.Input
{
    internal static class CommandManager
    {
        // WPF holds these weakly and also re-raises on every input event. Avalonia controls
        // unsubscribe from ICommand.CanExecuteChanged when they detach, so strong references are
        // safe for UI subscribers (long-lived non-UI subscribers must unsubscribe themselves); the
        // app layer calls InvalidateRequerySuggested where WPF would requery on input.
        private static readonly object Gate = new();
        private static EventHandler? _requerySuggested;
        private static int _queued;

        public static event EventHandler? RequerySuggested
        {
            add { lock (Gate) _requerySuggested += value; }
            remove { lock (Gate) _requerySuggested -= value; }
        }

        // Like WPF: queued (never re-entrant), and any number of calls before it runs collapse into
        // one refresh. Without a UI dispatcher (unit tests) it raises immediately.
        public static void InvalidateRequerySuggested()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) { Raise(); return; }
            if (Interlocked.Exchange(ref _queued, 1) == 1) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                Volatile.Write(ref _queued, 0);
                Raise();
            }));
        }

        private static void Raise()
        {
            EventHandler? handlers;
            lock (Gate) handlers = _requerySuggested;
            handlers?.Invoke(null, EventArgs.Empty);
        }

        // A refresh queued on a dispatcher that's going away will never run; without this reset every
        // later invalidation would be swallowed.
        internal static void ResetQueue() => Volatile.Write(ref _queued, 0);
    }
}

namespace System.Media
{
    internal sealed class SoundPlayer : IDisposable
    {
        private readonly IO.Stream _stream;

        public SoundPlayer(IO.Stream stream) => _stream = stream;

        public void PlaySync()
        {
            var player = WavPlayback.Player;
            if (player == null) return;
            if (_stream is IO.MemoryStream ms && ms.Position == 0) { player.PlayWav(ms.ToArray()); return; }
            using var buffer = new IO.MemoryStream();
            _stream.CopyTo(buffer);
            player.PlayWav(buffer.ToArray());
        }

        public void Dispose() { }
    }
}
