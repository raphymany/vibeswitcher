using System.Diagnostics;
using System.IO;
using System.Windows;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Views;

// Thin view model for each row in the session log list.
internal record SessionErrorRow(SessionError Error)
{
    public string CodeDisplay => Error.Code.ToCode();
    public string Summary     => $"{Error.Title}: {Error.Message}";
    public DateTime Timestamp => Error.Timestamp;
}

public partial class SessionLogWindow : Window
{
    public SessionLogWindow()
    {
        InitializeComponent();

        ErrorList.ItemsSource = SessionErrorTracker.Errors
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
            AppLogger.Warning("SessionLogWindow.OpenLog", ex.Message);
            SessionErrorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Log File Could Not Be Opened",
                $"Could not open the error log: {ex.Message}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
