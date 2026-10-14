using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.App.Dialogs;

public sealed record SessionErrorRow(SessionError Error)
{
    public string CodeDisplay => Error.Code.ToCode();
    public string Summary => $"{Error.Title}: {Error.Message}";
    public string Time => Error.Timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); // (colons everywhere, as WPF shows)
}

// Port of Views/SessionLogWindow.xaml.cs: this session's errors, and the log file. Unlike the other
// dialogs it can be resized, minimized and maximized. (The log is in the app's data folder — the
// Windows path doesn't apply — so the window is given it.)
public partial class SessionLogWindow : VsDialog
{
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly string _logPath;

    public SessionLogWindow() : this(null!, null!, "") { } // (for the XAML loader)

    public SessionLogWindow(IAppLogger logger, ISessionErrorTracker errorTracker, string logPath)
    {
        AvaloniaXamlLoader.Load(this);
        _logger = logger;
        _errorTracker = errorTracker;
        _logPath = logPath;
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        WindowChrome.EnableEdgeResize(this);

        if (errorTracker != null)
            this.FindControl<ItemsControl>("ErrorList")!.ItemsSource =
                errorTracker.Errors.Select(e => new SessionErrorRow(e)).ToList();
    }

    private async void OpenLog_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // The log file isn't there yet: says so instead (as on Windows).
            if (!File.Exists(_logPath))
            {
                await ShowOwned(new AlertDialog("Log File",
                    "No log file has been written yet.\nPersistent errors will appear here once they occur.",
                    AlertKind.Info));
                return;
            }
            if (!SystemOpener.OpenFile(_logPath, out var error))
            {
                _logger.Warning("SessionLogWindow.OpenLog", error);
                _errorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Log File Could Not Be Opened",
                    $"Could not open the error log: {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("SessionLogWindow.OpenLog", ex.Message);
        }
    }

    private Task ShowOwned(VsDialog dialog) => dialog.ShowDialog(this);

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
