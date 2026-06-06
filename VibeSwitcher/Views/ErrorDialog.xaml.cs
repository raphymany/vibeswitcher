using System.Diagnostics;
using System.Windows;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Views;

public partial class ErrorDialog : Window
{
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;

    public ErrorDialog(ErrorCode code, string title, string message, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        InitializeComponent();
        _logger = logger;
        _errorTracker = errorTracker;
        Title = title;
        TitleText.Text = title;
        CodeText.Text = code.ToCode();
        MessageText.Text = message;
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppLogger.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warning("ErrorDialog.OpenLog", ex.Message);
            _errorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Log File Could Not Be Opened",
                $"Could not open the error log: {ex.Message}");
        }
    }
}
