using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Imaging;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using WpfVisibility = System.Windows.Visibility;

namespace VibeSwitcher.App.Tests;

// What the shared (Windows) Settings logic needs from the app: dialogs it can wait on, pictures,
// gallery icons, and its WPF-style show/hide values.
public class SettingsBridgeTests
{
    private static Window Shown(Window window)
    {
        window.Show();
        return window;
    }

    private static Window Owner() => Shown(new Window { Width = 600, Height = 400 });

    private static void Click(Window dialog, string button) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == button)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Clicks a button in a dialog once it's up (while the caller waits on it).
    private static void ClickLater(Func<Window> dialog, string button, Action<Window>? check = null) =>
        Dispatcher.UIThread.Post(() =>
        {
            var d = dialog();
            check?.Invoke(d);
            Click(d, button);
        }, DispatcherPriority.Background);

    [AvaloniaFact]
    public void SynchronousDialogs_WaitForTheAnswer_AsOnWindows()
    {
        var owner = Owner();
        IDialogService service = new DialogService(() => owner);

        ClickLater(() => owner.OwnedWindows.Single(), "Delete");
        Assert.True(service.ShowConfirmDelete("Gaming"));
        Assert.Empty(owner.OwnedWindows);

        ClickLater(() => owner.OwnedWindows.Single(), "Cancel");
        Assert.False(service.ShowConfirm("Replace Configuration?", "Replaces everything.", "Replace"));

        ClickLater(() => owner.OwnedWindows.Single(), "OK");
        service.ShowAlert("Icon Error", "Could not save the gallery icon.");
        Assert.Empty(owner.OwnedWindows);
        owner.Close();
    }

    [AvaloniaFact]
    public void WhileWaiting_TheAppKeepsRunning()
    {
        var owner = Owner();
        IDialogService service = new DialogService(() => owner);
        int ticks = 0;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1), DispatcherPriority.Normal, (_, _) => ticks++);
        timer.Start();
        Dispatcher.UIThread.Post(() => { Thread.Sleep(20); ClickLater(() => owner.OwnedWindows.Single(), "Try Again"); }, DispatcherPriority.Background);
        Assert.True(service.ShowHotkeyConflictRetry("Hotkey Already in Use", "Try another?"));
        timer.Stop();
        Assert.True(ticks > 0);
        owner.Close();
    }

    [AvaloniaFact]
    public void WithSettingsHidden_ADialogStandsOnItsOwn_InFront()
    {
        var hiddenSettings = new Window();
        var dialog = new ConfirmDialog("Exit VibeSwitcher?", "Hotkeys will stop working.", "Exit", subtitle: "");
        var task = new DialogService(() => hiddenSettings).ShowAsync(dialog);
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.IsVisible);
        Assert.Null(dialog.Owner);
        Assert.True(dialog.Topmost);
        Assert.Equal(WindowStartupLocation.CenterScreen, dialog.WindowStartupLocation);
        Click(dialog, "Exit");
        Assert.True(task.IsCompletedSuccessfully && task.Result);
    }

    [AvaloniaFact]
    public void ADialogOpenedFromADialog_BelongsToThatDialog()
    {
        var owner = Owner();
        var service = new DialogService(() => owner);
        var outer = service.ShowConfirmAsync("Clone profile", "Pick a hotkey", "Clone");
        Dispatcher.UIThread.RunJobs();
        var wizard = owner.OwnedWindows.Single();

        Window? alertOwner = null;
        ClickLater(() => wizard.OwnedWindows.Single(), "OK", d => alertOwner = d.Owner as Window);
        ((IDialogService)service).ShowAlert("Hotkey Already in Use", "Pick another.");
        Assert.Same(wizard, alertOwner);

        Click(wizard, "Clone");
        Assert.True(outer.IsCompletedSuccessfully && outer.Result);
        owner.Close();
    }

    [AvaloniaFact]
    public void WhileADialogIsOpen_TheAppsOtherWindowsIgnoreInput()
    {
        var owner = Owner();
        var miniContent = new Border { Focusable = true };
        var mini = Shown(new Window { Width = 200, Height = 300, Content = miniContent });
        int keys = 0;
        miniContent.KeyDown += (_, _) => keys++;
        void PressKey() => miniContent.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        var service = new DialogService(() => owner, () => [owner, mini]);
        var task = service.ShowConfirmDeleteAsync("Gaming");
        Dispatcher.UIThread.RunJobs();
        PressKey();
        Assert.Equal(0, keys);

        Click(owner.OwnedWindows.Single(), "Cancel");
        Assert.False(task.Result);
        PressKey();
        Assert.Equal(1, keys);
        mini.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Waiting_IsForTheUiThreadOnly_AndADialogsFailureSurfaces()
    {
        Assert.Throws<InvalidDataException>(() => DialogService.Wait(Task.FromException(new InvalidDataException("broken dialog"))));
        Assert.Equal(7, DialogService.Wait(Task.FromResult(7)));
        await Task.Run(() => Assert.ThrowsAny<InvalidOperationException>(() => DialogService.Wait(Task.CompletedTask)));
    }

    [AvaloniaFact]
    public void TheAppTriggerWizard_NotPortedYet_AnswersCancelled_AndPickersNeedAWindow()
    {
        IDialogService service = new DialogService(() => null);
        Assert.Null(service.ShowAppTriggerWizard([], new Dictionary<string, string>()));
        Assert.Null(service.ShowBrowseIconFile()); // no window to show the file picker on
    }

    [AvaloniaFact]
    public void IconFiles_BecomePictures()
    {
        AppImages.Install();
        using var icon = IconHelper.GetDefaultIcon();
        var image = Assert.IsType<AvaloniaImage>(IconHelper.ToImageSource(icon));
        Assert.True(image.Bitmap.PixelSize.Width >= 32);
        Assert.Same(image.Bitmap, AppImages.ToBitmap.Convert(image, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaTheory]
    [InlineData(IconColor.Black, 0)]
    [InlineData(IconColor.White, 255)]
    public void GalleryIcons_AreDrawnInTheChosenColour(IconColor color, byte value)
    {
        var png = AppImages.RenderGlyph("🎮", color, 64);
        using var bitmap = new Bitmap(new MemoryStream(png));
        Assert.Equal(64, bitmap.PixelSize.Width);
        Assert.Equal(64, bitmap.PixelSize.Height);

        using var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using var frame = copy.Lock();
        bitmap.CopyPixels(frame, Avalonia.Platform.AlphaFormat.Unpremul);
        var pixels = new byte[frame.RowBytes * 64];
        System.Runtime.InteropServices.Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
        int drawn = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 128) continue; // (edges are blended; check the solid part)
            drawn++;
            // (±1: reading back the stored, alpha-scaled colour rounds.)
            Assert.InRange(pixels[i], value - 1, value + 1);
            Assert.InRange(pixels[i + 1], value - 1, value + 1);
            Assert.InRange(pixels[i + 2], value - 1, value + 1);
        }
        Assert.True(drawn > 50, "the glyph should cover part of the square");
    }

    [AvaloniaFact]
    public void GalleryIcons_AreSavedAsIcons_TheSettingsLogicCanLoad()
    {
        AppImages.Install();
        var dir = Path.Combine(Path.GetTempPath(), $"VSGallery_{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(dir, "Gaming.ico");
            GalleryIconHelper.SaveGalleryIcon(GalleryIconHelper.Items[0], path, IconColor.Auto);
            using var icon = IconHelper.LoadIcon(path, dir);
            Assert.Equal(File.ReadAllBytes(path), icon.Data);
            var image = Assert.IsType<AvaloniaImage>(IconHelper.ToImageSource(icon));
            Assert.Equal(64, image.Bitmap.PixelSize.Width);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void WpfVisibility_CollapsedTakesNoSpace_HiddenKeepsIt()
    {
        var input = new TextBox();
        var hidden = new Border { Width = 50, Height = 20, Opacity = 0.6, Child = input };
        var collapsed = new Button { Width = 50, Height = 20 };
        var panel = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Children = { hidden, collapsed } };
        var window = Shown(new Window { Content = panel });

        Wpf.SetVisibility(collapsed, WpfVisibility.Collapsed);
        Wpf.SetVisibility(hidden, WpfVisibility.Hidden);
        window.UpdateLayout();
        Assert.False(collapsed.IsVisible);
        Assert.True(hidden.IsVisible);
        Assert.Equal(20, panel.Bounds.Height); // the hidden part still takes its place
        Assert.Equal(0, hidden.Opacity);
        Assert.False(hidden.IsHitTestVisible);
        Assert.False(input.Focus()); // nothing inside can take focus

        Wpf.SetVisibility(hidden, WpfVisibility.Visible);
        Wpf.SetVisibility(collapsed, WpfVisibility.Visible);
        window.UpdateLayout();
        Assert.Equal(40, panel.Bounds.Height);
        Assert.Equal(0.6, hidden.Opacity); // its own value is back
        Assert.True(hidden.IsHitTestVisible);
        Assert.True(input.Focus());
        window.Close();
    }
}
