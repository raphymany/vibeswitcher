using VibeSwitcher.Models;
using VibeSwitcher.Services;
using Xunit;

namespace VibeSwitcher.Tests;

// HotkeyService requires a real Win32 HWND to call RegisterHotKey. These tests use
// IntPtr.Zero and only exercise paths that return early before touching WinAPI —
// specifically profiles with empty/invalid hotkeys and atom-free lookups.
public class HotkeyServiceTests : IDisposable
{
    private readonly HotkeyService _svc = new(IntPtr.Zero, new FakeAppLogger(), new FakeSessionErrorTracker());

    public void Dispose() => _svc.Dispose();

    [Fact]
    public void RegisterAll_EmptyList_ReturnsNoConflicts()
    {
        var conflicts = _svc.RegisterAll([]);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void RegisterAll_ProfileWithEmptyHotkey_ReturnsNoConflicts()
    {
        var profile = new DeviceProfile { Hotkey = new HotkeyDefinition() };
        var conflicts = _svc.RegisterAll([profile]);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void RegisterAll_ProfileWithVkCodeZero_ReturnsNoConflicts()
    {
        var profile = new DeviceProfile { Hotkey = new HotkeyDefinition { VirtualKeyCode = 0 } };
        var conflicts = _svc.RegisterAll([profile]);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void HandleHotkey_UnknownAtom_ReturnsNull()
    {
        _svc.RegisterAll([]);
        var result = _svc.HandleHotkey(9999);
        Assert.Null(result);
    }

    [Fact]
    public void UnregisterProfile_UnknownId_DoesNotThrow()
    {
        _svc.UnregisterProfile(Guid.NewGuid());
    }

    [Fact]
    public void RegisterProfile_WithEmptyHotkey_DoesNotThrow()
    {
        var profile = new DeviceProfile { Hotkey = new HotkeyDefinition() };
        _svc.RegisterProfile(profile);
    }

    [Fact]
    public void UnregisterAll_AfterRegisterAll_DoesNotThrow()
    {
        var profile = new DeviceProfile { Hotkey = new HotkeyDefinition() };
        _svc.RegisterAll([profile]);
        _svc.UnregisterAll();
    }
}
