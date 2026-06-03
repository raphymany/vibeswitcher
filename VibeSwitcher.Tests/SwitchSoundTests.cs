using VibeSwitcher.Models;
using VibeSwitcher.Services;
using Xunit;

namespace VibeSwitcher.Tests;

public class SwitchSoundTests
{
    private static AppConfig EnabledConfig(string tone = "Click", int volume = 50, string? customPath = null)
        => new()
        {
            SwitchSoundEnabled    = true,
            SwitchSoundTone       = tone,
            SwitchSoundCustomPath = customPath,
            SwitchSoundVolume     = volume,
        };

    private static DeviceProfile Plain() => new() { Name = "P" };

    // ── Global disabled ───────────────────────────────────────────────────────

    [Fact]
    public void GlobalDisabled_ReturnsNull()
    {
        var cfg = new AppConfig { SwitchSoundEnabled = false };
        Assert.Null(SwitchSoundService.Resolve(Plain(), cfg));
    }

    // ── No override — uses global settings ───────────────────────────────────

    [Fact]
    public void NoOverride_UsesGlobalTone()
    {
        var cfg = EnabledConfig("Chime", 70);
        var resolved = SwitchSoundService.Resolve(Plain(), cfg);

        Assert.NotNull(resolved);
        Assert.Equal("Chime", resolved!.Value.tone);
        Assert.Equal(70,      resolved.Value.volume);
    }

    [Fact]
    public void NoOverride_GlobalCustomPath_Forwarded()
    {
        var cfg = EnabledConfig("Custom", 60, "C:\\test.wav");
        var resolved = SwitchSoundService.Resolve(Plain(), cfg);

        Assert.NotNull(resolved);
        Assert.Equal("Custom",       resolved!.Value.tone);
        Assert.Equal("C:\\test.wav", resolved.Value.customPath);
    }

    // ── Profile override: muted ───────────────────────────────────────────────

    [Fact]
    public void Override_Muted_ReturnsNull()
    {
        var cfg = EnabledConfig("Click", 80);
        var profile = new DeviceProfile { SoundOverride = true, SoundMuted = true };

        Assert.Null(SwitchSoundService.Resolve(profile, cfg));
    }

    // ── Profile override: custom tone ─────────────────────────────────────────

    [Fact]
    public void Override_CustomTone_UsesProfileTone()
    {
        var cfg = EnabledConfig("Click", 80);
        var profile = new DeviceProfile { SoundOverride = true, SoundMuted = false, SoundTone = "Blip" };

        var resolved = SwitchSoundService.Resolve(profile, cfg);

        Assert.NotNull(resolved);
        Assert.Equal("Blip", resolved!.Value.tone);
    }

    [Fact]
    public void Override_CustomVolume_UsesProfileVolume()
    {
        var cfg = EnabledConfig("Click", 80);
        var profile = new DeviceProfile { SoundOverride = true, SoundMuted = false, SoundVolume = 25 };

        var resolved = SwitchSoundService.Resolve(profile, cfg);

        Assert.NotNull(resolved);
        Assert.Equal(25, resolved!.Value.volume);
    }

    [Fact]
    public void Override_NullTone_FallsBackToGlobal()
    {
        var cfg = EnabledConfig("Chime", 50);
        var profile = new DeviceProfile { SoundOverride = true, SoundMuted = false, SoundTone = null };

        var resolved = SwitchSoundService.Resolve(profile, cfg);

        Assert.NotNull(resolved);
        Assert.Equal("Chime", resolved!.Value.tone);
    }

    [Fact]
    public void Override_NullVolume_FallsBackToGlobal()
    {
        var cfg = EnabledConfig("Click", 65);
        var profile = new DeviceProfile { SoundOverride = true, SoundMuted = false, SoundVolume = null };

        var resolved = SwitchSoundService.Resolve(profile, cfg);

        Assert.NotNull(resolved);
        Assert.Equal(65, resolved!.Value.volume);
    }

    [Fact]
    public void Override_CustomPath_UsesProfilePath()
    {
        var cfg = EnabledConfig("Custom", 50, "C:\\global.wav");
        var profile = new DeviceProfile
        {
            SoundOverride = true,
            SoundMuted = false,
            SoundTone = "Custom",
            SoundCustomPath = "C:\\profile.wav"
        };

        var resolved = SwitchSoundService.Resolve(profile, cfg);

        Assert.NotNull(resolved);
        Assert.Equal("C:\\profile.wav", resolved!.Value.customPath);
    }

    [Fact]
    public void Override_NullCustomPath_FallsBackToGlobal()
    {
        var cfg = EnabledConfig("Custom", 50, "C:\\global.wav");
        var profile = new DeviceProfile
        {
            SoundOverride = true,
            SoundMuted = false,
            SoundTone = "Custom",
            SoundCustomPath = null
        };

        var resolved = SwitchSoundService.Resolve(profile, cfg);

        Assert.NotNull(resolved);
        Assert.Equal("C:\\global.wav", resolved!.Value.customPath);
    }

    // ── Mute is independent of override flag ─────────────────────────────────

    [Fact]
    public void MuteWithoutOverride_ReturnsNull()
    {
        var cfg = EnabledConfig("Click", 80);
        var profile = new DeviceProfile { SoundOverride = false, SoundMuted = true };

        Assert.Null(SwitchSoundService.Resolve(profile, cfg));
    }

    // ── Override flag off — uses global when not muted ────────────────────────

    [Fact]
    public void OverrideOff_NotMuted_UsesGlobal()
    {
        var cfg = EnabledConfig("Chime", 40);
        var profile = new DeviceProfile
        {
            SoundOverride = false,
            SoundMuted    = false,
            SoundTone     = "Blip", // ignored — override is off
            SoundVolume   = 99      // ignored — override is off
        };

        var resolved = SwitchSoundService.Resolve(profile, cfg);

        Assert.NotNull(resolved);
        Assert.Equal("Chime", resolved!.Value.tone);
        Assert.Equal(40,      resolved.Value.volume);
    }
}
