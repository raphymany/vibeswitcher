using System.Diagnostics;
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
        try
        {
            Process.Start(new ProcessStartInfo(AppLogger.LogPath) { UseShellExecute = true });
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
