using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
        // The real start-up (tray/menu-bar icon, services, Settings) comes with the app wiring; for
        // now the app shows its dialog preview so the look can be checked by hand.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Dialogs.DialogPreviewWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
