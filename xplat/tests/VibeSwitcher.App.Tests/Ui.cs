using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VibeSwitcher.App.Tests;

// Driving the UI in tests: clicks, answering dialogs, waiting for the app to get somewhere.
internal static class Ui
{
    public static T Named<T>(Window w, string name) where T : Control => w.FindControl<T>(name)!;

    // What a click does: the Click handlers, then the button's command (Avalonia runs the command
    // itself, not through the routed event).
    public static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (b.Command is { } command && command.CanExecute(b.CommandParameter)) command.Execute(b.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    public static void Click(Window dialog, string button) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == button)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Clicks a button in the window's dialog once a dialog with that button is up (while the caller
    // waits on it, or before the next step that opens it). Gives up after a few seconds.
    public static void ClickLater(Window owner, string button, Action<Window>? check = null) =>
        ClickLater(owner, button, check, DateTime.UtcNow.AddSeconds(5));

    public static void ClickLater(Window owner, string button, Action<Window>? check, DateTime giveUp) =>
        Dispatcher.UIThread.Post(() =>
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(d => d.IsVisible &&
                d.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == button));
            if (dialog == null)
            {
                if (DateTime.UtcNow < giveUp) ClickLater(owner, button, check, giveUp);
                return;
            }
            check?.Invoke(dialog);
            Click(dialog, button);
        }, DispatcherPriority.Background);

    // Lets the app run (dialogs open and close, awaited work continues) until the condition holds,
    // or a few seconds have passed.
    public static async Task Until(Func<bool> done)
    {
        var giveUp = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < giveUp) { Dispatcher.UIThread.RunJobs(); await Task.Delay(5); }
        Dispatcher.UIThread.RunJobs();
    }

    // A temporary folder the config service may still be writing to (its saves finish in the
    // background; Windows won't delete an open file): tried for a moment, then left to the system.
    public static async Task DeleteSoon(string dir)
    {
        for (int i = 0; i < 20; i++)
        {
            try { Directory.Delete(dir, recursive: true); return; }
            catch (IOException) { await Task.Delay(50); }
            catch (UnauthorizedAccessException) { await Task.Delay(50); }
        }
    }

    public static IEnumerable<string?> Texts(Control c) =>
        c.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text);
}
