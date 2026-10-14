using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/DeviceAliasesDialog.xaml.cs: an alias is saved as soon as its field loses focus (the
// shared view model writes it to the config), so Save and Close both keep what was typed — Save
// first commits the field still being typed in.
public partial class DeviceAliasesDialog : VsDialog, INotifyPropertyChanged
{
    private bool _isPlaybackTab = true;

    public IReadOnlyList<DeviceAliasItem> PlaybackDevices { get; }
    public IReadOnlyList<DeviceAliasItem> RecordingDevices { get; }

    public bool IsPlaybackTab
    {
        get => _isPlaybackTab;
        private set
        {
            if (_isPlaybackTab == value) return;
            _isPlaybackTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRecordingTab));
        }
    }

    public bool IsRecordingTab => !_isPlaybackTab;

    public DeviceAliasesDialog() : this([]) { } // (for the XAML loader)

    public DeviceAliasesDialog(IEnumerable<DeviceAliasItem> aliases)
    {
        var list = aliases.ToList();
        PlaybackDevices = list.Where(a => a.IsPlayback)
            .OrderByDescending(a => a.HasProfileUsage).ThenBy(a => a.RawName).ToList();
        RecordingDevices = list.Where(a => !a.IsPlayback)
            .OrderByDescending(a => a.HasProfileUsage).ThenBy(a => a.RawName).ToList();
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
    }

    private void PlaybackTab_Click(object? sender, RoutedEventArgs e) => IsPlaybackTab = true;
    private void RecordingTab_Click(object? sender, RoutedEventArgs e) => IsPlaybackTab = false;

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        // Flush any field that still has focus before closing (an alias is otherwise only saved
        // when its field loses focus, so the last edit would be dropped).
        CommitFocusedTextBox();
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void CommitFocusedTextBox()
    {
        if (FocusManager?.GetFocusedElement() is TextBox tb)
            BindingOperations.GetBindingExpressionBase(tb, TextBox.TextProperty)?.UpdateSource();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
