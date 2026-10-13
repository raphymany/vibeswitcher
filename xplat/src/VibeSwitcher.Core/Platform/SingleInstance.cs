using System.IO;
using System.Net.Sockets;
using System.Text;

namespace VibeSwitcher.Platform;

// One VibeSwitcher per user, like the Windows app's named mutex — and a channel for commands.
//
// The first instance holds an exclusive lock on a file in the config folder (released by the OS
// when the process ends, so a crash never leaves a stale lock) and listens on a Unix domain socket
// beside it; the folder is private to the user (0700), so nobody else can reach the socket. A later
// launch can't take the lock, so it connects, sends its command ("open Settings", "switch to Gaming",
// "toggle mute") and gets the result back; the launcher then prints it and exits.
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessage = 4096;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    // A switch can take a while (a Bluetooth headset changing profile, a confirmation on screen).
    internal static TimeSpan HandlerTimeout = TimeSpan.FromSeconds(30);
    // How long a launch keeps trying to reach an instance that holds the lock but isn't listening
    // yet (it's still starting up).
    internal static TimeSpan ConnectWindow = TimeSpan.FromSeconds(3);

    private readonly AppPaths _paths;
    private readonly Action<string>? _warn;
    private FileStream? _lock;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _disposed;
    private bool _socketCreated;

    public SingleInstance(AppPaths paths, Action<string>? warn = null)
    {
        _paths = paths;
        _warn = warn;
    }

    // True when this is the first instance. `handle` runs for every command a later launch sends
    // (on a background thread — marshal to the UI thread inside); its string is the reply.
    // Throws InvalidOperationException when the lock can't be used at all (with the reason).
    public bool TryAcquire(Func<AppCommand, Task<string>> handle)
    {
        try { Directory.CreateDirectory(_paths.ConfigDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"VibeSwitcher can't create its settings folder '{_paths.ConfigDir}': {ex.Message}", ex);
        }
        try { Restrict(_paths.ConfigDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _warn?.Invoke($"Could not make the settings folder private: {ex.Message}");
        }
        try
        {
            // FileShare.None is an exclusive lock: flock(LOCK_EX|LOCK_NB) on macOS/Linux, a sharing
            // lock on Windows.
            _lock = new FileStream(_paths.InstanceLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (IsLockContention(ex))
        {
            return false; // another instance holds it
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"VibeSwitcher can't use its lock file '{_paths.InstanceLock}': {ex.Message}" +
                (ex is UnauthorizedAccessException ? " (was VibeSwitcher once run with sudo? Delete that file.)" : ""), ex);
        }

        try
        {
            if (!OpenChannel(handle)) return false;
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Still the single instance — just without the command line (e.g. a network home folder
            // that can't hold sockets).
            _warn?.Invoke($"The command line can't reach VibeSwitcher this session: {ex.Message}");
            CloseListener();
        }
        return true;
    }

    // False when another live instance turned out to be listening (its lock file was replaced).
    private bool OpenChannel(Func<AppCommand, Task<string>> handle)
    {
        if (!_paths.SocketPathFits)
        {
            _warn?.Invoke("The settings folder's path is too long for the command line's connection; command-line control is off.");
            return true;
        }
        if (File.Exists(_paths.InstanceSocket))
        {
            // Normally a leftover from a crash (we hold the lock) — but never remove a socket someone
            // is still listening on.
            if (CanConnect(_paths.InstanceSocket))
            {
                _lock?.Dispose();
                _lock = null;
                return false;
            }
            File.Delete(_paths.InstanceSocket);
        }
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_paths.InstanceSocket));
        _socketCreated = true;
        Restrict(_paths.InstanceSocket, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _listener.Listen(8);

        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoop(_listener, handle, _cts.Token));
        return true;
    }

    // Lock already held by someone: EWOULDBLOCK/EAGAIN on macOS (35) and Linux (11), or a sharing/lock
    // violation on Windows (32/33). Anything else (read-only disk, disk full…) is a real error.
    internal static bool IsLockContention(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        return OperatingSystem.IsWindows() ? code is 32 or 33 : code is 11 or 35;
    }

    // A wedged listener (e.g. a stopped process with a full queue) mustn't hang startup: give up after
    // a second and count it as alive — safer than deleting a socket that might still be in use.
    private static bool CanConnect(string socketPath)
    {
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            probe.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeout.Token).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (OperationCanceledException) { return true; }
        catch (SocketException) { return false; }
    }

    private static async Task AcceptLoop(Socket listener, Func<AppCommand, Task<string>> handle, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try { client = await listener.AcceptAsync(ct).ConfigureAwait(false); }
            catch (Exception) when (ct.IsCancellationRequested) { return; }
            catch (SocketException)
            {
                // e.g. out of file descriptors: back off instead of spinning.
                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                continue;
            }
            _ = Task.Run(() => Serve(client, handle, ct), CancellationToken.None);
        }
    }

    private static async Task Serve(Socket client, Func<AppCommand, Task<string>> handle, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                string? line;
                using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    read.CancelAfter(ReadTimeout);
                    line = await ReadLine(client, read.Token).ConfigureAwait(false);
                }
                string reply;
                if (line == null || AppCommand.Deserialize(line) is not { } cmd)
                    reply = "error: unrecognised request";
                else
                {
                    try { reply = await handle(cmd).WaitAsync(HandlerTimeout, ct).ConfigureAwait(false); }
                    catch (TimeoutException) { reply = "error: VibeSwitcher is still working on it"; }
                    catch (Exception ex) { reply = $"error: {ex.Message}"; }
                }
                using var write = CancellationTokenSource.CreateLinkedTokenSource(ct);
                write.CancelAfter(ReadTimeout);
                await client.SendAsync(Encoding.UTF8.GetBytes(OneLine(reply) + "\n"), SocketFlags.None, write.Token).ConfigureAwait(false);
            }
            catch (Exception) { /* the sender gave up or went away */ }
        }
    }

    // Sends a command to the running instance (call this after TryAcquire returned false) and returns
    // its reply — or null when it can't be reached (it may still be starting; retried for a few seconds).
    public static async Task<string?> SendAsync(AppPaths paths, AppCommand command)
    {
        if (!paths.SocketPathFits) return null;
        var until = DateTime.UtcNow + ConnectWindow;
        while (true)
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(paths.InstanceSocket)).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                if (DateTime.UtcNow >= until) return null;
                await Task.Delay(100).ConfigureAwait(false);
                continue;
            }
            using var timeout = new CancellationTokenSource(ReadTimeout + HandlerTimeout + ReadTimeout);
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(command.Serialize() + "\n"), SocketFlags.None, timeout.Token).ConfigureAwait(false);
                return await ReadLine(socket, timeout.Token).ConfigureAwait(false) ?? "error: no reply";
            }
            catch (OperationCanceledException) { return "error: the running VibeSwitcher didn't answer"; }
            catch (SocketException) { return "error: the running VibeSwitcher closed the connection"; }
        }
    }

    // One '\n'-terminated line of at most MaxMessage bytes; null if the peer sends nothing usable.
    private static async Task<string?> ReadLine(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[MaxMessage];
        int filled = 0;
        while (filled < buffer.Length)
        {
            int n = await socket.ReceiveAsync(buffer.AsMemory(filled), SocketFlags.None, ct).ConfigureAwait(false);
            if (n == 0) break;
            int nl = Array.IndexOf(buffer, (byte)'\n', filled, n);
            filled += n;
            if (nl >= 0) return Encoding.UTF8.GetString(buffer, 0, nl);
        }
        return null;
    }

    private static string OneLine(string s) => s.Replace('\n', ' ').Replace('\r', ' ');

    private static void Restrict(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, mode);
    }

    private void CloseListener()
    {
        _cts?.Cancel();
        try { _listener?.Dispose(); } catch (Exception) { }
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        _listener = null;
        _acceptLoop = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        CloseListener();
        if (_lock != null)
        {
            // Only a socket file this instance created (while holding the lock) is removed.
            if (_socketCreated) try { File.Delete(_paths.InstanceSocket); } catch (Exception) { }
            _lock.Dispose();
            _lock = null;
        }
    }
}
