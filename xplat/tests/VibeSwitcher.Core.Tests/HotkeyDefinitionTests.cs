using VibeSwitcher.Keys;
using VibeSwitcher.Models;

namespace VibeSwitcher.Tests;

// FORK of VibeSwitcher.Tests/HotkeyDefinitionTests.cs (which asserts against Win32 WinApi.MOD_*),
// plus coverage for the per-OS modifier labels. Tracked in xplat/forks.txt.
public class HotkeyDefinitionTests : IDisposable
{
    public HotkeyDefinitionTests() => HotkeyDefinition.LabelStyle = HotkeyLabelStyle.Windows;
    public void Dispose() => HotkeyDefinition.LabelStyle = HotkeyLabelStyle.Windows;

    [Fact]
    public void IsEmpty_WhenVkZero_ReturnsTrue() => Assert.True(new HotkeyDefinition().IsEmpty);

    [Fact]
    public void IsEmpty_WhenVkNonZero_ReturnsFalse() => Assert.False(new HotkeyDefinition { VirtualKeyCode = 33 }.IsEmpty);

    [Theory]
    [InlineData(1, true)]
    [InlineData(254, true)]
    [InlineData(0, false)]
    [InlineData(255, false)]
    public void IsValid_MatchesWindowsRange(int vk, bool expected) =>
        Assert.Equal(expected, new HotkeyDefinition { VirtualKeyCode = vk }.IsValid);

    [Fact]
    public void ModifierConstants_MatchWin32Values()
    {
        // Same bits as Win32 MOD_ALT/CONTROL/SHIFT/WIN (WinApi.MOD_* in the Windows app).
        Assert.Equal(0x1, HotkeyDefinition.MOD_ALT);
        Assert.Equal(0x2, HotkeyDefinition.MOD_CTRL);
        Assert.Equal(0x4, HotkeyDefinition.MOD_SHIFT);
        Assert.Equal(0x8, HotkeyDefinition.MOD_WIN);
    }

    [Fact]
    public void GetModifierFlags_NoModifiers_ReturnsZero() =>
        Assert.Equal(0, new HotkeyDefinition { VirtualKeyCode = 33 }.GetModifierFlags());

    [Fact]
    public void GetModifierFlags_AllModifiers_ReturnsCombinedBitmask()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseAlt = true, UseCtrl = true, UseShift = true, UseWin = true };
        Assert.Equal(0xF, hk.GetModifierFlags());
    }

    [Fact]
    public void Matches_ComparesKeyAndEveryModifier()
    {
        var a = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        Assert.True(a.Matches(new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true }));
        Assert.False(a.Matches(new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true, UseWin = true }));
        Assert.False(a.Matches(new HotkeyDefinition { VirtualKeyCode = 34, UseCtrl = true }));
    }

    [Fact]
    public void ToDisplayString_WhenEmpty_ReturnsNotSet() => Assert.Equal("Not set", new HotkeyDefinition().ToDisplayString());

    // Exact strings the Windows app shows (WPF Key names, including its quirks).
    [Theory]
    [InlineData(33, false, false, false, false, "PageUp")]
    [InlineData(34, false, false, false, false, "Next")]
    [InlineData(49, true, false, false, false, "Ctrl+D1")]
    [InlineData(120, true, true, false, false, "Ctrl+Alt+F9")]
    [InlineData(65, true, true, true, true, "Ctrl+Alt+Shift+Win+A")]
    [InlineData(186, false, false, true, false, "Shift+Oem1")]
    [InlineData(200, false, false, false, false, "VK(200)")]
    public void ToDisplayString_WindowsStyle_MatchesWindowsApp(int vk, bool ctrl, bool alt, bool shift, bool win, string expected)
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = vk, UseCtrl = ctrl, UseAlt = alt, UseShift = shift, UseWin = win };
        Assert.Equal(expected, hk.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_MacStyle_UsesGlyphsInAppleOrder()
    {
        HotkeyDefinition.LabelStyle = HotkeyLabelStyle.MacOS;
        var hk = new HotkeyDefinition { VirtualKeyCode = 65, UseWin = true, UseShift = true, UseAlt = true, UseCtrl = true };
        Assert.Equal("⌃⌥⇧⌘A", hk.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_LinuxStyle_CallsWinKeySuper()
    {
        HotkeyDefinition.LabelStyle = HotkeyLabelStyle.Linux;
        var hk = new HotkeyDefinition { VirtualKeyCode = 120, UseCtrl = true, UseWin = true };
        Assert.Equal("Ctrl+Super+F9", hk.ToDisplayString());
    }

    [Fact]
    public void VirtualKeyMap_CoversEveryKeyWpfNames() => Assert.Equal(172, VirtualKeyMap.Count);

    [Fact]
    public void ConflictException_MessageUsesDisplayString()
    {
        var ex = new Services.HotkeyConflictException(new HotkeyDefinition { VirtualKeyCode = 120, UseCtrl = true });
        Assert.Equal("Hotkey conflict: Ctrl+F9", ex.Message);
    }

    [Fact]
    public void JsonShape_IsUnchanged()
    {
        // configs move between Windows and Mac/Linux, so the serialized form must not gain or lose fields.
        var json = System.Text.Json.JsonSerializer.Serialize(new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true });
        Assert.Equal("{\"VirtualKeyCode\":33,\"UseAlt\":false,\"UseCtrl\":true,\"UseShift\":false,\"UseWin\":false,\"IsEmpty\":false,\"IsValid\":true}", json);
    }
}
