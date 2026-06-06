using System.Windows;
using VibeSwitcher.Helpers;
using VibeSwitcher.Services;

namespace VibeSwitcher.Views;

public partial class MicTestDialog : Window
{
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private float _peakLevel;

    public MicTestDialog(string deviceId, string deviceName, IAppLogger logger)
    {
        InitializeComponent();
        _deviceId = deviceId;
        _deviceName = deviceName;
        _logger = logger;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DeviceNameText.Text = _deviceName;

        _ = RunCaptureAsync(_cts.Token);
        _ = RunCountdownAsync(_cts.Token);
    }

    private async Task RunCaptureAsync(CancellationToken ct)
    {
        await Task.Run(async () =>
        {
            try
            {
                AudioMicMonitor.RunMicLevelMonitor(_deviceId, ct, level =>
                {
                    float scaled = Math.Min(level * 10f, 1f);
                    Dispatcher.InvokeAsync(() =>
                    {
                        LevelBar.Value = scaled;
                        LevelText.Text = $"{(int)(scaled * 100)}%";
                        _peakLevel = Math.Max(_peakLevel, scaled);
                        PeakText.Text = $"Peak: {(int)(_peakLevel * 100)}%";
                    });
                }, _logger);
            }
            catch
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    SpeakPrompt.Visibility = System.Windows.Visibility.Collapsed;
                    CaptureError.Visibility = System.Windows.Visibility.Visible;
                    LevelBar.Visibility = System.Windows.Visibility.Collapsed;
                    CountdownText.Text = string.Empty;
                });
                _cts.Cancel();
            }
        }, CancellationToken.None);
    }

    private async Task RunCountdownAsync(CancellationToken ct)
    {
        for (int sec = 5; sec > 0; sec--)
        {
            if (ct.IsCancellationRequested) return;
            int s = sec;
            await Dispatcher.InvokeAsync(() => CountdownText.Text = $"Closing in {s} s");
            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { return; }
        }
        if (!ct.IsCancellationRequested)
            await Dispatcher.InvokeAsync(Close);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
