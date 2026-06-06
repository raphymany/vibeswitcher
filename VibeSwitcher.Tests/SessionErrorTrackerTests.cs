using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class SessionErrorTrackerTests
{
    private readonly SessionErrorTracker _tracker = new();

    [Fact]
    public void HasErrors_IsFalseInitially()
    {
        Assert.False(_tracker.HasErrors);
    }

    [Fact]
    public void Count_IsZeroInitially()
    {
        Assert.Equal(0, _tracker.Count);
    }

    [Fact]
    public void Record_IncrementsCount()
    {
        _tracker.Record(ErrorCode.ConfigLoadFailed, "T", "M");
        Assert.Equal(1, _tracker.Count);
        Assert.True(_tracker.HasErrors);
    }

    [Fact]
    public void Record_StoresCorrectFields()
    {
        _tracker.Record(ErrorCode.ProfileSwitchFailed, "My Title", "My Message");
        var err = _tracker.Errors[0];
        Assert.Equal(ErrorCode.ProfileSwitchFailed, err.Code);
        Assert.Equal("My Title", err.Title);
        Assert.Equal("My Message", err.Message);
    }

    [Fact]
    public void Record_TimestampIsRecent()
    {
        var before = DateTime.Now.AddSeconds(-1);
        _tracker.Record(ErrorCode.ProfileSwitchFailed, "T", "M");
        var after  = DateTime.Now.AddSeconds(1);
        var ts = _tracker.Errors[0].Timestamp;
        Assert.InRange(ts, before, after);
    }

    [Fact]
    public void Record_FiresErrorAddedEvent()
    {
        bool fired = false;
        EventHandler handler = (_, _) => fired = true;
        _tracker.ErrorAdded += handler;
        _tracker.Record(ErrorCode.ConfigSaveFailed, "T", "M");
        Assert.True(fired);
    }

    [Fact]
    public void Errors_ReturnsImmutableSnapshot()
    {
        _tracker.Record(ErrorCode.ConfigLoadFailed, "T", "M");
        var snapshot = _tracker.Errors;
        _tracker.Record(ErrorCode.ConfigSaveFailed, "T2", "M2");
        Assert.Single(snapshot);
    }

    [Fact]
    public async Task Record_ThreadSafe_ConcurrentCalls()
    {
        const int threadCount = 10;
        await Task.WhenAll(Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
                _tracker.Record(ErrorCode.ProfileSwitchFailed, "T", "M"))));
        Assert.Equal(threadCount, _tracker.Count);
    }
}
