using Tmds.DBus.Protocol;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform.Linux.OS;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.Linux.Tests;

public sealed class LinuxStartupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Directory.CreateTempSubdirectory("vs-autostart").FullName, "autostart");
    private readonly FakeSessionErrorTracker _errors = new();
    private string? _path = "/home/ann/Apps/VibeSwitcher.AppImage";

    private LinuxStartupService Service() => new(new FakeAppLogger(), _errors, _dir, () => _path);
    private string EntryFile => Path.Combine(_dir, "vibeswitcher.desktop");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch (Exception) { }
    }

    [Fact]
    public void Enable_WritesAnAutostartEntry_Disable_RemovesIt()
    {
        var svc = Service();
        Assert.False(svc.IsStartupEnabled());

        svc.Enable();
        Assert.True(svc.IsStartupEnabled());
        var text = File.ReadAllText(EntryFile);
        Assert.StartsWith("[Desktop Entry]\n", text);
        Assert.Contains("\nType=Application\n", text);
        Assert.Contains("\nExec=/home/ann/Apps/VibeSwitcher.AppImage\n", text);
        Assert.DoesNotContain("\r", text);

        svc.Disable();
        Assert.False(svc.IsStartupEnabled());
        svc.Disable(); // already off: fine
        Assert.Empty(_errors.Errors);
    }

    [Theory]
    [InlineData("Hidden=true")]
    [InlineData("Hidden = true")]
    [InlineData("X-GNOME-Autostart-enabled=false")]
    public void DisabledByTheDesktopsOwnToggle_CountsAsOff(string line)
    {
        var svc = Service();
        svc.Enable();
        File.AppendAllText(EntryFile, line + "\n");
        Assert.False(svc.IsStartupEnabled());
    }

    [Fact]
    public void Refresh_RepointsAMovedApp_LeavesOthersAlone()
    {
        var svc = Service();
        svc.RefreshRegistryPath(); // not enabled: nothing created
        Assert.False(File.Exists(EntryFile));

        svc.Enable();
        _path = "/home/ann/Downloads/VibeSwitcher-2.1.0.AppImage";
        svc.RefreshRegistryPath();
        Assert.Contains("\nExec=/home/ann/Downloads/VibeSwitcher-2.1.0.AppImage\n", File.ReadAllText(EntryFile));
    }

    [Fact]
    public void NoPath_RecordsTheWindowsErrorCode()
    {
        _path = null;
        Service().Enable();
        Assert.Equal(ErrorCode.StartupPathResolutionFailed, Assert.Single(_errors.Errors).Code);
        Assert.False(File.Exists(EntryFile));
    }

    [Fact]
    public void PathWithALineBreak_IsRefused_NotWrittenHalfBroken()
    {
        _path = "/home/ann/odd\nfolder/app";
        Service().Enable();
        Assert.Equal(ErrorCode.StartupPathResolutionFailed, Assert.Single(_errors.Errors).Code);
        Assert.False(File.Exists(EntryFile));
    }

    [Theory]
    [InlineData("/opt/VibeSwitcher.AppImage", "/opt/VibeSwitcher.AppImage")]
    [InlineData("/home/ann/My Apps/VibeSwitcher.AppImage", "\"/home/ann/My Apps/VibeSwitcher.AppImage\"")]
    [InlineData("/home/ann/$weird/app", "\"/home/ann/\\\\$weird/app\"")]
    [InlineData("/home/ann/100%/app", "/home/ann/100%%/app")]
    [InlineData("/home/a\\b/app", "\"/home/a\\\\\\\\b/app\"")]
    [InlineData("/home/ann/\"q\"/app", "\"/home/ann/\\\\\"q\\\\\"/app\"")]
    public void ExecValue_FollowsTheDesktopEntryQuotingRules(string path, string expected) =>
        Assert.Equal(expected, LinuxStartupService.ExecValue(path));

    [Fact]
    public void AnInheritedAppImageVariable_DoesNotRedirectStartAtLogin()
    {
        // Started from a terminal or editor that is itself an AppImage: $APPIMAGE names that program,
        // and this process doesn't run from its mount — so the real executable is used.
        var (oldImage, oldDir) = (Environment.GetEnvironmentVariable("APPIMAGE"), Environment.GetEnvironmentVariable("APPDIR"));
        try
        {
            Environment.SetEnvironmentVariable("APPIMAGE", "/home/ann/Apps/Code.AppImage");
            Environment.SetEnvironmentVariable("APPDIR", "/tmp/.mount_CodeAB12");
            Assert.Equal(Environment.ProcessPath, LinuxStartupService.DefaultLaunchPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPIMAGE", oldImage);
            Environment.SetEnvironmentVariable("APPDIR", oldDir);
        }
    }
}

public sealed class DBusFactAttribute : FactAttribute
{
    public DBusFactAttribute()
    {
        // Opt-in: it registers a stand-in notification service, which a desktop session already has.
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("VS_DBUS_IT") != "1"
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            Skip = "Needs a private D-Bus session: VS_DBUS_IT=1 dbus-run-session -- dotnet test (Linux).";
    }
}

