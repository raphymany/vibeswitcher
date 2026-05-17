using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Tests;

public class HotkeyDefinitionTests
{
    [Fact]
    public void IsEmpty_WhenVkZero_ReturnsTrue()
    {
        Assert.True(new HotkeyDefinition().IsEmpty);
    }

    [Fact]
    public void IsEmpty_WhenVkNonZero_ReturnsFalse()
    {
        Assert.False(new HotkeyDefinition { VirtualKeyCode = 33 }.IsEmpty);
    }

    [Fact]
    public void IsValid_WhenVkOne_ReturnsTrue()
    {
        Assert.True(new HotkeyDefinition { VirtualKeyCode = 1 }.IsValid);
    }

    [Fact]
    public void IsValid_WhenVk254_ReturnsTrue()
    {
        Assert.True(new HotkeyDefinition { VirtualKeyCode = 254 }.IsValid);
    }

    [Fact]
    public void IsValid_WhenVkZero_ReturnsFalse()
    {
        Assert.False(new HotkeyDefinition { VirtualKeyCode = 0 }.IsValid);
    }

    [Fact]
    public void IsValid_WhenVk255_ReturnsFalse()
    {
        Assert.False(new HotkeyDefinition { VirtualKeyCode = 255 }.IsValid);
    }

    [Fact]
    public void GetModifierFlags_NoModifiers_ReturnsZero()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33 };
        Assert.Equal(0, hk.GetModifierFlags());
    }

    [Fact]
    public void GetModifierFlags_CtrlOnly_ReturnsCtrlBit()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        Assert.Equal(WinApi.MOD_CTRL, hk.GetModifierFlags());
    }

    [Fact]
    public void GetModifierFlags_AltOnly_ReturnsAltBit()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseAlt = true };
        Assert.Equal(WinApi.MOD_ALT, hk.GetModifierFlags());
    }

    [Fact]
    public void GetModifierFlags_ShiftOnly_ReturnsShiftBit()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseShift = true };
        Assert.Equal(WinApi.MOD_SHIFT, hk.GetModifierFlags());
    }

    [Fact]
    public void GetModifierFlags_WinOnly_ReturnsWinBit()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseWin = true };
        Assert.Equal(WinApi.MOD_WIN, hk.GetModifierFlags());
    }

    [Fact]
    public void GetModifierFlags_AllModifiers_ReturnsCombinedBitmask()
    {
        var hk = new HotkeyDefinition
        {
            VirtualKeyCode = 33,
            UseAlt = true, UseCtrl = true, UseShift = true, UseWin = true
        };
        var expected = WinApi.MOD_ALT | WinApi.MOD_CTRL | WinApi.MOD_SHIFT | WinApi.MOD_WIN;
        Assert.Equal(expected, hk.GetModifierFlags());
    }

    [Fact]
    public void ToDisplayString_WhenEmpty_ReturnsNone()
    {
        Assert.Equal("(none)", new HotkeyDefinition().ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_WithCtrl_ContainsCtrl()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        Assert.Contains("Ctrl", hk.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_WithAlt_ContainsAlt()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseAlt = true };
        Assert.Contains("Alt", hk.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_WithShift_ContainsShift()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseShift = true };
        Assert.Contains("Shift", hk.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_NonEmpty_ContainsPlusSeparator()
    {
        var hk = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        Assert.Contains("+", hk.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_UnknownVk_IncludesVkNumberFallback()
    {
        // VK 200 is not mapped to a named Key — should fall back to "VK(200)" format
        var hk = new HotkeyDefinition { VirtualKeyCode = 200 };
        var display = hk.ToDisplayString();
        Assert.Contains("200", display);
    }
}
