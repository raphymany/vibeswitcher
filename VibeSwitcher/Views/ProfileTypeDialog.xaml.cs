using System.Windows;
using VibeSwitcher.Models;

namespace VibeSwitcher.Views;

public partial class ProfileTypeDialog : Window
{
    public ProfileMode? ChosenMode { get; private set; }

    public ProfileTypeDialog()
    {
        InitializeComponent();
    }

    private void Playback_Click(object sender, RoutedEventArgs e)
    {
        ChosenMode = ProfileMode.Playback;
        DialogResult = true;
    }

    private void Recording_Click(object sender, RoutedEventArgs e)
    {
        ChosenMode = ProfileMode.Recording;
        DialogResult = true;
    }

    private void Both_Click(object sender, RoutedEventArgs e)
    {
        ChosenMode = ProfileMode.Both;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
