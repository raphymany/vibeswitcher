using System.Diagnostics;
using Tmds.DBus.Protocol;
using VibeSwitcher.Platform.Linux.OS;

namespace VibeSwitcher.Platform.Linux.Tests;

public class LogindSleepAddressTests
{
    [Fact]
    public void TheSystemBus_IsTheVariable_ElseTheStandardSocket()
    {
        Assert.Equal("unix:path=/tmp/sys", LogindSleepWatcher.SystemBusAddress(
            n => n == "DBUS_SYSTEM_BUS_ADDRESS" ? "unix:path=/tmp/sys" : null, _ => true));
        Assert.Equal("unix:path=/run/dbus/system_bus_socket", LogindSleepWatcher.SystemBusAddress(_ => null, _ => true));
        Assert.Equal("unix:path=/var/run/dbus/system_bus_socket", LogindSleepWatcher.SystemBusAddress(_ => null, p => p.StartsWith("/var/")));
        Assert.Null(LogindSleepWatcher.SystemBusAddress(_ => "", _ => false)); // (a container without one)
    }

    [Fact]
    public async Task NoBus_NoWatcher()
    {
        Assert.Null(await LogindSleepWatcher.TryStartAsync(new LinuxAudioServiceTests.RecordingLogger(), busAddress: null));
        // (Nothing listening there: also none, and nothing thrown.)
        Assert.Null(await LogindSleepWatcher.TryStartAsync(new LinuxAudioServiceTests.RecordingLogger(), "unix:path=/nonexistent/vs-bus"));
    }
}

// logind's PrepareForSleep signal, from a stand-in that owns logind's bus name on a private bus.
[Trait("Category", "DBus")]
public sealed class LogindSleepWatcherTests
{
    private static MessageBuffer PrepareForSleep(Connection bus, bool start)
    {
        using var w = bus.GetMessageWriter();
        w.WriteSignalHeader(null, LogindSleepWatcher.ObjectPath, LogindSleepWatcher.Interface, "PrepareForSleep", "b");
        w.WriteBool(start);
        return w.CreateMessage();
    }

    private static MessageBuffer RequestName(Connection bus, string name)
    {
        using var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su", MessageFlags.None);
        w.WriteString(name);
        w.WriteUInt32(4); // DO_NOT_QUEUE
        return w.CreateMessage();
    }

    private static async Task Until(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
    }

    [DBusFact]
    public async Task OnlyLogindsWake_Counts()
    {
        using var logind = new Connection(Address.Session!);
        await logind.ConnectAsync();
        Assert.Equal(1u, await logind.CallMethodAsync(RequestName(logind, LogindSleepWatcher.Bus), static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null));
        using var impostor = new Connection(Address.Session!);
        await impostor.ConnectAsync();

        using var watcher = (await LogindSleepWatcher.TryStartAsync(new LinuxAudioServiceTests.RecordingLogger(), Address.Session))!;
        Assert.NotNull(watcher);
        int woke = 0;
        watcher.Woke += () => Interlocked.Increment(ref woke);
        watcher.Woke += () => throw new InvalidOperationException("a subscriber's own trouble"); // (doesn't stop the others)

        Assert.True(logind.TrySendMessage(PrepareForSleep(logind, start: true)));   // going to sleep: not a wake
        Assert.True(impostor.TrySendMessage(PrepareForSleep(impostor, start: false))); // not logind
        Assert.True(logind.TrySendMessage(PrepareForSleep(logind, start: false)));  // woke up
        await Until(() => Volatile.Read(ref woke) == 1);
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref woke));

        watcher.Dispose();
        Assert.True(logind.TrySendMessage(PrepareForSleep(logind, start: false)));
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref woke)); // (stopped listening)
    }
}
