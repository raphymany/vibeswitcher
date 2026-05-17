using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class ErrorCodeTests
{
    [Fact]
    public void ToCode_FormatsWithVsPrefix()
    {
        Assert.Equal("VS-001", ErrorCode.PlaybackDeviceUnavailable.ToCode());
    }

    [Fact]
    public void ToCode_PadsToThreeDigits()
    {
        Assert.Equal("VS-027", ErrorCode.AudioServiceUnavailable.ToCode());
        Assert.Equal("VS-028", ErrorCode.DeviceNotificationFailed.ToCode());
    }

    [Fact]
    public void ToCode_MatchesExpectedPattern()
    {
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            Assert.Matches(@"^VS-\d{3}$", code.ToCode());
        }
    }

    [Fact]
    public void AllValues_AreUnique()
    {
        var values = Enum.GetValues<ErrorCode>().Cast<int>().ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllValues_ArePositive()
    {
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            Assert.True((int)code > 0, $"{code} must be > 0");
        }
    }

    [Theory]
    [InlineData(ErrorCode.PlaybackDeviceUnavailable,  "VS-001")]
    [InlineData(ErrorCode.RecordingDeviceUnavailable, "VS-002")]
    [InlineData(ErrorCode.ProfileSwitchFailed,        "VS-003")]
    [InlineData(ErrorCode.HotkeyConflict,             "VS-004")]
    [InlineData(ErrorCode.ConfigLoadFailed,           "VS-007")]
    [InlineData(ErrorCode.ConfigSaveFailed,           "VS-008")]
    [InlineData(ErrorCode.AudioServiceUnavailable,    "VS-027")]
    [InlineData(ErrorCode.DeviceNotificationFailed,   "VS-028")]
    public void ToCode_KnownCodes_MatchExpected(ErrorCode code, string expected)
    {
        Assert.Equal(expected, code.ToCode());
    }
}
