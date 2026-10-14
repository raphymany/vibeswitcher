using Tmds.DBus.Protocol;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Platform.Linux.OS;

// Waking from sleep, as systemd-logind announces it: PrepareForSleep(false) from
// org.freedesktop.login1 on the system bus, sent the moment the machine is back — the Linux
// counterpart of Windows' PowerModeChanged(Resume). Every mainstream distribution has logind (also
// elogind on Devuan and others); where it's missing the app keeps its clock-based ResumeDetector.
// Only signals from the bus name logind owns count (on the system bus nothing else can own it).
// (logind also says "awake" after a sleep that failed or was cancelled: the app then just applies
// the active profile again, which is harmless.)
public sealed class LogindSleepWatcher : IDisposable
{
    internal const string Bus = "org.freedesktop.login1";
    internal const string ObjectPath = "/org/freedesktop/login1";
    internal const string Interface = "org.freedesktop.login1.Manager";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly Connection _connection;
    private readonly IAppLogger _logger;
    private IDisposable? _watch;
    private int _disposed;
    private int _stoppedLogged;

    // Raised on a D-Bus thread when the machine has woken up.
    public event Action? Woke;

    private LogindSleepWatcher(Connection connection, IAppLogger logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // Null where there's no system bus (a container, say) or it can't be reached.
    public static Task<LogindSleepWatcher?> TryStartAsync(IAppLogger logger) =>
        TryStartAsync(logger, SystemBusAddress(Environment.GetEnvironmentVariable, File.Exists));

    internal static async Task<LogindSleepWatcher?> TryStartAsync(IAppLogger logger, string? busAddress)
    {
        if (string.IsNullOrEmpty(busAddress)) return null;
        var connection = new Connection(busAddress);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(ConnectTimeout).ConfigureAwait(false);
            var watcher = new LogindSleepWatcher(connection, logger);
            watcher._watch = await connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Sender = Bus, Path = ObjectPath, Interface = Interface, Member = "PrepareForSleep" },
                static (Message m, object? _) => m.GetBodyReader().ReadBool(),
                static (Exception? e, bool goingToSleep, object? _, object? state) =>
                {
                    var self = (LogindSleepWatcher)state!;
                    if (e != null) self.Stopped(e);
                    else if (!goingToSleep) self.RaiseWoke();
                },
                ObserverFlags.None, null, watcher, false).ConfigureAwait(false);
            logger.Info("LogindSleepWatcher", "Listening for wake-ups from the system (logind).");
            return watcher;
        }
        catch (Exception ex)
        {
            logger.Debug("LogindSleepWatcher", $"No wake-ups from the system bus: {ex.Message}");
            connection.Dispose();
            return null;
        }
    }

    // $DBUS_SYSTEM_BUS_ADDRESS, else the standard socket (/var/run is a link to /run on current systems).
    internal static string? SystemBusAddress(Func<string, string?> env, Func<string, bool> exists)
    {
        if (env("DBUS_SYSTEM_BUS_ADDRESS") is { Length: > 0 } address) return address;
        foreach (var socket in new[] { "/run/dbus/system_bus_socket", "/var/run/dbus/system_bus_socket" })
            if (exists(socket)) return "unix:path=" + socket;
        return null;
    }

    // The bus connection went away (practically never without a restart): logged, once — the app's
    // clock-based detector still notices wakes.
    private void Stopped(Exception e)
    {
        if (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _stoppedLogged, 1) == 0)
            _logger.Warning("LogindSleepWatcher", $"No longer hearing wake-ups from the system: {e.Message}");
    }

    private void RaiseWoke()
    {
        if (Volatile.Read(ref _disposed) != 0 || Woke is not { } handlers) return;
        // Each subscriber on its own, so one that throws can't keep the others from hearing it.
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception) { /* subscribers log their own errors */ }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _watch?.Dispose();
        _connection.Dispose();
    }
}
