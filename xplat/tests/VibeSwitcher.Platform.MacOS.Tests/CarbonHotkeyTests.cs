using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.MacOS.Hotkeys;

namespace VibeSwitcher.Platform.MacOS.Tests;

public class MacKeyCodeTests
{
    [Theory]
    [InlineData(0x41, 0x00u)] // A
    [InlineData(0x47, 0x05u)] // G
    [InlineData(0x31, 0x12u)] // 1
    [InlineData(0x30, 0x1Du)] // 0
    [InlineData(0x70, 0x7Au)] // F1
    [InlineData(0x7B, 0x6Fu)] // F12
    [InlineData(0x20, 0x31u)] // Space
    [InlineData(0x25, 0x7Bu)] // Left
    [InlineData(0xBA, 0x29u)] // ;
    [InlineData(0x60, 0x52u)] // numpad 0
    public void WindowsKeyToMacKeyCode(int vk, uint code) => Assert.Equal(code, MacKeyCodes.FromVirtualKey(vk));

    [Fact]
    public void AllLettersDigitsAndF1ToF20_AreMapped_ToDistinctKeys()
    {
        var keys = Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x70, 20)).ToList();
        Assert.All(keys, vk => Assert.NotNull(MacKeyCodes.FromVirtualKey(vk)));
        Assert.Equal(MacKeyCodes.All.Count, MacKeyCodes.All.Values.Distinct().Count()); // no two Windows keys share a Mac key
    }

    [Theory]
    [InlineData(0xAD)] // Volume Mute (macOS keeps media keys for itself)
    [InlineData(0x2C)] // Print Screen (no such key)
    [InlineData(0x84)] // F21
    public void KeysAMacCantUse_AreUnsupported(int vk) => Assert.Null(MacKeyCodes.FromVirtualKey(vk));

    [Fact]
    public void OptionOnly_Detection()
    {
        Assert.True(CarbonKeyGrabber.IsOptionOnly(CarbonKeyGrabber.OptionKey));
        Assert.True(CarbonKeyGrabber.IsOptionOnly(CarbonKeyGrabber.OptionKey | CarbonKeyGrabber.ShiftKey));
        Assert.False(CarbonKeyGrabber.IsOptionOnly(CarbonKeyGrabber.OptionKey | CarbonKeyGrabber.ControlKey));
        Assert.False(CarbonKeyGrabber.IsOptionOnly(CarbonKeyGrabber.OptionKey | CarbonKeyGrabber.CmdKey));
        Assert.False(CarbonKeyGrabber.IsOptionOnly(CarbonKeyGrabber.ShiftKey));
    }

    [Fact]
    public void Modifiers_WinIsCommand_AltIsOption()
    {
        Assert.Equal(CarbonKeyGrabber.ControlKey | CarbonKeyGrabber.OptionKey,
            CarbonKeyGrabber.Modifiers(HotkeyDefinition.MOD_CTRL | HotkeyDefinition.MOD_ALT));
        Assert.Equal(CarbonKeyGrabber.CmdKey | CarbonKeyGrabber.ShiftKey,
            CarbonKeyGrabber.Modifiers(HotkeyDefinition.MOD_WIN | HotkeyDefinition.MOD_SHIFT));
    }
}

// Real Carbon hotkeys on a Mac (CI). A physical key press can't be simulated there without the
// Accessibility permission, so delivery is tested by sending Carbon the very event a key press
// produces, through the installed handler.
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("VS_MAC_IT") != "1")
            Skip = "Needs a Mac (VS_MAC_IT=1, set by CI).";
    }
}

[Trait("Category", "MacOS")]
public sealed class CarbonKeyGrabberTests
{
    private const int Ctrl = HotkeyDefinition.MOD_CTRL, Alt = HotkeyDefinition.MOD_ALT, Shift = HotkeyDefinition.MOD_SHIFT;

    [MacFact]
    public void Register_Unregister_RegisterAgain()
    {
        using var g = new CarbonKeyGrabber();
        Assert.Equal(GrabResult.Ok, g.Register(1, Ctrl | Alt | Shift, 0x47, out var error)); // ⌃⌥⇧G
        Assert.Null(error);
        g.Unregister(1);
        Assert.Equal(GrabResult.Ok, g.Register(1, Ctrl | Alt | Shift, 0x47, out _));
    }

