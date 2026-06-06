using System.Diagnostics;
using System.IO;
using System.Windows;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Views;

internal record SessionErrorRow(SessionError Error)
{
    public string CodeDisplay => Error.Code.ToCode();
    public string Summary     => $"{Error.Title}: {Error.Message}";
    public DateTime Timestamp => Error.Timestamp;
}

public partial class SessionLogWindow : Window
{
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;

    public SessionLogWindow(IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        InitializeComponent();
        _logger = logger;
        _errorTracker = errorTracker;

        ErrorList.ItemsSource = errorTracker.Errors
            .Select(e => new SessionErrorRow(e))
            .ToList();
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(AppLogger.LogPath))
        {
            var dlg = new AlertDialog("Log File",
                "No log file has been written yet.\nPersistent errors will appear here once they occur.",
                AlertKind.Info) { Owner = this };
            dlg.ShowDialog();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(AppLogger.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warning("SessionLogWindow.OpenLog", ex.Message);
            _errorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Log File Could Not Be Opened",
                $"Could not open the error log: {ex.Message}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
