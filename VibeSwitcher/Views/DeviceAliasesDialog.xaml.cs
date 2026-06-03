using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.Views;

public partial class DeviceAliasesDialog : Window, INotifyPropertyChanged
{
    private bool _isPlaybackTab = true;

    public IReadOnlyList<DeviceAliasItem> PlaybackDevices  { get; }
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

    public DeviceAliasesDialog(IEnumerable<DeviceAliasItem> aliases)
    {
        var list = aliases.ToList();
        PlaybackDevices  = list.Where(a => a.IsPlayback)
            .OrderByDescending(a => a.HasProfileUsage).ThenBy(a => a.RawName).ToList();
        RecordingDevices = list.Where(a => !a.IsPlayback)
            .OrderByDescending(a => a.HasProfileUsage).ThenBy(a => a.RawName).ToList();
        InitializeComponent();
        DataContext = this;
    }

    private void PlaybackTab_Click(object sender, RoutedEventArgs e) => IsPlaybackTab = true;
    private void RecordingTab_Click(object sender, RoutedEventArgs e) => IsPlaybackTab = false;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Flush any TextBox that still has focus before closing (UpdateSourceTrigger=LostFocus
        // would otherwise silently drop the last edit if Save is clicked without tabbing away).
        CommitFocusedTextBox();
        Close();
    }

    private void CommitFocusedTextBox()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox tb)
            BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