    [MacFact]
    public void ShortcutAlreadyTaken_IsInUse_LikeWindows()
    {
        using var first = new CarbonKeyGrabber();
        using var second = new CarbonKeyGrabber();
        Assert.Equal(GrabResult.Ok, first.Register(1, Ctrl | Alt | Shift, 0x48, out _));
        Assert.Equal(GrabResult.InUse, second.Register(1, Ctrl | Alt | Shift, 0x48, out _));
        Assert.Equal(GrabResult.InUse, first.Register(2, Ctrl | Alt | Shift, 0x48, out _)); // same app, twice
        first.Dispose(); // releases it
        Assert.Equal(GrabResult.Ok, second.Register(1, Ctrl | Alt | Shift, 0x48, out _));
    }

    [MacFact]
    public void APress_ReachesTheRightGrabberAndId()
    {
        using var a = new CarbonKeyGrabber();
        using var b = new CarbonKeyGrabber();
        var got = new List<string>();
        a.Pressed += id => { lock (got) got.Add($"a{id}"); };
        b.Pressed += id => { lock (got) got.Add($"b{id}"); };
        Assert.Equal(GrabResult.Ok, a.Register(5, Ctrl | Alt | Shift, 0x4A, out _));
        Assert.Equal(GrabResult.Ok, b.Register(5, Ctrl | Alt | Shift, 0x4B, out _));

        Assert.Equal(0, a.SimulatePress(5));
        Assert.Equal(0, b.SimulatePress(5));
        lock (got) Assert.Equal(["a5", "b5"], got);
    }

    [MacFact]
    public void OptionOnlyShortcuts_AreRefusedClearly_OnMacOS15()
    {
        using var g = new CarbonKeyGrabber();
        var result = g.Register(1, Alt, 0x47, out var error); // ⌥G
        if (OperatingSystem.IsMacOSVersionAtLeast(15))
        {
            Assert.Equal(GrabResult.Unsupported, result);
            Assert.Contains("Option", error);
        }
        Assert.Equal(GrabResult.Ok, g.Register(2, Ctrl | Alt, 0x47, out _)); // ⌃⌥G is fine
    }

    [MacFact]
    public void AfterDispose_RegisterFails_NothingLeaks()
    {
        var g = new CarbonKeyGrabber();
        g.Dispose();
        Assert.Equal(GrabResult.Failed, g.Register(1, Ctrl | Alt | Shift, 0x4C, out _));
        using var other = new CarbonKeyGrabber();
        Assert.Equal(GrabResult.Ok, other.Register(1, Ctrl | Alt | Shift, 0x4C, out _)); // still free
    }

    [MacFact]
    public void UnsupportedKey_IsReported()
    {
        using var g = new CarbonKeyGrabber();
        Assert.Equal(GrabResult.Unsupported, g.Register(1, Ctrl, 0xAD, out var error));
        Assert.NotNull(error);
    }

    [MacFact]
    public void ThroughTheHotkeyService()
    {
        using var g = new CarbonKeyGrabber();
        using var svc = new VibeSwitcher.Services.HotkeyService(g, new VibeSwitcher.Tests.FakeAppLogger(), new VibeSwitcher.Tests.FakeSessionErrorTracker());
        var a = new DeviceProfile { Name = "A", Hotkey = new HotkeyDefinition { VirtualKeyCode = 0x31, UseCtrl = true, UseAlt = true, UseShift = true } };
        var b = new DeviceProfile { Name = "B", Hotkey = new HotkeyDefinition { VirtualKeyCode = 0x31, UseCtrl = true, UseAlt = true, UseShift = true } };
        Assert.Single(svc.RegisterAll([a, b])); // the duplicate is a conflict
        Assert.False(svc.TestHotkey(a.Hotkey)); // ours, so not "taken"
        using var otherApp = new CarbonKeyGrabber();
        Assert.Equal(GrabResult.Ok, otherApp.Register(9, Ctrl | Alt | Shift, 0x32, out _));
        Assert.True(svc.TestHotkey(new HotkeyDefinition { VirtualKeyCode = 0x32, UseCtrl = true, UseAlt = true, UseShift = true }));
    }
}