public class LinuxNotifierTests
{
    [Fact]
    public void Body_IsEscapedForMarkup() =>
        Assert.Equal("R&amp;B &lt;3 &gt; Rock", LinuxNotifier.Escape("R&B <3 > Rock"));

    [Fact]
    public void TheSessionBus_IsFoundWithoutTheDBusLibrarysX11Lookup()
    {
        var runtime = Directory.CreateTempSubdirectory("vs-run-").FullName;
        try
        {
            string? Env(string name, string? address = null, string? dir = null) => name switch
            {
                "DBUS_SESSION_BUS_ADDRESS" => address,
                "XDG_RUNTIME_DIR" => dir,
                _ => null,
            };
            Func<string?> throwing = () => throw new DllNotFoundException("libX11");
            Assert.Equal("unix:path=/run/user/1000/bus", LinuxNotifier.SessionBusAddress(n => Env(n, "unix:path=/run/user/1000/bus"), throwing));
            File.WriteAllText(Path.Combine(runtime, "bus"), "");
            Assert.Equal("unix:path=" + Path.Combine(runtime, "bus"), LinuxNotifier.SessionBusAddress(n => Env(n, dir: runtime), throwing));
            // Nothing else to go on: the library's own lookup, which mustn't take the app down.
            Assert.Null(LinuxNotifier.SessionBusAddress(n => null, throwing));
            Assert.Equal("unix:abstract=x", LinuxNotifier.SessionBusAddress(n => null, () => "unix:abstract=x"));
        }
        finally { Directory.Delete(runtime, recursive: true); }
    }

    [Fact]
    public async Task NoSessionBus_FalseAndLoggedOnce()
    {
        var log = new LinuxAudioServiceTests.RecordingLogger();
        using var n = new LinuxNotifier(log, busAddress: null);
        Assert.False(await n.ShowAsync("a", "b"));
        Assert.False(await n.ShowAsync("a", "b"));
        Assert.Single(log.Warnings);
    }

    // A stand-in notification server on the real session bus.
    private sealed class FakeNotificationServer : IMethodHandler
    {
        public readonly List<(string App, uint Replaces, string Summary, string Body, int Timeout)> Calls = [];
        public string[] Capabilities = ["body", "body-markup"];
        public string Path => "/org/freedesktop/Notifications";
        public bool RunMethodHandlerSynchronously(Message message) => true;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var req = context.Request;
            if (req.MemberAsString == "GetCapabilities")
            {
                using var cw = context.CreateReplyWriter("as");
                cw.WriteArray(Capabilities);
                context.Reply(cw.CreateMessage());
            }
            else if (req.MemberAsString == "Notify")
            {
                var r = req.GetBodyReader();
                var app = r.ReadString(); var replaces = r.ReadUInt32(); r.ReadString();
                var summary = r.ReadString(); var body = r.ReadString();
                r.ReadArrayOfString(); r.ReadDictionaryOfStringToVariantValue(); var timeout = r.ReadInt32();
                uint id;
                lock (Calls) { Calls.Add((app, replaces, summary, body, timeout)); id = replaces != 0 ? replaces : (uint)(40 + Calls.Count); }
                using var w = context.CreateReplyWriter("u");
                w.WriteUInt32(id);
                context.Reply(w.CreateMessage());
            }
            return ValueTask.CompletedTask;
        }
    }

    [DBusFact]
    [Trait("Category", "DBus")]
    public async Task Notify_ReachesTheNotificationService_AndLaterOnesReplaceIt()
    {
        using var server = new Connection(Address.Session!);
        await server.ConnectAsync();
        var handler = new FakeNotificationServer();
        server.AddMethodHandler(handler);
        // RequestName("org.freedesktop.Notifications", DO_NOT_QUEUE) → 1 = primary owner.
        uint result = await server.CallMethodAsync(RequestName(server), static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null);
        Assert.Equal(1u, result);

        using (var notifier = new LinuxNotifier(new FakeAppLogger()))
        {
            Assert.True(await notifier.ShowAsync("Switched to Gaming", "Headset & mic"));
            Assert.True(await notifier.ShowAsync("Switched to Music", "Speakers"));
        }
        lock (handler.Calls)
        {
            Assert.Equal(2, handler.Calls.Count);
            Assert.Equal(("VibeSwitcher", 0u, "Switched to Gaming", "Headset &amp; mic", 5000), handler.Calls[0]);
            Assert.Equal(41u, handler.Calls[1].Replaces); // replaces the first banner instead of stacking
        }

        // A service that shows text literally gets it unescaped.
        handler.Capabilities = ["body"];
        using (var plain = new LinuxNotifier(new FakeAppLogger()))
            Assert.True(await plain.ShowAsync("Switched", "Headset & mic"));
        lock (handler.Calls) Assert.Equal("Headset & mic", handler.Calls[^1].Body);
    }

    private static MessageBuffer RequestName(Connection c)
    {
        using var w = c.GetMessageWriter();
        w.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su", MessageFlags.None);
        w.WriteString("org.freedesktop.Notifications");
        w.WriteUInt32(4);
        return w.CreateMessage();
    }
}
