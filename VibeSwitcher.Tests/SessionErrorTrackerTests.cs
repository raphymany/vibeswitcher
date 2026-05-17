using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class SessionErrorTrackerTests : IDisposable
{
    public SessionErrorTrackerTests() => SessionErrorTracker.Reset();
    public void Dispose() => SessionErrorTracker.Reset();

    [Fact]
    public void HasErrors_IsFalseAfterReset()
    {
        Assert.False(SessionErrorTracker.HasErrors);
    }

    [Fact]
    public void Count_IsZeroAfterReset()
    {
        Assert.Equal(0, SessionErrorTracker.Count);
    }

    [Fact]
    public void Record_IncrementsCount()
    {
        SessionErrorTracker.Record(ErrorCode.ConfigLoadFailed, "T", "M");
        Assert.Equal(1, SessionErrorTracker.Count);
        Assert.True(SessionErrorTracker.HasErrors);
    }

    [Fact]
    public void Record_StoresCorrectFields()
    {
        SessionErrorTracker.Record(ErrorCode.ProfileSwitchFailed, "My Title", "My Message");
        var err = SessionErrorTracker.Errors[0];
        Assert.Equal(ErrorCode.ProfileSwitchFailed, err.Code);
        Assert.Equal("My Title", err.Title);
        Assert.Equal("My Message", err.Message);
    }

    [Fact]
    public void Record_TimestampIsRecent()
    {
        var before = DateTime.Now.AddSeconds(-1);
        SessionErrorTracker.Record(ErrorCode.ProfileSwitchFailed, "T", "M");
        var after  = DateTime.Now.AddSeconds(1);
        var ts = SessionErrorTracker.Errors[0].Timestamp;
        Assert.InRange(ts, before, after);
    }

    [Fact]
    public void Record_FiresErrorAddedEvent()
    {
        bool fired = false;
        EventHandler handler = (_, _) => fired = true;
        SessionErrorTracker.ErrorAdded += handler;
        try
        {
            SessionErrorTracker.Record(ErrorCode.ConfigSaveFailed, "T", "M");
            Assert.True(fired);
        }
        finally
        {
            SessionErrorTracker.ErrorAdded -= handler;
        }
    }

    [Fact]
    public void Errors_ReturnsImmutableSnapshot()
    {
        SessionErrorTracker.Record(ErrorCode.ConfigLoadFailed, "T", "M");
        var snapshot = SessionErrorTracker.Errors;
        SessionErrorTracker.Record(ErrorCode.ConfigSaveFailed, "T2", "M2");
        Assert.Single(snapshot); // snapshot captured before the second Record
    }

    [Fact]
    public async Task Record_ThreadSafe_ConcurrentCalls()
    {
        const int threadCount = 10;
        await Task.WhenAll(Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
                SessionErrorTracker.Record(ErrorCode.ProfileSwitchFailed, "T", "M"))));
        Assert.Equal(threadCount, SessionErrorTracker.Count);
    }
}
