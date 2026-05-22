using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Views;

namespace VibeSwitcher.Services;

public class DialogService : IDialogService
{
    private static Window? OwnerWindow =>
        Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

    public HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current)
    {
        var dialog = new HotkeyCaptureDialog(current) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.CapturedHotkey : null;
    }

    public bool ShowConfirmDelete(string profileName)
    {
        var dialog = new ConfirmDeleteDialog(profileName) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true;
    }

    public bool ShowConfirmClone(string profileName)
    {
        var badgeBg = Application.Current.TryFindResource("HoverBg") as Brush
                      ?? new SolidColorBrush(Colors.LightGray);
        var accent  = Application.Current.TryFindResource("Accent") as Brush
                      ?? new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x00));

        var dialog = new ConfirmDialog(
            "Clone Profile?",
            $"Create a copy of \"{profileName}\"?",
            "Clone",
            subtitle: "A new profile will be created with the same settings.",
            iconElement: BuildCopyIcon(badgeBg, accent),
            iconBgResource: "HoverBg")
        { Owner = OwnerWindow };
        return dialog.ShowDialog() == true;
    }

    public string? ShowBrowseIconFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Icon File",
            Filter = "Icon Files (*.ico)|*.ico",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public GalleryPickResult? ShowIconGallery()
    {
        var dialog = new IconGalleryDialog { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return null;
        return new GalleryPickResult { Item = dialog.SelectedItem, BrowseFromDisk = dialog.BrowseFromDisk, IconColor = dialog.SelectedColor };
    }

    public void ShowAlert(string title, string message)
    {
        new AlertDialog(title, message) { Owner = OwnerWindow }.ShowDialog();
    }

    public bool ShowHotkeyConflictRetry(string title, string message)
    {
        return new ConflictRetryDialog(title, message) { Owner = OwnerWindow }.ShowDialog() == true;
    }

    public ProfileMode? ShowProfileTypeDialog()
    {
        var dialog = new ProfileTypeDialog { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.ChosenMode : null;
    }

    public void ShowMicTest(string deviceId, string deviceName)
    {
        new MicTestDialog(deviceId, deviceName) { Owner = OwnerWindow }.ShowDialog();
    }

    public ScheduleEntry? ShowScheduleWizard(ScheduleEntry source, bool use12Hour)
    {
        var dialog = new ScheduleWizardDialog(source, use12Hour) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    // Two overlapping rounded rectangles drawn with WPF shapes — the universal copy/clone icon.
    // The front square's fill matches the badge background so it cleanly occludes the back square.
    private static UIElement BuildCopyIcon(Brush badgeBg, Brush stroke)
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var back = new Rectangle
        {
            Width = 11, Height = 11,
            Fill = Brushes.Transparent,
            Stroke = stroke, StrokeThickness = 1.5,
            RadiusX = 2, RadiusY = 2,
        };
        Canvas.SetLeft(back, 5);
        Canvas.SetTop(back, 0);

        var front = new Rectangle
        {
            Width = 11, Height = 11,
            Fill = badgeBg,
            Stroke = stroke, StrokeThickness = 1.5,
            RadiusX = 2, RadiusY = 2,
        };
        Canvas.SetLeft(front, 0);
        Canvas.SetTop(front, 5);

        canvas.Children.Add(back);
        canvas.Children.Add(front);
        return canvas;
    }
}
