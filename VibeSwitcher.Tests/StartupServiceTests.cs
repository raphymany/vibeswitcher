using Microsoft.Win32;
using VibeSwitcher.Services;
using Xunit;

namespace VibeSwitcher.Tests;

public class StartupServiceTests : IDisposable
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeSwitcher";

    private readonly StartupService _svc = new(new FakeAppLogger(), new FakeSessionErrorTracker());
    private readonly string? _savedRegistryValue;

    public StartupServiceTests()
    {
        // Capture the exact raw value that was in the registry before the test runs.
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        _savedRegistryValue = key?.GetValue(ValueName) as string;

        // Start each test from a known disabled state so tests don't affect each other.
        _svc.Disable();
    }

    public void Dispose()
    {
        // Restore the registry to exactly what it was before the test ran.
        // Avoids writing the test runner's exe path via Enable() which uses ProcessPath.
        if (_savedRegistryValue != null)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);
            key.SetValue(ValueName, _savedRegistryValue);
        }
        else
        {
            _svc.Disable();
        }
    }

    [Fact]
    public void Enable_IsStartupEnabled_ReturnsTrue()
    {
        _svc.Enable();
        Assert.True(_svc.IsStartupEnabled());
    }

    [Fact]
    public void Disable_AfterEnable_IsStartupEnabled_ReturnsFalse()
    {
        _svc.Enable();
        _svc.Disable();
        Assert.False(_svc.IsStartupEnabled());
    }

    [Fact]
    public void Disable_WhenAlreadyDisabled_IsIdempotent()
    {
        _svc.Disable();
        _svc.Disable();
        Assert.False(_svc.IsStartupEnabled());
    }

    [Fact]
    public void Enable_CalledTwice_IsIdempotent()
    {
        _svc.Enable();
        _svc.Enable();
        Assert.True(_svc.IsStartupEnabled());
    }
}
