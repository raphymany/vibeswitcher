using VibeSwitcher.App.Hosting;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Platform.Linux.Hotkeys; // (PortalShortcutState)

namespace VibeSwitcher.App.Tests;

// How global shortcuts work on a desktop, in Settings › Shortcuts' words, and the remembered GNOME choice.
public class ShortcutsWayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vs-xplat-").FullName;
    private const string GnomeConsent = "GNOME can keep the shortcuts once you allow it.";
    private const string NoShortcuts = "This desktop doesn't let apps set global shortcuts.";

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    [Fact]
    public void TheGnomeChoice_IsKeptBesideTheSharedConfig()
    {
        Assert.False(XPlatSettings.Load(_dir).GnomeShortcutsAllowed); // (nothing saved yet)
        new XPlatSettings(GnomeShortcutsAllowed: true).Save(_dir);
        Assert.True(XPlatSettings.Load(_dir).GnomeShortcutsAllowed);
        Assert.Equal([XPlatSettings.FileName], Directory.GetFiles(_dir).Select(Path.GetFileName)); // (no temp file left)
        Assert.Contains("\"gnomeShortcutsAllowed\": true", File.ReadAllText(Path.Combine(_dir, XPlatSettings.FileName)));

        // Unreadable: as if never chosen (never allowed by mistake).
        File.WriteAllText(Path.Combine(_dir, XPlatSettings.FileName), "{ not json");
        Assert.False(XPlatSettings.Load(_dir).GnomeShortcutsAllowed);
        File.WriteAllText(Path.Combine(_dir, XPlatSettings.FileName), "{ \"gnomeShortcutsAllowed\": \"yes\" }");
        Assert.False(XPlatSettings.Load(_dir).GnomeShortcutsAllowed);
    }

    [Fact]
    public void WhereTheSystemGrabsKeys_TheresNothingToExplain()
    {
        Assert.Null(Wording.Shortcuts(ShortcutsWay.Native, null));
    }

    [Fact]
    public void GnomeShortcuts_OfferAllowThenStop()
    {
        var ask = Wording.Shortcuts(ShortcutsWay.GnomeNeedsConsent, GnomeConsent)!;
        Assert.StartsWith(GnomeConsent, ask.Note);
        Assert.Contains("on each profile) are saved but don't work", ask.Note);
        Assert.Equal((GnomeChoice.Allow, "Add my shortcuts to GNOME"), (ask.Choice, ask.ChoiceText));

        var on = Wording.Shortcuts(ShortcutsWay.GnomeShortcuts, null)!;
        Assert.Contains("Settings › Keyboard › Custom Shortcuts", on.Note);
        Assert.Equal((GnomeChoice.Stop, "Remove my shortcuts from GNOME"), (on.Choice, on.ChoiceText));
    }

    [Fact]
    public void TheDesktopsService_SaysWhereItsUpTo_AndWhichKeysItGave()
    {
        string Note(PortalShortcutState state) => Wording.Shortcuts(ShortcutsWay.DesktopService, null, state)!.Note;
        Assert.Contains("approve them now", Note(PortalShortcutState.WaitingForUser));
        Assert.Contains("turned down", Note(PortalShortcutState.Declined));
        Assert.Contains("session log", Note(PortalShortcutState.Failed));
        Assert.Contains("asks you to approve it", Note(PortalShortcutState.NotBound));

        var bound = Wording.Shortcuts(ShortcutsWay.DesktopService, null, PortalShortcutState.Bound,
            [("Open VibeSwitcher settings", "Super+S"), ("Switch to Gaming", "Ctrl+Alt+G")])!;
        Assert.Equal(["Open VibeSwitcher settings: Super+S", "Switch to Gaming: Ctrl+Alt+G"], bound.Keys);
        Assert.Equal(GnomeChoice.None, bound.Choice);
        Assert.Null(bound.ChoiceText);
    }

    [Fact]
    public void WithNoWay_TheReasonIsGiven()
    {
        var off = Wording.Shortcuts(ShortcutsWay.Off, NoShortcuts)!;
        Assert.Equal(NoShortcuts, off.Note);
        Assert.Empty(off.Keys);
        Assert.Equal(GnomeChoice.None, off.Choice);
    }
}
