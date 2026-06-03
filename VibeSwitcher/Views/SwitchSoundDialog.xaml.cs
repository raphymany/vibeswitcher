using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Views;

public partial class SwitchSoundDialog : Window
{
    private string _tone;
    private string? _customPath;
    private int _volume;
    private bool _showBanner;

    public SoundOverrideResult? Result { get; private set; }

    public SwitchSoundDialog(bool enabled, string? tone, string? customPath, int volume, bool showBanner = false)
    {
        InitializeComponent();

        _tone       = tone ?? "Click";
        _customPath = customPath;
        _volume     = volume;
        _showBanner = showBanner;

        UpdateToneChips();
        CustomPathBox.Text       = customPath ?? "";
        CustomPathPanel.Visibility = _tone == "Custom" ? Visibility.Visible : Visibility.Collapsed;

        VolumeSliderControl.Value = volume;
        VolumeLabel.Text          = $"{volume}%";

        BannerToggle.IsChecked = showBanner;
    }

    private void ToneChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is string tag)
        {
            _tone = tag;
            UpdateToneChips();
            CustomPathPanel.Visibility = tag == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateToneChips()
    {
        ToneClick.IsChecked  = _tone == "Click";
        ToneChime.IsChecked  = _tone == "Chime";
        ToneBlip.IsChecked   = _tone == "Blip";
        ToneBell.IsChecked   = _tone == "Bell";
        ToneAlert.IsChecked  = _tone == "Alert";
        ToneSoft.IsChecked   = _tone == "Soft";
        TonePing.IsChecked   = _tone == "Ping";
        ToneCustom.IsChecked = _tone == "Custom";
    }

    private void CustomPath_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _customPath = string.IsNullOrWhiteSpace(CustomPathBox.Text) ? null : CustomPathBox.Text.Trim();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Sound File",
            Filter = "WAV Files (*.wav)|*.wav",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;
        _customPath = dlg.FileName;
        CustomPathBox.Text = dlg.FileName;
        _tone = "Custom";
        UpdateToneChips();
        CustomPathPanel.Visibility = Visibility.Visible;
    }

    private void Volume_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        _volume = (int)VolumeSliderControl.Value;
        VolumeLabel.Text = $"{_volume}%";
    }

    private void BannerToggle_Changed(object sender, RoutedEventArgs e)
    {
        _showBanner = BannerToggle.IsChecked == true;
    }

    private void TestSound_Click(object sender, RoutedEventArgs e)
    {
        _ = new SwitchSoundService().TestAsync(_tone, _customPath, _volume);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = new SoundOverrideResult(true, _tone, _customPath, _volume, _showBanner);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        if (e.Key == Key.Enter)  { Save_Click(sender, e); e.Handled = true; }
    }
}
