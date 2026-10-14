using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/SwitchSoundDialog.xaml.cs: a built-in tone or a custom .wav (with the user's earlier
// uploads), the volume, the banner, a test play. SoundResult (Windows: Result — here that's the
// dialog's yes/no answer) has Enabled=false when Remove was clicked.
public partial class SwitchSoundDialog : VsDialog
{
    private string _tone;
    private string? _customPath;
    private int _volume;
    private bool _showBanner;
    private readonly IAppLogger _logger;
    private readonly string _libraryDir;

    public ObservableCollection<LibrarySoundItem> LibraryItems { get; } = new();

    public SoundOverrideResult? SoundResult { get; private set; }

    public SwitchSoundDialog() : this(false, null, null, 50, null, "") { } // (for the XAML loader)

    public SwitchSoundDialog(bool enabled, string? tone, string? customPath, int volume, IAppLogger? logger, string soundsLibraryDir,
        bool showBanner = false, bool isEditing = false)
    {
        AvaloniaXamlLoader.Load(this);

        _tone = tone ?? "Click";
        _customPath = customPath;
        _volume = volume;
        _showBanner = showBanner;
        _logger = logger ?? new QuietLogger();
        _libraryDir = soundsLibraryDir;
        PickWav = PickWavAsync;
        TestPlay = (t, path, v) => new SwitchSoundService(_logger).TestAsync(t, path, v);

        UpdateToneChips();
        Named<TextBox>("CustomPathBox").Text = customPath ?? "";
        Named<StackPanel>("CustomPathPanel").IsVisible = _tone == "Custom";

        Named<Slider>("VolumeSliderControl").Value = volume;
        Named<TextBlock>("VolumeLabel").Text = $"{volume}%";

        Named<CheckBox>("BannerToggle").IsChecked = !showBanner; // the toggle means "suppress banner"

        if (isEditing) Named<Button>("RemoveBtn").IsVisible = true;

        Named<ItemsControl>("LibraryList").ItemsSource = LibraryItems;
        LoadLibrary();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseWith(false); e.Handled = true; }
            if (e.Key == Key.Enter) { Save(); e.Handled = true; }
        };
    }

    private T Named<T>(string name) where T : Control => this.FindControl<T>(name)!;

    // The .wav picker and the test play (tests stand in for them).
    internal Func<Task<string?>> PickWav { get; set; }
    internal Func<string, string?, int, Task> TestPlay { get; set; }

    private void LoadLibrary()
    {
        LibraryItems.Clear();
        if (_libraryDir.Length > 0)
            foreach (var path in UploadLibrary.List(_libraryDir, "*.wav"))
                LibraryItems.Add(new LibrarySoundItem(path));
        Named<StackPanel>("LibrarySection").IsVisible = LibraryItems.Count > 0;
    }

    private void LibraryItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibrarySoundItem item }) UseCustom(item.Path);
    }

    private void DeleteLibraryItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibrarySoundItem item })
        {
            UploadLibrary.Delete(item.Path);
            LibraryItems.Remove(item);
            Named<StackPanel>("LibrarySection").IsVisible = LibraryItems.Count > 0;
        }
        e.Handled = true;
    }

    private void UseCustom(string path)
    {
        _customPath = path;
        Named<TextBox>("CustomPathBox").Text = path;
        _tone = "Custom";
        UpdateToneChips();
        Named<StackPanel>("CustomPathPanel").IsVisible = true;
    }

    private void ToneChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag })
        {
            _tone = tag;
            UpdateToneChips();
            Named<StackPanel>("CustomPathPanel").IsVisible = tag == "Custom";
        }
    }

    private static readonly string[] Tones = ["Click", "Chime", "Blip", "Bell", "Alert", "Soft", "Ping", "Custom"];

    private void UpdateToneChips()
    {
        foreach (var tone in Tones) Named<ToggleButton>("Tone" + tone).IsChecked = _tone == tone;
    }

    internal string Tone => _tone;

    private void CustomPath_Changed(object? sender, TextChangedEventArgs e)
    {
        var text = Named<TextBox>("CustomPathBox").Text;
        _customPath = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        // (Linux pickers don't block the dialog: no second picker meanwhile, and nothing more to do
        // if the dialog was closed while the picker was up.)
        var browse = sender as Button;
        if (browse != null) browse.IsEnabled = false;
        try
        {
            var path = await PickWav();
            if (path == null || !IsVisible) return;
            UseCustom(path);
            // Keep the original in the user's uploads so it can be re-picked later without browsing.
            if (_libraryDir.Length > 0) UploadLibrary.Save(path, _libraryDir);
            LoadLibrary();
        }
        catch (Exception ex)
        {
            _logger.Warning("SwitchSoundDialog.Browse", ex.Message);
        }
        finally
        {
            if (browse != null) browse.IsEnabled = true;
        }
    }

    // The system's file picker for .wav files, on this dialog (Windows: OpenFileDialog).
    private async Task<string?> PickWavAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Sound File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WAV Files")
                {
                    Patterns = ["*.wav", "*.WAV"],
                    MimeTypes = ["audio/wav", "audio/x-wav"],
                    AppleUniformTypeIdentifiers = ["com.microsoft.waveform-audio"],
                },
            ],
        });
        return files.Count == 1 ? DialogService.LocalPath(files[0]) : null;
    }

    private void Volume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (this.FindControl<TextBlock>("VolumeLabel") == null) return; // (while the XAML loads)
        _volume = (int)Named<Slider>("VolumeSliderControl").Value;
        Named<TextBlock>("VolumeLabel").Text = $"{_volume}%";
    }

    private void BannerToggle_Changed(object? sender, RoutedEventArgs e) =>
        _showBanner = Named<CheckBox>("BannerToggle").IsChecked != true; // checked = suppress

    private async void TestSound_Click(object? sender, RoutedEventArgs e)
    {
        var btn = (Button)sender!;
        btn.IsEnabled = false;
        try { await TestPlay(_tone, _customPath, _volume); }
        catch (Exception ex) { _logger.Warning("SwitchSoundDialog.TestSound", ex.Message); }
        finally { btn.IsEnabled = true; }
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        SoundResult = new SoundOverrideResult(true, _tone, _customPath, _volume, _showBanner);
        CloseWith(true);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        // Enabled=false tells the caller to clear the per-profile sound override.
        SoundResult = new SoundOverrideResult(false, _tone, _customPath, _volume, _showBanner);
        CloseWith(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);

    private sealed class QuietLogger : IAppLogger
    {
        public void Info(string context, string message) { }
        public void Warning(string context, string message) { }
        public void Error(string context, string message) { }
        public void Error(string context, Exception ex) { }
        public void Debug(string context, string message) { }
    }
}

// A previously-uploaded custom switch sound from the user's library.
public sealed class LibrarySoundItem(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}
