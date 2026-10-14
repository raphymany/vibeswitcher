using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Dialogs;

namespace VibeSwitcher.App.Tests;

public class DialogTests
{
    private static Window Owner()
    {
        var owner = new Window { Width = 600, Height = 400 };
        owner.Show();
        return owner;
    }

    private static T OpenDialog<T>(Window owner) where T : Window
    {
        Dispatcher.UIThread.RunJobs();
        return owner.OwnedWindows.OfType<T>().Single();
    }

    private static Button ButtonWith(Window dialog, string text) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task ConfirmDelete_DeleteIsYes_CancelIsNo_AndShowsTheName()
    {
        var owner = Owner();
        var service = new DialogService(() => owner);

        var task = service.ShowConfirmDeleteAsync("Gaming");
        var dialog = OpenDialog<ConfirmDeleteDialog>(owner);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Are you sure you want to delete \"Gaming\"?");
        Assert.False(dialog.ShowInTaskbar);
        Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
        Click(ButtonWith(dialog, "Delete"));
        Assert.True(await task);

        task = service.ShowConfirmDeleteAsync("Gaming");
        Click(ButtonWith(OpenDialog<ConfirmDeleteDialog>(owner), "Cancel"));
        Assert.False(await task);
    }

    [AvaloniaFact]
    public async Task ClosingADialogAnyOtherWay_IsNo()
    {
        var owner = Owner();
        var service = new DialogService(() => owner);
        var task = service.ShowConfirmAsync("Replace Configuration?", "Replaces everything.", "Replace");
        OpenDialog<ConfirmDialog>(owner).Close();
        Assert.False(await task);
    }

    [AvaloniaFact]
    public async Task Escape_PressesTheCancelButton_EnterTheDefaultOne()
    {
        var owner = Owner();
        var service = new DialogService(() => owner);

        var task = service.ShowHotkeyConflictRetryAsync("Hotkey Already in Use", "Try another?");
        var dialog = OpenDialog<ConflictRetryDialog>(owner);
        dialog.Focus();
        dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.True(await task); // "Try Again" is the default

        task = service.ShowScheduleConflictAsync("Weekdays 9:00 – Work");
        dialog = OpenDialog<ConflictRetryDialog>(owner);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Schedule Conflict");
        dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(await task); // "Close" is the cancel button
    }

    [AvaloniaFact]
    public async Task Alert_And_Uninstall_AndADialogWithNoWindowToSitOn()
    {
        var service = new DialogService(() => null);
        var alert = service.ShowAlertAsync("Device Not Found", "Plug it in.");
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(OpenWindows.OfType<AlertDialog>());
        Assert.Equal(WindowStartupLocation.CenterScreen, dialog.WindowStartupLocation);
        Click(ButtonWith(dialog, "OK"));
        await alert;

        var owner = Owner();
        var uninstall = new UninstallDialog(UninstallText.LinuxPackage);
        var shown = new DialogService(() => owner).ShowAsync(uninstall);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(uninstall.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == UninstallText.LinuxPackage.Body);
        uninstall.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Click(ButtonWith(uninstall, "Uninstall"));
        Assert.True(await shown);
        Assert.True(uninstall.DeleteData);
    }

    // Headless tests have no desktop lifetime: windows are tracked as they open.
    private static readonly List<Window> OpenWindows = [];

    static DialogTests()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => OpenWindows.Add(w));
        Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => OpenWindows.Remove(w));
    }

    [AvaloniaFact]
    public void EveryDialog_HasTheVibeSwitcherFrame_OnEachSystem()
    {
        var old = WindowChrome.Style;
        try
        {
            foreach (var style in new[] { ChromeStyle.Custom, ChromeStyle.Mac })
            {
                WindowChrome.Style = style;
                foreach (var dialog in DialogCatalog.All())
                {
                    dialog.Show();
                    var bar = Assert.Single(dialog.GetVisualDescendants().OfType<TitleBar>());
                    Assert.Single(dialog.GetVisualDescendants().OfType<AccentFrame>());
                    Assert.Equal(dialog is SessionLogWindow, dialog.CanResize); // (the session log resizes, as on Windows)
                    Assert.NotNull(dialog.Icon);
                    var buttons = bar.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Buttons");
                    if (style == ChromeStyle.Mac)
                    {
                        Assert.True(dialog.ExtendClientAreaToDecorationsHint); // the system's own buttons
                        Assert.False(buttons.IsVisible);
                    }
                    else
                    {
                        Assert.Equal(SystemDecorations.None, dialog.SystemDecorations);
                        Assert.True(buttons.IsVisible);
                    }
                    dialog.Close();
                }
            }
        }
        finally { WindowChrome.Style = old; }
    }

    [AvaloniaFact]
    public async Task ADialogOverAHiddenWindow_StandsOnItsOwn()
    {
        var owner = Owner();
        owner.Hide(); // e.g. Settings hidden in the tray
        var task = new DialogService(() => owner).ShowConfirmDeleteAsync("Gaming");
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(OpenWindows.OfType<ConfirmDeleteDialog>());
        Assert.Equal(WindowStartupLocation.CenterScreen, dialog.WindowStartupLocation);
        Click(ButtonWith(dialog, "Delete"));
        Assert.True(await task);
    }

    [AvaloniaFact]
    public async Task TheTitleBarsCloseButton_IsNo()
    {
        var owner = Owner();
        var task = new DialogService(() => owner).ShowConfirmDeleteAsync("Gaming");
        var dialog = OpenDialog<ConfirmDeleteDialog>(owner);
        Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseBtn"));
        Assert.False(await task);
    }

    [AvaloniaFact]
    public void BadgeColours_FollowTheKind()
    {
        var info = new AlertDialog("Mini Mode", "Small window.", AlertKind.Info);
        info.Show();
        var badge = info.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "IconBorder");
        Assert.Same(Find("InfoBadgeBg"), badge.Background);
        info.Close();

        // On an accent badge the icon is white; on a tinted one it's the warning colour.
        var onAccent = new ConfirmDialog("Welcome to Mini Mode", "…", "Got it", iconGeometry: "IcoCompact", iconBgResource: "Accent");
        onAccent.Show();
        var icon = (Avalonia.Controls.Shapes.Path)onAccent.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "IconContent").Content!;
        Assert.Equal(Avalonia.Media.Colors.White, ((Avalonia.Media.ISolidColorBrush)icon.Stroke!).Color);
        onAccent.Close();
    }

    [AvaloniaFact]
    public void FocusRings_AsOnWindows()
    {
        var preview = new DialogPreviewWindow();
        preview.Show();
        var boxes = preview.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Null(boxes[0].FocusAdorner);    // the toggle switch draws its own ring
        Assert.NotNull(boxes[1].FocusAdorner); // the check box gets the accent outline
        Assert.Null(preview.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Alert").FocusAdorner);
        preview.Close();
    }

    private static object? Find(string key) =>
        Avalonia.Application.Current!.TryFindResource(key, Avalonia.Application.Current!.ActualThemeVariant, out var v) ? v : null;
}
