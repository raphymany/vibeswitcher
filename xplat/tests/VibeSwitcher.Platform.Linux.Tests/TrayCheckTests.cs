using Tmds.DBus.Protocol;
using VibeSwitcher.Platform.Linux.OS;

namespace VibeSwitcher.Platform.Linux.Tests;

// Whether the desktop shows tray icons: someone owns org.kde.StatusNotifierWatcher on the bus.
public class TrayCheckTests
{
    [Fact]
    public async Task NoSessionBus_CantTell() => Assert.Null(await LinuxTrayCheck.IsTrayAvailableAsync(null));

    [DBusFact]
    [Trait("Category", "DBus")]
    public async Task ATrayHost_IsFoundOnTheBus()
    {
        Assert.False(await LinuxTrayCheck.IsTrayAvailableAsync(Address.Session));

        using var host = new Connection(Address.Session!);
        await host.ConnectAsync();
        // RequestName(watcher, DO_NOT_QUEUE) → 1 = primary owner.
        uint result = await host.CallMethodAsync(RequestName(host), static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null);
        Assert.Equal(1u, result);
        // A watcher that doesn't say whether a host shows the icons is taken at its word.
        Assert.True(await LinuxTrayCheck.IsTrayAvailableAsync(Address.Session));

        // One that says no host is registered (KDE's background service in a GNOME session): no tray.
        var watcher = new FakeWatcher();
        host.AddMethodHandler(watcher);
        Assert.False(await LinuxTrayCheck.IsTrayAvailableAsync(Address.Session));
        watcher.HostRegistered = true;
        Assert.True(await LinuxTrayCheck.IsTrayAvailableAsync(Address.Session));
    }

    private sealed class FakeWatcher : IMethodHandler
    {
        public volatile bool HostRegistered;
        public string Path => "/StatusNotifierWatcher";
        public bool RunMethodHandlerSynchronously(Message message) => true;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.Request.MemberAsString == "Get")
            {
                using var w = context.CreateReplyWriter("v");
                w.WriteVariant(VariantValue.Bool(HostRegistered));
                context.Reply(w.CreateMessage());
            }
            return ValueTask.CompletedTask;
        }
    }

    private static MessageBuffer RequestName(Connection c)
    {
        using var w = c.GetMessageWriter();
        w.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su", MessageFlags.None);
        w.WriteString(LinuxTrayCheck.WatcherName);
        w.WriteUInt32(4);
        return w.CreateMessage();
    }
}
