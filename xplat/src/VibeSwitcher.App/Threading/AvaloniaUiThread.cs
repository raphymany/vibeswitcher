using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VibeSwitcher.Compat;

namespace VibeSwitcher.App.Threading;

// The UI thread the shared (Windows) code runs its work on: Avalonia's, behind the stand-ins for
// WPF's Dispatcher and DispatcherTimer. Registered at start-up, before any view model exists
// (without it the view models skip their UI-thread work — e.g. the device lists stay empty).
public sealed class AvaloniaUiThread : IUiDispatcher
{
    public static void Install() => UiThread.Register(new AvaloniaUiThread());

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(interval, DispatcherPriority.Normal, (_, _) => tick());
        timer.Start();
        return new Stop(timer);
    }

    private sealed class Stop(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    // WPF re-checks every command's CanExecute after each mouse or keyboard input (the shared
    // RelayCommands rely on it to enable/disable their buttons); this does the same for a window.
    public static void RequeryCommandsOnInput(Window window)
    {
        void Requery(object? sender, RoutedEventArgs e) => System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        window.AddHandler(InputElement.PointerReleasedEvent, Requery, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, Requery, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
