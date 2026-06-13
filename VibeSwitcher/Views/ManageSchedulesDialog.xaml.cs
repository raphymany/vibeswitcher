using System.Collections.Specialized;
using System.Windows;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.Views;

// Lists every schedule on a profile with per-row Edit and Remove. Opened from the card's
// schedule chip when the profile has more than one schedule (a single schedule edits directly).
public partial class ManageSchedulesDialog : Window
{
    private readonly ProfileCardViewModel _card;

    public ManageSchedulesDialog(ProfileCardViewModel card)
    {
        InitializeComponent();
        _card = card;
        DataContext = card;

        // Nothing left to manage once the last schedule is removed — close instead of
        // showing an empty list.
        card.Schedules.CollectionChanged += OnSchedulesChanged;
        Closed += (_, _) => card.Schedules.CollectionChanged -= OnSchedulesChanged;
    }

    private void OnSchedulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_card.Schedules.Count == 0) Close();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
