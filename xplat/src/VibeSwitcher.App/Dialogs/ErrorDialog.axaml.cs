using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/ErrorDialog.xaml.cs. The log's location is passed in (it differs per system).
public partial class ErrorDialog : VsDialog
{
    private readonly IAppLogger? _logger;
    private readonly ISessionErrorTracker? _errorTracker;
    private readonly string _logPath = "";

    public ErrorDialog() { AvaloniaXamlLoader.Load(this); } // (for the XAML loader)

    public ErrorDialog(ErrorCode code, string title, string message, string logPath, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        AvaloniaXamlLoader.Load(this);
        _logger = logger;
        _errorTracker = errorTracker;
        _logPath = logPath;
        Title = title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("CodeText")!.Text = code.ToCode();
        this.FindControl<SelectableTextBlock>("MessageText")!.Text = message;
    }

    private void OK_Click(object? sender, RoutedEventArgs e) => CloseWith(true);

    private async void OpenLog_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!await Launcher.LaunchFileInfoAsync(new FileInfo(_logPath)))
                throw new InvalidOperationException("no app opened it");
        }
        catch (Exception ex)
        {
            _logger?.Warning("ErrorDialog.OpenLog", ex.Message);
            _errorTracker?.Record(ErrorCode.HyperlinkOpenFailed, "Log File Could Not Be Opened",
                $"Could not open the error log: {ex.Message}");
        }
    }
}
