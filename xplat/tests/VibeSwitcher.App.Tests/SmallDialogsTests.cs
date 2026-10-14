using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Input;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;
using static VibeSwitcher.App.Tests.Ui;

namespace VibeSwitcher.App.Tests;

// The smaller dialogs Settings and the profile cards open: hotkey capture, profile type, device
// aliases, session log, manage schedules, mic test, supported headsets.
[Collection("Shared UI settings")] // (changes the hotkey label style and Wording.Mac)
public class SmallDialogsTests
{
    private static void Key(Window w, Key key, KeyModifiers modifiers = KeyModifiers.None, bool up = false) =>
        w.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = up ? InputElement.KeyUpEvent : InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });

    private static string Preview(Window w) => Named<TextBlock>(w, "HotkeyPreviewText").Text!;

    // ── Hotkey capture ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Keys_BecomeTheWindowsCodesTheConfigKeeps()
    {
        Assert.Equal(0x47, VirtualKeys.For(Avalonia.Input.Key.G));
        Assert.Equal(0x31, VirtualKeys.For(Avalonia.Input.Key.D1));
        Assert.Equal(0x74, VirtualKeys.For(Avalonia.Input.Key.F5));
        Assert.Equal(0x20, VirtualKeys.For(Avalonia.Input.Key.Space));
        Assert.Equal(0x22, VirtualKeys.For(Avalonia.Input.Key.PageDown)); // WPF calls it "Next"
        Assert.Equal(0x60, VirtualKeys.For(Avalonia.Input.Key.NumPad0));
        Assert.Equal(0xBA, VirtualKeys.For(Avalonia.Input.Key.OemSemicolon)); // "Oem1"
        Assert.Equal(0x2E, VirtualKeys.For(Avalonia.Input.Key.Delete));
        Assert.Equal(0, VirtualKeys.For(Avalonia.Input.Key.None));
    }

    [AvaloniaFact]
    public void HotkeyCapture_ShowsTheCombination_AndOkKeepsIt()
    {
        var old = HotkeyDefinition.LabelStyle;
        HotkeyDefinition.LabelStyle = HotkeyLabelStyle.Linux;
        try
        {
            var dialog = new HotkeyCaptureDialog(new HotkeyDefinition(), "Press any key combination to assign a shortcut for muting speakers");
            dialog.Show();
            Assert.Equal("Not set", Preview(dialog));
            Assert.Contains("muting speakers", Named<TextBlock>(dialog, "HotkeySubtitleText").Text);

            // Modifiers show as they're held…
            Key(dialog, Avalonia.Input.Key.LeftCtrl);
            Assert.Equal("Ctrl+", Preview(dialog));
            Key(dialog, Avalonia.Input.Key.LWin);
            Assert.Equal("Ctrl+Super+", Preview(dialog));
            // …then the key completes it.
            Key(dialog, Avalonia.Input.Key.M, KeyModifiers.Control | KeyModifiers.Meta);
            Assert.Equal("Ctrl+Super+M", Preview(dialog));
            Key(dialog, Avalonia.Input.Key.LWin, up: true);
            Key(dialog, Avalonia.Input.Key.LeftCtrl, up: true);
            Assert.Equal("Ctrl+Super+M", Preview(dialog));

            // Enter, Escape and Tab aren't hotkeys (they still reach OK / Cancel / focus).
            Key(dialog, Avalonia.Input.Key.Tab);
            Assert.Equal("Ctrl+Super+M", Preview(dialog));

            Click(dialog, "OK");
            Assert.True(dialog.Result);
            Assert.Equal(new HotkeyDefinition { VirtualKeyCode = 0x4D, UseCtrl = true, UseWin = true }.GetModifierFlags(),
                dialog.CapturedHotkey!.GetModifierFlags());
            Assert.Equal(0x4D, dialog.CapturedHotkey.VirtualKeyCode);
        }
        finally { HotkeyDefinition.LabelStyle = old; }
    }

    [AvaloniaFact]
    public void HotkeyCapture_ClearEmptiesIt_CancelChangesNothing_AndMacUsesItsSymbols()
    {
        var old = HotkeyDefinition.LabelStyle;
        HotkeyDefinition.LabelStyle = HotkeyLabelStyle.MacOS;
        try
        {
            var current = new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true };
            var dialog = new HotkeyCaptureDialog(current);
            dialog.Show();
            Assert.Equal("⌃⌥G", Preview(dialog));
            Key(dialog, Avalonia.Input.Key.LeftShift);
            Assert.Equal("⇧", Preview(dialog));
            Key(dialog, Avalonia.Input.Key.LeftShift, up: true);
            Assert.Equal("⌃⌥G", Preview(dialog));

            Click(dialog, "Clear");
            Assert.Equal("Not set", Preview(dialog));
            Click(dialog, "OK");
            Assert.True(dialog.CapturedHotkey!.IsEmpty);
            Assert.Equal(0x47, current.VirtualKeyCode); // (the profile's own hotkey isn't touched)

            var cancelled = new HotkeyCaptureDialog(current);
            cancelled.Show();
            Key(cancelled, Avalonia.Input.Key.K, KeyModifiers.Alt);
            Click(cancelled, "Cancel");
            Assert.False(cancelled.Result);
            Assert.Null(cancelled.CapturedHotkey);
        }
        finally { HotkeyDefinition.LabelStyle = old; }
    }

    [AvaloniaFact]
    public void HotkeyCapture_TakesSpace_BeforeAFocusedButtonCan()
    {
        var dialog = new HotkeyCaptureDialog(new HotkeyDefinition());
        dialog.Show();
        var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel");
        cancel.Focus();
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Space, Source = cancel };
        cancel.RaiseEvent(args);
        Assert.True(args.Handled);
        Assert.True(dialog.IsVisible); // Cancel wasn't pressed
        Assert.Contains("Space", Preview(dialog));
        dialog.Close();
    }

    [AvaloniaFact]
    public void HotkeyCapture_EnterIsOk_EscapeIsCancel_AndOddKeysChangeNothing()
    {
        var dialog = new HotkeyCaptureDialog(new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true });
        dialog.Show();
        var before = Preview(dialog);
        Key(dialog, Avalonia.Input.Key.ImeProcessed); // (an input method composing)
        Key(dialog, Avalonia.Input.Key.DeadCharProcessed);
        Key(dialog, Avalonia.Input.Key.None);
        Assert.Equal(before, Preview(dialog));
        Key(dialog, Avalonia.Input.Key.Enter);
        Assert.True(dialog.Result);
        Assert.Equal(0x47, dialog.CapturedHotkey!.VirtualKeyCode);

        var other = new HotkeyCaptureDialog(new HotkeyDefinition());
        other.Show();
        Key(other, Avalonia.Input.Key.Escape);
        Assert.False(other.Result);
        Assert.False(other.IsVisible);
    }

    [AvaloniaFact]
    public void HotkeyCapture_ALetterWithNoLatinKey_UsesWhereItSitsOnTheKeyboard()
    {
        // (A Cyrillic layout: the key reports no Latin letter, but sits where a US keyboard has G.)
        var dialog = new HotkeyCaptureDialog(new HotkeyDefinition());
        dialog.Show();
        dialog.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.None,
            PhysicalKey = PhysicalKey.G,
            KeyModifiers = KeyModifiers.Control,
        });
        Click(dialog, "OK");
        Assert.Equal(0x47, dialog.CapturedHotkey!.VirtualKeyCode);
        Assert.True(dialog.CapturedHotkey.UseCtrl);
    }

    [AvaloniaFact]
    public void ProfileType_IgnoresEscape_AsOnWindows()
    {
        var dialog = new ProfileTypeDialog();
        dialog.Show();
        Key(dialog, Avalonia.Input.Key.Escape);
        Assert.True(dialog.IsVisible);
        Click(dialog, "Cancel");
        Assert.False(dialog.IsVisible);
        Assert.Null(dialog.ChosenMode);
    }

    [Fact]
    public void ModifierPreview_FollowsTheLabelStyle()
    {
        var old = HotkeyDefinition.LabelStyle;
        try
        {
            HotkeyDefinition.LabelStyle = HotkeyLabelStyle.Windows;
            Assert.Equal("Ctrl+Alt+Shift+Win+", HotkeyDefinition.ModifierPreview(true, true, true, true));
            HotkeyDefinition.LabelStyle = HotkeyLabelStyle.Linux;
            Assert.Equal("Alt+Super+", HotkeyDefinition.ModifierPreview(false, true, false, true));
            HotkeyDefinition.LabelStyle = HotkeyLabelStyle.MacOS;
            Assert.Equal("⌃⇧⌘", HotkeyDefinition.ModifierPreview(true, false, true, true));
        }
        finally { HotkeyDefinition.LabelStyle = old; }
    }

    [AvaloniaFact]
    public void SettingsShortcuts_NowAskThroughTheCaptureDialog()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = setup.Window();
        window.Show();
        SettingsCatalog.WaitForDevices(window);
        Dispatcher.UIThread.Post(() =>
        {
            var dialog = window.OwnedWindows.OfType<HotkeyCaptureDialog>().Single();
            Assert.Contains("opening Settings", Named<TextBlock>(dialog, "HotkeySubtitleText").Text);
            Key(dialog, Avalonia.Input.Key.V, KeyModifiers.Control | KeyModifiers.Shift);
            Click(dialog, "OK");
        }, DispatcherPriority.Background);
        Click(Named<Button>(window, "SettingsHotkeySetBtn"));
        Assert.Equal(0x56, setup.Config.Current.SettingsHotkey!.VirtualKeyCode);
        Assert.True(setup.Config.Current.SettingsHotkey.UseShift);
        window.Close();
    }

    // ── Profile type ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ProfileType_ReturnsTheChoice_OrNothing()
    {
        var owner = new Window();
        owner.Show();
        IDialogService service = new DialogService(() => owner);
        Dispatcher.UIThread.Post(() => Click(Named<Button>(owner.OwnedWindows.Single(), "RecordingBtn")), DispatcherPriority.Background);
        Assert.Equal(ProfileMode.Recording, service.ShowProfileTypeDialog());
        ClickLater(owner, "Cancel");
        Assert.Null(service.ShowProfileTypeDialog());
        owner.Close();
    }

    // ── Device aliases ──────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void DeviceAliases_ListUsedDevicesFirst_PerTab_AndSaveKeepsTheLastEdit()
    {
        var aliases = DialogCatalog.Aliases();
        var renamed = new List<(string, string)>();
        // (The shared view model saves an alias when it changes.)
        var dialog = new DeviceAliasesDialog(aliases);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "Arctis Nova 7 Headset", "LG HDR 4K (HDMI)", "Speakers (Built-in Audio)" },
            dialog.PlaybackDevices.Select(a => a.RawName));
        Assert.True(Named<ItemsControl>(dialog, "PlaybackList").IsVisible);
        Assert.False(Named<ItemsControl>(dialog, "RecordingList").IsVisible);
        Assert.Contains("on", Named<Button>(dialog, "PlaybackTabBtn").Classes);

        var texts = Texts(Named<ItemsControl>(dialog, "PlaybackList")).ToList();
        Assert.Contains("Disconnected", texts);
        Assert.Contains("Not used in any profile", texts); // (the speakers)

        Click(Named<Button>(dialog, "RecordingTabBtn"));
        Assert.True(Named<ItemsControl>(dialog, "RecordingList").IsVisible);
        Assert.False(Named<ItemsControl>(dialog, "PlaybackList").IsVisible);
        Assert.Contains("on", Named<Button>(dialog, "RecordingTabBtn").Classes);

        // Typing into a field and pressing Save keeps it, though the field never lost focus.
        var box = Named<ItemsControl>(dialog, "RecordingList").GetVisualDescendants().OfType<TextBox>().Single();
        box.Focus();
        box.Text = "  Headset Mic ";
        Assert.Equal("", aliases.Single(a => a.DeviceId == "mic").Alias); // (not yet: only on losing focus)
        Click(dialog, "Save");
        Assert.Equal("Headset Mic", aliases.Single(a => a.DeviceId == "mic").Alias);
        Assert.False(dialog.IsVisible);
    }

    // ── Session log ─────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task SessionLog_ListsTheErrors_AndSaysWhenThereIsNoLogFileYet()
    {
        var log = new SessionLogWindow(new FakeAppLogger(), new DialogCatalog.FixedErrors(),
            Path.Combine(Path.GetTempPath(), $"vs-nolog-{Guid.NewGuid():N}", "error.log"));
        log.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(Named<ItemsControl>(log, "ErrorList")).ToList();
        Assert.Contains("09:41:07", texts);
        Assert.Contains("VS-003", texts);
        Assert.Contains("Hotkey Conflict: Could not register 'Ctrl+Alt+G' — another app is using it.", texts);
        Assert.True(log.CanResize);

        string? alert = null;
        ClickLater(log, "OK", d => alert = string.Join(" ", Texts(d)));
        Click(log, "Open Log File");
        await Until(() => alert != null);
        Assert.Contains("No log file has been written yet.", alert);
        log.Close();
    }

    [AvaloniaFact]
    public void TheSettingsErrorsButton_OpensTheSessionLog()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Errors.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict", "Taken.");
        var window = setup.Window();
        window.Show();
        SettingsCatalog.WaitForDevices(window);
        ClickLater(window, "Close", d => Assert.IsType<SessionLogWindow>(d));
        Click(Named<Button>(window, "LogsButton"));
        Assert.Empty(window.OwnedWindows);
        window.Close();
    }

    // ── Manage schedules ────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ManageSchedules_ListsThem_AndClosesWhenTheLastIsRemoved()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.Profiles[0].Schedules.Add(new ScheduleEntry { Hour = 9, Minute = 30, Days = [DayOfWeek.Monday] });
        var window = setup.Window();
        window.Show();
        SettingsCatalog.WaitForDevices(window);
        var card = window.ViewModel.Profiles.Single(p => p.Name == "Gaming");
        Assert.Equal(2, card.Schedules.Count);

        var manage = new ManageSchedulesDialog(card);
        manage.Show();
        Dispatcher.UIThread.RunJobs();
        var rows = Named<ItemsControl>(manage, "ScheduleList");
        Assert.Equal(2, rows.ItemCount);
        Assert.Contains("Gaming", Texts(manage));

        // The row's own Remove button (bound to the card's command); the shared view model asks first.
        var removeButtons = rows.GetVisualDescendants().OfType<Button>().Where(b => b.Content as string == "Remove").ToList();
        Assert.Equal(2, removeButtons.Count);
        Assert.All(rows.GetVisualDescendants().OfType<Button>(), b => Assert.NotNull(b.Command));
        ClickLater(window, "Remove", d => Assert.IsType<ConfirmDialog>(d));
        Click(removeButtons[0]);
        Assert.Single(card.Schedules);
        Assert.True(manage.IsVisible);
        Assert.Equal(1, rows.ItemCount);

        ClickLater(window, "Remove");
        card.DeleteScheduleEntryCommand.Execute(card.Schedules[0]);
        Assert.Empty(card.Schedules);
        Assert.False(manage.IsVisible); // nothing left to manage
        window.Close();
    }

    // ── Mic test ────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task MicTest_ShowsTheLevelAndPeak_ThenClosesItself()
    {
        var dialog = new MicTestDialog("mic", "Arctis Nova 7 Microphone", new DialogCatalog.SteadyMic(0.035f), null)
        {
            Second = TimeSpan.FromMilliseconds(20),
        };
        dialog.Show();
        await Until(() => Named<TextBlock>(dialog, "LevelText").Text == "35%");
        Assert.Equal("Peak: 35%", Named<TextBlock>(dialog, "PeakText").Text);
        Assert.Equal(0.35, Named<ProgressBar>(dialog, "LevelBar").Value, 3);
        Assert.Equal("Arctis Nova 7 Microphone", Named<TextBlock>(dialog, "DeviceNameText").Text);

        dialog.ShowLevel(0.025f); // quieter: the level drops, the peak stays
        Assert.Equal("25%", Named<TextBlock>(dialog, "LevelText").Text);
        Assert.Equal("Peak: 35%", Named<TextBlock>(dialog, "PeakText").Text);
        dialog.ShowLevel(0.5f); // (capped at 100%)
        Assert.Equal("100%", Named<TextBlock>(dialog, "LevelText").Text);

        await Until(() => !dialog.IsVisible); // five "seconds" later
        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public async Task MicTest_WhenTheMicrophoneCantOpen_SaysSoAndStaysOpen()
    {
        var dialog = new MicTestDialog("mic", "USB Mic", audio: null, logger: null) { Second = TimeSpan.FromMilliseconds(20) };
        dialog.Show();
        await Until(() => Named<TextBlock>(dialog, "CaptureError").IsVisible);
        Assert.True(Named<TextBlock>(dialog, "CaptureError").IsVisible);
        Assert.False(Named<ProgressBar>(dialog, "LevelBar").IsVisible);
        Assert.False(Named<TextBlock>(dialog, "SpeakPrompt").IsVisible);
        Assert.Equal("", Named<TextBlock>(dialog, "CountdownText").Text);
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.IsVisible);
        dialog.Close();
    }

    // ── Supported headsets ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SupportedHeadsets_ListsTheBrands_InTheSystemsWords()
    {
        var groups = SupportedHeadsetsDialog.BuildGroups();
        Assert.Equal(new[] { "Logitech", "Corsair", "SteelSeries", "HyperX" }, groups.Select(g => g.Name));
        Assert.True(groups[0].Tested);
        Assert.All(groups, g => Assert.NotEmpty(g.ModelNames));
        Assert.Equal(KnownHidHeadsets.All.Select(h => h.ModelName).Distinct().Count(), groups.Sum(g => g.ModelNames.Count));

        var old = Wording.Mac;
        Wording.Mac = true;
        try
        {
            var dialog = new SupportedHeadsetsDialog();
            dialog.Show();
            var texts = Texts(dialog).ToList();
            Assert.Contains(texts, t => t?.Contains("macOS notifies VibeSwitcher") == true);
            Assert.DoesNotContain(texts, t => t?.Contains("Windows") == true);
            Assert.Contains("Tested ✅", texts);
            Click(dialog, "Enable Auto-Switch");
            Assert.True(dialog.Result);
        }
        finally { Wording.Mac = old; }
        Assert.True(SystemOpener.IsWebUrl(SupportedHeadsetsDialog.RequestUrl));
    }
}
