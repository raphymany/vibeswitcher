using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/MicTestDialog.xaml.cs: the device's live input level and its peak for five seconds,
// then it closes itself. The level comes from the system's audio backend (Windows: AudioMicMonitor).
public partial class MicTestDialog : VsDialog
{
    private readonly string _deviceId;
    private readonly IPlatformAudioService? _audio;
    private readonly IAppLogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private float _peakLevel;

    public MicTestDialog() : this("", "", null, null) { } // (for the XAML loader)

    // (audio: null when the system's backend isn't there — the dialog then says it can't open the
    // microphone.)
    public MicTestDialog(string deviceId, string deviceName, IPlatformAudioService? audio, IAppLogger? logger)
    {
        AvaloniaXamlLoader.Load(this);
        _deviceId = deviceId;
        _audio = audio;
        _logger = logger;
        Named<TextBlock>("DeviceNameText").Text = deviceName;
        Opened += (_, _) =>
        {
            _ = RunCaptureAsync(_cts.Token);
            _ = RunCountdownAsync(_cts.Token);
        };
    }

    private T Named<T>(string name) where T : Control => this.FindControl<T>(name)!;

    // How long the countdown waits each second (tests shorten it).
    internal TimeSpan Second { get; set; } = TimeSpan.FromSeconds(1);

    private async Task RunCaptureAsync(CancellationToken ct)
    {
        bool failed = false;
        await Task.Run(() =>
        {
            try
            {
                if (_audio == null) { failed = true; return; }
                _audio.RunMicLevelMonitor(_deviceId, ct, level =>
                    Dispatcher.UIThread.Post(() => ShowLevel(level)));
                // The backend returns early only when the device couldn't be opened.
                failed = !ct.IsCancellationRequested;
            }
            catch (Exception ex)
            {
                _logger?.Debug("MicTestDialog.RunCaptureAsync", ex.Message);
                failed = true;
            }
        }, CancellationToken.None);

        if (failed && !ct.IsCancellationRequested) ShowCaptureError();
    }

    internal void ShowLevel(float level)
    {
        float scaled = Math.Min(level * 10f, 1f);
        Named<ProgressBar>("LevelBar").Value = scaled;
        Named<TextBlock>("LevelText").Text = $"{(int)(scaled * 100)}%";
        _peakLevel = Math.Max(_peakLevel, scaled);
        Named<TextBlock>("PeakText").Text = $"Peak: {(int)(_peakLevel * 100)}%";
    }

    private void ShowCaptureError()
    {
        Named<TextBlock>("SpeakPrompt").IsVisible = false;
        Named<TextBlock>("CaptureError").IsVisible = true;
        Named<ProgressBar>("LevelBar").IsVisible = false;
        Named<TextBlock>("CountdownText").Text = string.Empty;
        _cts.Cancel(); // (and the dialog stays open, as on Windows)
    }

    private async Task RunCountdownAsync(CancellationToken ct)
    {
        for (int sec = 5; sec > 0; sec--)
        {
            if (ct.IsCancellationRequested) return;
            Named<TextBlock>("CountdownText").Text = $"Closing in {sec} s";
            try { await Task.Delay(Second, ct); }
            catch (OperationCanceledException) { return; }
        }
        if (!ct.IsCancellationRequested) Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }
}
