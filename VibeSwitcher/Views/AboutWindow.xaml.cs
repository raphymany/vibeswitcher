using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using VibeSwitcher.Helpers;


namespace VibeSwitcher.Views;

public partial class AboutWindow : Window
{
    private readonly string _version;
    private readonly int _profileCount;

    public AboutWindow(int profileCount = 0)
    {
        InitializeComponent();
        _profileCount = profileCount;

        var infoVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0]; // strip git commit hash suffix appended by MSBuild
        _version = !string.IsNullOrEmpty(infoVer)
            ? infoVer
            : Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {_version}";

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

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var errors = SessionErrorTracker.Errors;
            var info = new System.Text.StringBuilder();
            info.AppendLine("=== VibeSwitcher Diagnostic Info ===");
            info.AppendLine($"Version:        {_version}");
            info.AppendLine($"OS:             {Environment.OSVersion}");
            info.AppendLine($"Profiles:       {_profileCount}");
            info.AppendLine($"Session errors: {errors.Count}");
            info.AppendLine($"Log file:       {AppLogger.LogPath}");
            if (errors.Count > 0)
            {
                info.AppendLine();
                info.AppendLine("--- Session errors ---");
                foreach (var err in errors)
                    info.AppendLine($"  [{err.Timestamp:HH:mm:ss}] {err.Code.ToCode()} {err.Title}");
            }
            Clipboard.SetText(info.ToString());

            var btn = (System.Windows.Controls.Button)sender;
            var original = btn.Content;
            btn.Content = "Copied!";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { btn.Content = original; timer.Stop(); };
            timer.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Warning("AboutWindow.CopyDiagnostics", ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
