using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VibeSwitcher.Views;

public partial class CustomReminderDialog : Window
{
    public int ResultMinutes { get; private set; }

    private static readonly List<string> HourItems =
        Enumerable.Range(0, 24).Select(h => h.ToString()).ToList();
    private static readonly List<string> MinuteItems =
        Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();

    public CustomReminderDialog(int currentMinutes)
    {
        InitializeComponent();
        HourCombo.ItemsSource = HourItems;
        MinuteCombo.ItemsSource = MinuteItems;

        var hr = currentMinutes > 0 ? currentMinutes / 60 : 0;
        var min = currentMinutes > 0 ? currentMinutes % 60 : 0;
        HourCombo.SelectedIndex = Math.Min(hr, 23);
        MinuteCombo.SelectedIndex = Math.Min(min, 59);

        Loaded += (_, _) => HourCombo.Focus();
        UpdateState();
    }

    private void Dropdowns_Changed(object sender, SelectionChangedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        if (HourCombo.SelectedIndex < 0 || MinuteCombo.SelectedIndex < 0) return;
        var total = HourCombo.SelectedIndex * 60 + MinuteCombo.SelectedIndex;
        var valid = total >= 1;
        OkButton.IsEnabled = valid;
        ErrorText.Visibility = !valid ? Visibility.Visible : Visibility.Collapsed;

        var h = HourCombo.SelectedIndex;
        var m = MinuteCombo.SelectedIndex;
        TotalHint.Text = total == 0
            ? "Select at least 1 minute"
            : h > 0
                ? $"{h} h {m:D2} min before the switch"
                : $"{m} min before the switch";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
        if (e.Key == Key.Enter && OkButton.IsEnabled) Confirm();
    }

    private void Confirm()
    {
        ResultMinutes = HourCombo.SelectedIndex * 60 + MinuteCombo.SelectedIndex;
        DialogResult = true;
    }
}
