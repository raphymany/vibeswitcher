using VibeSwitcher.Models;
using VibeSwitcher.Services;
using Xunit;

namespace VibeSwitcher.Tests;

public class SwitchSoundTests
{
    private static DeviceProfile Plain() => new() { Name = "P" };

    // ── No override — always null ─────────────────────────────────────────────

    [Fact]
    public void NoOverride_ReturnsNull()
    {
        Assert.Null(SwitchSoundService.Resolve(Plain()));
    }

    [Fact]
    public void NoOverride_IgnoresProfileToneAndVolume()
    {
        var profile = new DeviceProfile { SoundOverride = false, SoundTone = "Blip", SoundVolume = 99 };
        Assert.Null(SwitchSoundService.Resolve(profile));
    }

    // ── Override on — uses profile values ────────────────────────────────────

    [Fact]
    public void Override_CustomTone_UsesProfileTone()
    {
        var profile = new DeviceProfile { SoundOverride = true, SoundTone = "Blip" };
        var resolved = SwitchSoundService.Resolve(profile);

        Assert.NotNull(resolved);
        Assert.Equal("Blip", resolved!.Value.tone);
    }

    [Fact]
    public void Override_CustomVolume_UsesProfileVolume()
    {
        var profile = new DeviceProfile { SoundOverride = true, SoundVolume = 25 };
        var resolved = SwitchSoundService.Resolve(profile);

        Assert.NotNull(resolved);
        Assert.Equal(25, resolved!.Value.volume);
    }

    [Fact]
    public void Override_NullTone_DefaultsToClick()
    {
        var profile = new DeviceProfile { SoundOverride = true, SoundTone = null };
        var resolved = SwitchSoundService.Resolve(profile);

        Assert.NotNull(resolved);
        Assert.Equal("Click", resolved!.Value.tone);
    }

    [Fact]
    public void Override_NullVolume_DefaultsTo50()
    {
        var profile = new DeviceProfile { SoundOverride = true, SoundVolume = null };
        var resolved = SwitchSoundService.Resolve(profile);

        Assert.NotNull(resolved);
        Assert.Equal(50, resolved!.Value.volume);
    }

    [Fact]
    public void Override_CustomPath_UsesProfilePath()
    {
        var profile = new DeviceProfile
        {
            SoundOverride   = true,
            SoundTone       = "Custom",
            SoundCustomPath = "C:\\profile.wav"
        };

        var resolved = SwitchSoundService.Resolve(profile);

        Assert.NotNull(resolved);
        Assert.Equal("C:\\profile.wav", resolved!.Value.customPath);
    }

    [Fact]
    public void Override_NullCustomPath_ReturnsNullPath()
    {
        var profile = new DeviceProfile
        {
            SoundOverride   = true,
            SoundTone       = "Custom",
            SoundCustomPath = null
        };

        var resolved = SwitchSoundService.Resolve(profile);

        Assert.NotNull(resolved);
        Assert.Null(resolved!.Value.customPath);
    }

    // ── Custom-path confinement (security) ────────────────────────────────────

    [Fact]
    public void IsAllowedCustomPath_NullConfinementDir_AllowsAnyPath()
    {
        // Test playback (no managed dir) — the user just picked the file, so it's allowed.
        Assert.True(SwitchSoundService.IsAllowedCustomPath(@"C:\Windows\Media\ding.wav", null));
    }

    [Fact]
    public void IsAllowedCustomPath_PathInsideManagedDir_Allowed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "VibeSwitcherTests", "Sounds");
        Assert.True(SwitchSoundService.IsAllowedCustomPath(Path.Combine(dir, "mine.wav"), dir));
    }

    [Fact]
    public void IsAllowedCustomPath_PathOutsideManagedDir_Rejected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "VibeSwitcherTests", "Sounds");
        Assert.False(SwitchSoundService.IsAllowedCustomPath(@"C:\Windows\Media\ding.wav", dir));
        // Traversal out of the managed dir is also rejected.
        Assert.False(SwitchSoundService.IsAllowedCustomPath(Path.Combine(dir, "..", "escape.wav"), dir));
    }

    [Fact]
    public void IsAllowedCustomPath_EmptyPath_Rejected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "VibeSwitcherTests", "Sounds");
        Assert.False(SwitchSoundService.IsAllowedCustomPath(null, dir));
        Assert.False(SwitchSoundService.IsAllowedCustomPath("", dir));
    }

    // ── All tones resolve ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Click")]
    [InlineData("Chime")]
    [InlineData("Blip")]
    [InlineData("Bell")]
    [InlineData("Alert")]
    [InlineData("Soft")]
    [InlineData("Ping")]
    [InlineData("Custom")]
    public void Override_KnownTone_ReturnsNotNull(string tone)
    {
        var profile = new DeviceProfile { SoundOverride = true, SoundTone = tone };
        Assert.NotNull(SwitchSoundService.Resolve(profile));
    }
}
