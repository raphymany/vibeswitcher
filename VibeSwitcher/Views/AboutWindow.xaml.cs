using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using VibeSwitcher.Helpers;


namespace VibeSwitcher.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var infoVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0]; // strip git commit hash suffix appended by MSBuild
        VersionText.Text = !string.IsNullOrEmpty(infoVer)
            ? $"Version {infoVer}"
            : $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

        try
        {
            var icon = IconHelper.GetDefaultIcon();
            AppIconImage.Source = IconHelper.ToImageSource(icon);
        }
        catch { }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warning("AboutWindow.Hyperlink", ex.Message);
            SessionErrorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Link Could Not Be Opened",
                $"Could not open link: {ex.Message}");
        }
        e.Handled = true;
    }

    private void ViewIssues_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/raphymany/vibeswitcher/issues") { UseShellExecute = true }); }
        catch (Exception ex)
        {
            AppLogger.Warning("AboutWindow", ex.Message);
            SessionErrorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Link Could Not Be Opened",
                $"Could not open GitHub Issues: {ex.Message}");
        }
    }

    private void SubmitIssue_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/raphymany/vibeswitcher/issues/new") { UseShellExecute = true }); }
        catch (Exception ex)
        {
            AppLogger.Warning("AboutWindow", ex.Message);
            SessionErrorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Link Could Not Be Opened",
                $"Could not open GitHub Issues form: {ex.Message}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
