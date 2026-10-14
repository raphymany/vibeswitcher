using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.App.Theming;

namespace VibeSwitcher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        WpfResources.Install(Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Threading.AvaloniaUiThread.Install();
        Imaging.AppImages.Install();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (AppHost.Current is { } host)
            {
                // The running app (Program has made it the one copy): no main window — the tray /
                // menu-bar icon and Settings — until Quit, which disposes it.
                host.InstallCrashLogging();
                desktop.Exit += (_, _) => host.Dispose();
                // Logging out, `kill` or a service manager end the app the way Quit does (pending
                // settings saved), not abruptly.
                _sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, context =>
                {
                    context.Cancel = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
                });
                // macOS: opening the app again from Finder or Launchpad while it runs ("reopen") shows
                // Settings — what a second launch does on Linux and Windows.
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                    activatable.Activated += (_, e) =>
                    {
                        if (e.Kind == ActivationKind.Reopen) host.Reopen();
                    };
                _ = StartAsync(host, desktop);
            }
            else
            {
                // (Windows, for development: the dialog preview.)
                desktop.MainWindow = new Dialogs.DialogPreviewWindow();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private IDisposable? _sigterm;

    // A start-up that fails is logged, explained in a dialog (an app started from the desktop has no
    // terminal to read), and the app quits rather than running with no icon to reach it.
    private static async Task StartAsync(AppHost host, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await host.StartAsync();
        }
        catch (Exception ex)
        {
            host.Logger?.Error("App.Start", ex);
            Console.Error.WriteLine($"VibeSwitcher couldn't start: {ex.Message}");
            try
            {
                await new Dialogs.DialogService(() => null).ShowAlertAsync("VibeSwitcher Couldn't Start",
                    $"Something went wrong while starting: {ex.Message}\n\nThe details are in the log in VibeSwitcher's settings folder.");
            }
            catch (Exception) { /* nothing more to show */ }
            desktop.Shutdown(1);
        }
    }
}
