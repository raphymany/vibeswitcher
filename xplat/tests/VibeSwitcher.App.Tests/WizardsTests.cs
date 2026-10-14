using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Path = System.IO.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Imaging;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;
using static VibeSwitcher.App.Tests.Ui;

namespace VibeSwitcher.App.Tests;

// The wizards the profile cards open: clone, icon gallery, schedule, switch sound.
public class WizardsTests
{
    private static void Key(Window w, Key key) =>
        w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static Button ButtonNamed(Window w, string content) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == content && b.IsEffectivelyVisible);

    // ── Icon gallery ───────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheGalleryDrawsEachEmoji_WithTheBundledFont_InTheChosenColour()
    {
        AppImages.Install();
        // Every gallery emoji is in the app's own emoji font (no system emoji font needed).
        Assert.True(Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(
            new Avalonia.Media.Typeface(Theming.AppFonts.Emoji), out var emojiFont));
        Assert.Equal("Noto Emoji", emojiFont!.FamilyName);
        foreach (var item in GalleryIconHelper.Items)
            Assert.True(emojiFont.TryGetGlyph((uint)char.ConvertToUtf32(item.Emoji, 0), out _), item.Label);

        var dialog = new IconGalleryDialog("");
        dialog.Show();
        Assert.Equal(12, dialog.PreviewItems.Count);
        Assert.All(dialog.PreviewItems, p => Assert.NotNull(p.Preview));
        Assert.Equal(IconColor.Black, dialog.SelectedColor);
        var black = CenterPixel(dialog.PreviewItems[0].Preview!);

        Named<RadioButton>(dialog, "ColorWhite").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(IconColor.White, dialog.SelectedColor);
        var white = CenterPixel(dialog.PreviewItems[0].Preview!);
        Assert.True(white.Take(3).Sum(b => (int)b) > black.Take(3).Sum(b => (int)b), "white previews should be lighter");
        Assert.False(Named<StackPanel>(dialog, "LibrarySection").IsVisible); // no uploads

        var gaming = dialog.GetVisualDescendants().OfType<Button>().First(b => b.Tag is GalleryPreviewItem { Label: "Gaming" });
        Click(gaming);
        Assert.True(dialog.Result);
        Assert.Equal("Gaming", dialog.SelectedItem!.Label);
        Assert.Null(dialog.CustomIconPath);
        Assert.False(dialog.BrowseFromDisk);
    }

    // The most opaque pixel's colour (B, G, R, A) in the middle row.
    private static byte[] CenterPixel(Bitmap bitmap)
    {
        int w = bitmap.PixelSize.Width, h = bitmap.PixelSize.Height;
        var bytes = new byte[w * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new Avalonia.PixelRect(0, h / 2, w, 1), handle.AddrOfPinnedObject(), bytes.Length, w * 4); }
        finally { handle.Free(); }
        return Enumerable.Range(0, w).Select(x => bytes.Skip(x * 4).Take(4).ToArray()).MaxBy(p => p[3])!;
    }

    [AvaloniaFact]
    public void TheGallery_OffersEarlierUploads_AndBrowsing()
    {
        AppImages.Install();
        var library = Directory.CreateTempSubdirectory("vs-icons-").FullName;
        try
        {
            GalleryIconHelper.SaveGalleryIcon(GalleryIconHelper.Items[1], Path.Combine(library, "Briefcase.ico"));
            var dialog = new IconGalleryDialog(library);
            dialog.Show();
            Assert.True(Named<StackPanel>(dialog, "LibrarySection").IsVisible);
            var item = Assert.Single(dialog.LibraryItems);
            Assert.Equal("Briefcase", item.Name);

            var pick = Named<ItemsControl>(dialog, "LibraryList").GetVisualDescendants().OfType<Button>()
                .First(b => b.Tag == item && ToolTip.GetTip(b) as string == "Briefcase");
            Click(pick);
            Assert.True(dialog.Result);
            Assert.Equal(item.Path, dialog.CustomIconPath);

            // Removing an upload deletes it and hides the section once empty.
            var again = new IconGalleryDialog(library);
            again.Show();
            var remove = Named<ItemsControl>(again, "LibraryList").GetVisualDescendants().OfType<Button>()
                .First(b => ToolTip.GetTip(b) as string == "Remove from your uploads");
            Click(remove);
            Assert.Empty(again.LibraryItems);
            Assert.False(File.Exists(item.Path));
            Assert.False(Named<StackPanel>(again, "LibrarySection").IsVisible);
            Assert.True(again.IsVisible); // (removing doesn't pick)

            Click(again, "Browse from disk...");
            Assert.True(again.BrowseFromDisk);
            Assert.True(again.Result);
        }
        finally { Directory.Delete(library, recursive: true); }
    }

    [AvaloniaFact]
    public void TheDialogService_ReturnsTheGalleryPick()
    {
        AppImages.Install();
        var owner = new Window();
        owner.Show();
        IDialogService service = new DialogService(() => owner) { Config = new FakeConfigService() };
        Dispatcher.UIThread.Post(() =>
        {
            var gallery = owner.OwnedWindows.OfType<IconGalleryDialog>().Single();
            Named<RadioButton>(gallery, "ColorWhite").IsChecked = true;
            Click(gallery.GetVisualDescendants().OfType<Button>().First(b => b.Tag is GalleryPreviewItem { Label: "Night" }));
        }, DispatcherPriority.Background);
        var pick = service.ShowIconGallery();
        Assert.Equal("Night", pick!.Item!.Label);
        Assert.Equal(IconColor.White, pick.IconColor);
        owner.Close();
    }

    // ── Clone ──────────────────────────────────────────────────────────────────────────────────────

    private static CloneProfileDialog Clone(DeviceProfile source, Func<HotkeyDefinition, HotkeyDefinition?>? capture = null)
    {
        var dialog = new CloneProfileDialog(source, [SettingsCatalog.Speakers, SettingsCatalog.Headset],
            [SettingsCatalog.HeadsetMic], use12Hour: false, capture ?? (h => h));
        dialog.Show();
        return dialog;
    }

    private static int VisibleDots(Window w) => w.GetVisualDescendants().OfType<Ellipse>().Count(e => e.Classes.Contains("dot") && e.IsVisible);

    [AvaloniaFact]
    public void Clone_WalksItsSteps_AndCopiesWhatWasPicked()
    {
        var source = DialogCatalog.CloneSource();
        var dialog = Clone(source, _ => new HotkeyDefinition { VirtualKeyCode = 0x48, UseCtrl = true });
        Assert.Equal("Gaming (copy)", Named<TextBox>(dialog, "NameBox").Text);
        Assert.Equal(4, VisibleDots(dialog)); // (it has a schedule: four steps)
        Assert.Equal(0, dialog.Step);
        Assert.False(Named<Button>(dialog, "BackBtn").IsVisible);

        Named<TextBox>(dialog, "NameBox").Text = "  Late night  ";
        Click(Named<Button>(dialog, "SetHotkeyBtn"));
        Assert.Contains("H", Named<TextBlock>(dialog, "HotkeyLabel").Text);

        Click(Named<Button>(dialog, "NextBtn"));
        Assert.Equal(1, dialog.Step);
        Assert.Equal(SettingsCatalog.Headset, Named<ComboBox>(dialog, "PlaybackCombo").SelectedItem);
        Named<RadioButton>(dialog, "ModePlayback").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Named<ComboBox>(dialog, "RecordingCombo").IsEnabled);
        Assert.Equal(0.5, Named<TextBlock>(dialog, "RecordingLabel").Opacity);
        Named<ComboBox>(dialog, "PlaybackCombo").SelectedItem = SettingsCatalog.Speakers;

        Click(Named<Button>(dialog, "NextBtn"));
        Assert.Equal(2, dialog.Step);
        // Only what the source has is offered: sound, triggers, pinned, notes (not auto-switch, silent, icon).
        Assert.True(Named<CheckBox>(dialog, "chkSound").IsVisible);
        Assert.False(Named<CheckBox>(dialog, "chkAutoSwitch").IsVisible);
        Assert.False(Named<CheckBox>(dialog, "chkIcon").IsVisible);
        Named<CheckBox>(dialog, "chkNotes").IsChecked = false;

        Click(Named<Button>(dialog, "NextBtn"));
        Assert.Equal(3, dialog.Step);
        Assert.Equal("Create ✓", Named<Button>(dialog, "NextBtn").Content);
        Click(Named<Button>(dialog, "BackBtn"));
        Assert.Equal(2, dialog.Step);
        Click(Named<Button>(dialog, "NextBtn"));
        Click(Named<Button>(dialog, "NextBtn")); // Create

        Assert.True(dialog.Result);
        var clone = dialog.ClonedProfile!;
        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("Late night", clone.Name);
        Assert.Equal(ProfileMode.Playback, clone.Mode);
        Assert.Equal(SettingsCatalog.Speakers.Id, clone.PlaybackDeviceId);
        Assert.Null(clone.RecordingDeviceId);
        Assert.Equal(0x48, clone.Hotkey.VirtualKeyCode);
        Assert.True(clone.SoundOverride);
        Assert.Equal("Chime", clone.SoundTone);
        Assert.True(clone.IsPinned);
        Assert.Equal(new[] { "/usr/bin/steam" }, clone.AppTriggers);
        Assert.Null(clone.Notes); // unticked
        var schedule = Assert.Single(clone.Schedules);
        Assert.Equal(18, schedule.Hour);
        Assert.NotEqual(source.Schedules[0].Id, schedule.Id);
    }

    [AvaloniaFact]
    public void Clone_WithNothingExtra_HasThreeSteps_AndSaysThereIsNothingToCopy()
    {
        var plain = new DeviceProfile { Name = "Work", Mode = ProfileMode.Playback, PlaybackDeviceId = SettingsCatalog.Speakers.Id };
        var dialog = Clone(plain);
        Assert.Equal(3, VisibleDots(dialog));
        Click(Named<Button>(dialog, "NextBtn"));
        Assert.False(Named<ComboBox>(dialog, "RecordingCombo").IsEnabled); // (playback-only)
        Click(Named<Button>(dialog, "NextBtn"));
        Assert.True(Named<TextBlock>(dialog, "NoCopyOptionsHint").IsVisible);
        Assert.False(Named<StackPanel>(dialog, "CopyOptionsPanel").IsVisible);
        Assert.Equal("Create ✓", Named<Button>(dialog, "NextBtn").Content);

        Named<TextBox>(dialog, "NameBox").Text = "   ";
        Click(Named<Button>(dialog, "NextBtn"));
        Assert.Equal("Work (copy)", dialog.ClonedProfile!.Name); // (a blank name falls back)
    }

    [AvaloniaFact]
    public void Clone_ACopiedSoundTurnsOffSilent_AndEscapeCancels()
    {
        var source = DialogCatalog.CloneSource();
        source.Silent = true;
        var dialog = Clone(source);
        var silent = Named<CheckBox>(dialog, "chkSilent");
        Assert.True(silent.IsVisible);
        Assert.False(silent.IsEnabled); // the copied sound has its own banner setting
        Assert.False(silent.IsChecked);
        Named<CheckBox>(dialog, "chkSound").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(silent.IsEnabled);

        Key(dialog, Avalonia.Input.Key.Escape);
        Assert.False(dialog.Result);
        Assert.False(dialog.IsVisible);
        Assert.Null(dialog.ClonedProfile);
    }

    // ── Schedule ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Schedule_A24HourTime_DaysAndACustomReminder()
    {
        var source = new ScheduleEntry { Hour = 7, Minute = 30, Days = [DayOfWeek.Monday], ReminderMinutes = 0 };
        var dialog = new ScheduleWizardDialog(source, use12Hour: false);
        dialog.Show();
        Assert.Equal("Add Schedule", Named<TextBlock>(dialog, "HeaderTitle").Text);
        Assert.False(Named<Button>(dialog, "RemoveBtn").IsVisible);
        Assert.False(Named<Button>(dialog, "SaveBtn").IsVisible); // (a new schedule walks every step)
        Assert.False(Named<Button>(dialog, "AmPmButton").IsVisible);
        Assert.Equal("7", Named<ComboBox>(dialog, "HourCombo").SelectedItem);
        Named<ComboBox>(dialog, "HourCombo").SelectedIndex = 21;
        Named<ComboBox>(dialog, "MinuteCombo").SelectedIndex = 5;

        Click(Named<Button>(dialog, "NextBtn"));
        Assert.True(Named<ToggleButton>(dialog, "MonBtn").IsChecked);
        Named<ToggleButton>(dialog, "MonBtn").IsChecked = false;
        Click(Named<ToggleButton>(dialog, "MonBtn"));
        Assert.True(Named<TextBlock>(dialog, "NoDaysHint").IsVisible);
        Named<ToggleButton>(dialog, "SatBtn").IsChecked = true;
        Named<ToggleButton>(dialog, "SunBtn").IsChecked = true;
        Click(Named<ToggleButton>(dialog, "SunBtn"));
        Assert.False(Named<TextBlock>(dialog, "NoDaysHint").IsVisible);

        Click(Named<Button>(dialog, "NextBtn"));
        Named<ComboBox>(dialog, "ReminderCombo").SelectedIndex = 5; // Custom…
        Assert.True(Named<StackPanel>(dialog, "CustomReminderPanel").IsVisible);
        Assert.Equal("Must be at least 1 minute — will be saved as No reminder", Named<TextBlock>(dialog, "ReminderTotalHint").Text);
        Named<ComboBox>(dialog, "ReminderHrCombo").SelectedIndex = 1;
        Named<ComboBox>(dialog, "ReminderMinCombo").SelectedIndex = 5;
        Assert.Equal("1 h 05 min before the switch", Named<TextBlock>(dialog, "ReminderTotalHint").Text);

        Click(Named<Button>(dialog, "NextBtn"));
        Named<CheckBox>(dialog, "SilentCheck").IsChecked = true;
        Assert.Equal("Save ✓", Named<Button>(dialog, "NextBtn").Content);
        Click(Named<Button>(dialog, "NextBtn"));

        var result = dialog.ScheduleResult!;
        Assert.Equal(source.Id, result.Id);
        Assert.Equal((21, 5), (result.Hour, result.Minute));
        Assert.Equal(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, result.Days);
        Assert.Equal(65, result.ReminderMinutes);
        Assert.True(result.Silent);
    }

    [AvaloniaFact]
    public void Schedule_TwelveHourClock_SaveEarlyWhenEditing_AndRemove()
    {
        var source = new ScheduleEntry { Hour = 18, Minute = 0, Days = [DayOfWeek.Friday], ReminderMinutes = 45 };
        var dialog = new ScheduleWizardDialog(source, use12Hour: true, isEditing: true);
        dialog.Show();
        Assert.Equal("Edit Schedule", Named<TextBlock>(dialog, "HeaderTitle").Text);
        Assert.Equal("6", Named<ComboBox>(dialog, "HourCombo").SelectedItem);
        Assert.Equal("PM", Named<Button>(dialog, "AmPmButton").Content);
        Click(Named<Button>(dialog, "AmPmButton"));
        Assert.Equal("AM", Named<Button>(dialog, "AmPmButton").Content);
        Named<ComboBox>(dialog, "HourCombo").SelectedIndex = 11; // 12 AM
        Assert.True(Named<Button>(dialog, "SaveBtn").IsVisible);
        Click(Named<Button>(dialog, "SaveBtn"));
        var saved = dialog.ScheduleResult!;
        Assert.Equal(0, saved.Hour); // 12 AM = midnight
        Assert.Equal(45, saved.ReminderMinutes); // (the custom reminder it had, untouched)
        Assert.Equal(new[] { DayOfWeek.Friday }, saved.Days);

        var edit = new ScheduleWizardDialog(source, use12Hour: true, isEditing: true);
        edit.Show();
        Assert.Equal(5, Named<ComboBox>(edit, "ReminderCombo").SelectedIndex); // custom: 45 min
        Assert.True(Named<StackPanel>(edit, "CustomReminderPanel").IsVisible);
        Click(Named<Button>(edit, "RemoveBtn"));
        Assert.True(edit.RemoveRequested);
        Assert.True(edit.Result);
    }

    [AvaloniaFact]
    public void TheDialogService_TurnsTheScheduleWizardIntoAnOutcome()
    {
        var owner = new Window();
        owner.Show();
        IDialogService service = new DialogService(() => owner);
        var entry = new ScheduleEntry { Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };

        ClickLater(owner, "Remove");
        Assert.True(service.ShowScheduleWizard(entry, use12Hour: false, isEditing: true).Removed);
        ClickLater(owner, "Cancel");
        Assert.Equal(ScheduleWizardOutcome.Cancelled, service.ShowScheduleWizard(entry, use12Hour: false));
        ClickLater(owner, "Save");
        Assert.Equal(9, service.ShowScheduleWizard(entry, use12Hour: false, isEditing: true).Entry!.Hour);
        owner.Close();
    }

    // ── Switch sound ───────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Sound_ATone_Volume_Banner_AndATestPlay()
    {
        var dialog = new SwitchSoundDialog(true, "Chime", null, 60, null, "", showBanner: true);
        dialog.Show();
        Assert.True(Named<ToggleButton>(dialog, "ToneChime").IsChecked);
        Assert.False(Named<ToggleButton>(dialog, "ToneClick").IsChecked);
        Assert.Equal("60%", Named<TextBlock>(dialog, "VolumeLabel").Text);
        Assert.False(Named<CheckBox>(dialog, "BannerToggle").IsChecked); // (it shows the banner)
        Assert.False(Named<Button>(dialog, "RemoveBtn").IsVisible);
        Assert.False(Named<StackPanel>(dialog, "CustomPathPanel").IsVisible);

        Click(Named<ToggleButton>(dialog, "TonePing"));
        Assert.Equal("Ping", dialog.Tone);
        Assert.False(Named<ToggleButton>(dialog, "ToneChime").IsChecked);
        Named<Slider>(dialog, "VolumeSliderControl").Value = 35;
        Assert.Equal("35%", Named<TextBlock>(dialog, "VolumeLabel").Text);
        Named<CheckBox>(dialog, "BannerToggle").IsChecked = true;

        var played = new List<(string, string?, int)>();
        var gate = new TaskCompletionSource();
        dialog.TestPlay = (t, p, v) => { played.Add((t, p, v)); return gate.Task; };
        var test = Named<Button>(dialog, "TestBtn");
        Click(test);
        Assert.False(test.IsEnabled); // while it plays
        gate.SetResult();
        await Until(() => test.IsEnabled);
        Assert.Equal(new[] { ("Ping", (string?)null, 35) }, played);

        Key(dialog, Avalonia.Input.Key.Enter); // Enter saves
        var result = dialog.SoundResult!;
        Assert.True(result.Enabled);
        Assert.Equal("Ping", result.Tone);
        Assert.Equal(35, result.Volume);
        Assert.False(result.ShowBanner);
    }

    [AvaloniaFact]
    public async Task Sound_ACustomFile_IsKeptInTheUploads_AndCanBeRemoved()
    {
        var library = Directory.CreateTempSubdirectory("vs-sounds-").FullName;
        var source = Path.Combine(Path.GetTempPath(), $"vs-door-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(source, SilentWav());
        try
        {
            var dialog = new SwitchSoundDialog(true, "Click", null, 50, null, library, isEditing: true);
            dialog.Show();
            Assert.False(Named<StackPanel>(dialog, "LibrarySection").IsVisible);
            Click(Named<ToggleButton>(dialog, "ToneCustom"));
            Assert.True(Named<StackPanel>(dialog, "CustomPathPanel").IsVisible);

            dialog.PickWav = () => Task.FromResult<string?>(source);
            Click(ButtonNamed(dialog, "Browse…"));
            await Until(() => dialog.LibraryItems.Count == 1);
            Assert.Equal(source, Named<TextBox>(dialog, "CustomPathBox").Text);
            Assert.True(Named<StackPanel>(dialog, "LibrarySection").IsVisible);
            var upload = Assert.Single(dialog.LibraryItems);
            Assert.True(File.Exists(upload.Path));

            // Typing a path keeps it too; picking an upload switches to it.
            Named<TextBox>(dialog, "CustomPathBox").Text = "  /tmp/other.wav ";
            Click(Named<ItemsControl>(dialog, "LibraryList").GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == upload.Name));
            Assert.Equal(upload.Path, Named<TextBox>(dialog, "CustomPathBox").Text);

            Click(Named<ItemsControl>(dialog, "LibraryList").GetVisualDescendants().OfType<Button>()
                .First(b => ToolTip.GetTip(b) as string == "Remove from your uploads"));
            Assert.Empty(dialog.LibraryItems);
            Assert.False(File.Exists(upload.Path));

            Click(Named<Button>(dialog, "RemoveBtn")); // remove the profile's own sound
            Assert.False(dialog.SoundResult!.Enabled);
        }
        finally
        {
            File.Delete(source);
            Directory.Delete(library, recursive: true);
        }
    }

    // A short silent 16-bit mono .wav.
    private static byte[] SilentWav()
    {
        const int samples = 100, rate = 44100;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2); w.Write(new byte[samples * 2]);
        w.Flush();
        return stream.ToArray();
    }

    [AvaloniaFact]
    public void Sound_EscapeCancels()
    {
        var dialog = new SwitchSoundDialog(true, null, null, 50, null, "");
        dialog.Show();
        Assert.True(Named<ToggleButton>(dialog, "ToneClick").IsChecked); // (no tone yet: Click)
        Key(dialog, Avalonia.Input.Key.Escape);
        Assert.False(dialog.Result);
        Assert.Null(dialog.SoundResult);
    }

    // ── More cases ─────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheGalleryColour_GoesBackToBlack_Too()
    {
        AppImages.Install();
        var dialog = new IconGalleryDialog("");
        dialog.Show();
        var black = CenterPixel(dialog.PreviewItems[0].Preview!);
        Named<RadioButton>(dialog, "ColorWhite").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Named<RadioButton>(dialog, "ColorBlack").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(IconColor.Black, dialog.SelectedColor);
        Assert.Equal(black, CenterPixel(dialog.PreviewItems[0].Preview!)); // drawn black again
        dialog.Close();
    }

    [AvaloniaFact]
    public void Schedule_NoonAndMidnight_Presets_Back_AndEscape()
    {
        var noon = new ScheduleWizardDialog(new ScheduleEntry { Hour = 12, Minute = 0 }, use12Hour: true);
        noon.Show();
        Assert.Equal("12", Named<ComboBox>(noon, "HourCombo").SelectedItem);
        Assert.Equal("PM", Named<Button>(noon, "AmPmButton").Content);
        Click(Named<Button>(noon, "NextBtn"));
        Click(Named<Button>(noon, "NextBtn"));
        Named<ComboBox>(noon, "ReminderCombo").SelectedIndex = 3; // 15 min before
        Assert.False(Named<StackPanel>(noon, "CustomReminderPanel").IsVisible);
        Click(Named<Button>(noon, "NextBtn"));
        Click(Named<Button>(noon, "NextBtn"));
        Assert.Equal(12, noon.ScheduleResult!.Hour);
        Assert.Equal(15, noon.ScheduleResult.ReminderMinutes);

        var midnight = new ScheduleWizardDialog(new ScheduleEntry { Hour = 0, Minute = 0, Days = [DayOfWeek.Tuesday] }, use12Hour: true);
        midnight.Show();
        Assert.Equal("12", Named<ComboBox>(midnight, "HourCombo").SelectedItem);
        Assert.Equal("AM", Named<Button>(midnight, "AmPmButton").Content);
        // Days chosen, Back, Next again: the step shows what was committed.
        Click(Named<Button>(midnight, "NextBtn"));
        Named<ToggleButton>(midnight, "WedBtn").IsChecked = true;
        Click(Named<Button>(midnight, "NextBtn"));
        Click(Named<Button>(midnight, "BackBtn"));
        Assert.True(Named<ToggleButton>(midnight, "WedBtn").IsChecked);
        Assert.True(Named<ToggleButton>(midnight, "TueBtn").IsChecked);
        Key(midnight, Avalonia.Input.Key.Escape);
        Assert.False(midnight.Result);
        Assert.Null(midnight.ScheduleResult);
    }

    [AvaloniaFact]
    public void Clone_EnterAdvances_ClearAndACancelledCapture()
    {
        int asked = 0;
        var dialog = Clone(new DeviceProfile { Name = "Work", Mode = ProfileMode.Playback }, _ => { asked++; return null; });
        Click(Named<Button>(dialog, "SetHotkeyBtn"));
        Assert.Equal(1, asked);
        Assert.Equal("Not set", Named<TextBlock>(dialog, "HotkeyLabel").Text); // (cancelled: unchanged)
        Click(Named<Button>(dialog, "ClearHotkeyBtn"));
        Assert.Equal("Not set", Named<TextBlock>(dialog, "HotkeyLabel").Text);
        Key(dialog, Avalonia.Input.Key.Enter); // the default button: Next
        Assert.Equal(1, dialog.Step);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task Sound_ACancelledPicker_AFailedTestPlay_AndNoLibraryFolder()
    {
        var dialog = new SwitchSoundDialog(true, "Click", null, 50, null, "");
        dialog.Show();
        Click(Named<ToggleButton>(dialog, "ToneCustom"));
        dialog.PickWav = () => Task.FromResult<string?>(null);
        var browse = ButtonNamed(dialog, "Browse…");
        Click(browse);
        await Until(() => browse.IsEnabled);
        Assert.Equal("", Named<TextBox>(dialog, "CustomPathBox").Text);

        // A picked file with no library folder: used, not kept.
        dialog.PickWav = () => Task.FromResult<string?>("/tmp/door.wav");
        Click(browse);
        await Until(() => Named<TextBox>(dialog, "CustomPathBox").Text == "/tmp/door.wav");
        Assert.Empty(dialog.LibraryItems);

        dialog.TestPlay = (_, _, _) => throw new InvalidDataException("not a 16-bit PCM wav");
        var test = Named<Button>(dialog, "TestBtn");
        Click(test);
        await Until(() => test.IsEnabled);
        Assert.True(test.IsEnabled);
        Assert.True(dialog.IsVisible);
        dialog.Close();
    }

    [AvaloniaFact]
    public void TheDialogService_PassesTheLibraries_AndReturnsCloneAndSound()
    {
        var owner = new Window();
        owner.Show();
        var config = new FakeConfigService();
        IDialogService service = new DialogService(() => owner) { Config = config };

        Dispatcher.UIThread.Post(() =>
        {
            var clone = owner.OwnedWindows.OfType<CloneProfileDialog>().Single();
            for (int i = 0; i < 3; i++) Click(Named<Button>(clone, "NextBtn"));
        }, DispatcherPriority.Background);
        var copy = service.ShowCloneWizard(new DeviceProfile { Name = "Work" }, [SettingsCatalog.Speakers], [], false, h => h);
        Assert.Equal("Work (copy)", copy!.Name);

        ClickLater(owner, "Save");
        var sound = service.ShowSoundWizard(true, "Bell", null, 40, showBanner: true);
        Assert.Equal("Bell", sound!.Tone);
        Assert.Equal(40, sound.Volume);
        Assert.True(sound.ShowBanner);

        ClickLater(owner, "Cancel");
        Assert.Null(service.ShowSoundWizard(true, "Bell", null, 40));
        owner.Close();
    }
}
