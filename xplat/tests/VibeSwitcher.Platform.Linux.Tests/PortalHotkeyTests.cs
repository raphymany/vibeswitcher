using System.Diagnostics;
using Tmds.DBus.Protocol;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hotkeys;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.Linux.Tests;

// A stand-in for the desktop's Global Shortcuts service (xdg-desktop-portal), on a private session
// bus: it answers like the real one — Request objects whose Response signal carries the result.
internal sealed class FakePortal : IMethodHandler, IAsyncDisposable
{
    public readonly Connection Bus;
    public readonly List<string> AppIds = [];
    public readonly List<string> ClosedSessions = [];
    public readonly List<(string Session, List<(string Id, string Description, string Trigger)> Shortcuts)> Binds = [];
    public readonly List<string> ClosedRequests = [];   // dialogs withdrawn by the app
    public uint BindResponse;          // 0 = approved, 1 = declined by the user
    public bool HoldBind;              // the user hasn't answered yet: no Response
    public int FailCreate;             // fail this many CreateSession calls (service still starting)
    public int Creates;
    public string? LastSession;
    private int _sessions;

    private FakePortal(Connection bus) => Bus = bus;

    public string Path => PortalKeyGrabber.PortalPath;
    // Off the receive loop: a handler that adds another object (a session) mustn't block it.
    public bool RunMethodHandlerSynchronously(Message message) => false;

    public static async Task<FakePortal> StartAsync()
    {
        var bus = new Connection(Address.Session!);
        await bus.ConnectAsync();
        var portal = new FakePortal(bus);
        bus.AddMethodHandler(portal);
        uint owner = await bus.CallMethodAsync(RequestName(bus), static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null);
        Assert.Equal(1u, owner); // primary owner
        return portal;
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var req = context.Request;
        string sender = req.SenderAsString!.TrimStart(':').Replace('.', '_');
        switch (req.InterfaceAsString, req.MemberAsString)
        {
            case ("org.freedesktop.DBus.Properties", "Get"):
            {
                var r = req.GetBodyReader();
                if (r.ReadString() != PortalKeyGrabber.ShortcutsInterface) { context.ReplyError("org.freedesktop.DBus.Error.UnknownInterface", "no"); break; }
                using var w = context.CreateReplyWriter("v");
                w.WriteVariantUInt32(1);
                context.Reply(w.CreateMessage());
                break;
            }
            case ("org.freedesktop.host.portal.Registry", "Register"):
            {
                lock (AppIds) AppIds.Add(req.GetBodyReader().ReadString());
                using var w = context.CreateReplyWriter(null);
                context.Reply(w.CreateMessage());
                break;
            }
            case (PortalKeyGrabber.ShortcutsInterface, "CreateSession"):
            {
                lock (Binds)
                {
                    Creates++;
                    if (FailCreate > 0)
                    {
                        FailCreate--;
                        context.ReplyError("org.freedesktop.DBus.Error.Failed", "backend not ready");
                        break;
                    }
                }
                var options = req.GetBodyReader().ReadDictionaryOfStringToVariantValue();
                string request = $"{PortalKeyGrabber.PortalPath}/request/{sender}/{options["handle_token"].GetString()}";
                string session = $"{PortalKeyGrabber.PortalPath}/session/{sender}/{options["session_handle_token"].GetString()}{++_sessions}";
                Bus.AddMethodHandler(new FakeSession(this, session));
                ReplyPath(context, request);
                Emit(Response(Bus, request, 0, session, null));
                lock (Binds) LastSession = session;
                break;
            }
            case (PortalKeyGrabber.ShortcutsInterface, "BindShortcuts"):
            {
                var r = req.GetBodyReader();
                var session = r.ReadObjectPathAsString();
                var shortcuts = new List<(string, string, string)>();
                var end = r.ReadArrayStart(DBusType.Struct);
                while (r.HasNext(end))
                {
                    r.AlignStruct();
                    var id = r.ReadString();
                    var props = r.ReadDictionaryOfStringToVariantValue();
                    shortcuts.Add((id, props["description"].GetString(), props["preferred_trigger"].GetString()));
                }
                r.ReadString(); // parent window
                var options = r.ReadDictionaryOfStringToVariantValue();
                string request = $"{PortalKeyGrabber.PortalPath}/request/{sender}/{options["handle_token"].GetString()}";
                uint code;
                bool hold;
                lock (Binds) { Binds.Add((session, shortcuts)); code = BindResponse; hold = HoldBind; }
                Bus.AddMethodHandler(new FakeRequest(this, request));
                ReplyPath(context, request);
                if (hold) break; // the dialog stays up
                // The desktop reports the keys it settled on, in its own words.
                Emit(Response(Bus, request, code, null, code == 0 ? shortcuts.Select(s => (s.Item1, "Desk " + s.Item3)).ToList() : null));
                break;
            }
            default:
                context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "no");
                break;
        }
        return ValueTask.CompletedTask;
    }

    private sealed class FakeSession(FakePortal owner, string path) : IMethodHandler
    {
        public string Path => path;
        public bool RunMethodHandlerSynchronously(Message message) => true;
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.Request.MemberAsString == "Close")
            {
                lock (owner.Binds) owner.ClosedSessions.Add(path);
                using var w = context.CreateReplyWriter(null);
                context.Reply(w.CreateMessage());
            }
            return ValueTask.CompletedTask;
        }
    }

    // A Request object: Close withdraws its dialog.
    private sealed class FakeRequest(FakePortal owner, string path) : IMethodHandler
    {
        public string Path => path;
        public bool RunMethodHandlerSynchronously(Message message) => true;
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.Request.MemberAsString == "Close")
            {
                lock (owner.Binds) owner.ClosedRequests.Add(path);
                using var w = context.CreateReplyWriter(null);
                context.Reply(w.CreateMessage());
            }
            return ValueTask.CompletedTask;
        }
    }

    public void Emit(MessageBuffer signal) => Assert.True(Bus.TrySendMessage(signal));

    public MessageBuffer Activated(string session, string id)
    {
        using var w = Bus.GetMessageWriter();
        w.WriteSignalHeader(null, PortalKeyGrabber.PortalPath, PortalKeyGrabber.ShortcutsInterface, "Activated", "osta{sv}");
        w.WriteObjectPath(session);
        w.WriteString(id);
        w.WriteUInt64(12345);
        w.WriteDictionary(new Dictionary<string, VariantValue>());
        return w.CreateMessage();
    }

    public MessageBuffer ShortcutsChanged(string session, params (string Id, string Trigger)[] shortcuts)
    {
        var w = Bus.GetMessageWriter();
        try
        {
            w.WriteSignalHeader(null, PortalKeyGrabber.PortalPath, PortalKeyGrabber.ShortcutsInterface, "ShortcutsChanged", "oa(sa{sv})");
            w.WriteObjectPath(session);
            WriteShortcuts(ref w, shortcuts);
            return w.CreateMessage();
        }
        finally { w.Dispose(); }
    }

    // (By ref: the writer is a struct, and a copy's writes wouldn't count in the original's length.)
    private static void WriteShortcuts(ref MessageWriter w, IEnumerable<(string Id, string Trigger)> shortcuts)
    {
        var array = w.WriteArrayStart(DBusType.Struct);
        foreach (var (id, trigger) in shortcuts)
        {
            w.WriteStructureStart();
            w.WriteString(id);
            w.WriteDictionary(new Dictionary<string, VariantValue> { ["trigger_description"] = VariantValue.String(trigger) });
        }
        w.WriteArrayEnd(array);
    }

    // Response(u response, a{sv} results) on the Request object.
    private static MessageBuffer Response(Connection bus, string request, uint code, string? session, List<(string, string)>? shortcuts)
    {
        using var w = bus.GetMessageWriter();
        w.WriteSignalHeader(null, request, PortalKeyGrabber.RequestInterface, "Response", "ua{sv}");
        w.WriteUInt32(code);
        var results = new Dictionary<string, VariantValue>();
        if (session != null) results["session_handle"] = VariantValue.String(session);
        if (shortcuts != null)
            results["shortcuts"] = new Array<Struct<string, Dict<string, VariantValue>>>(shortcuts.Select(s =>
                new Struct<string, Dict<string, VariantValue>>(s.Item1,
                    new Dict<string, VariantValue>(new Dictionary<string, VariantValue> { ["trigger_description"] = VariantValue.String(s.Item2) }))));
        w.WriteDictionary(results);
        return w.CreateMessage();
    }

    private static void ReplyPath(MethodContext context, string path)
    {
        using var w = context.CreateReplyWriter("o");
        w.WriteObjectPath(path);
        context.Reply(w.CreateMessage());
    }

    private static MessageBuffer RequestName(Connection c)
    {
        using var w = c.GetMessageWriter();
        w.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su", MessageFlags.None);
        w.WriteString(PortalKeyGrabber.PortalBus);
        w.WriteUInt32(4); // DO_NOT_QUEUE
        return w.CreateMessage();
    }

    private static MessageBuffer ReleaseName(Connection c)
    {
        using var w = c.GetMessageWriter();
        w.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReleaseName", "s", MessageFlags.None);
        w.WriteString(PortalKeyGrabber.PortalBus);
        return w.CreateMessage();
    }

    public int BindCount { get { lock (Binds) return Binds.Count; } }
    public int CreateCount { get { lock (Binds) return Creates; } }

    public async ValueTask DisposeAsync()
    {
        // Released explicitly (not just by disconnecting, which the bus handles later), so the next
        // test can take the name straight away.
        try { await Bus.CallMethodAsync(ReleaseName(Bus), static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null); }
        catch (DBusException) { }
        Bus.Dispose();
    }
}

[Trait("Category", "DBus")]
public sealed class PortalKeyGrabberTests
{
    private const int CtrlAlt = HotkeyDefinition.MOD_CTRL | HotkeyDefinition.MOD_ALT;
    private readonly LinuxAudioServiceTests.RecordingLogger _log = new();

    private Task<PortalKeyGrabber?> Create(TimeSpan? debounce = null, TimeSpan? approvalTimeout = null, TimeSpan[]? retryDelays = null) =>
        PortalKeyGrabber.TryCreateAsync(_log, Address.Session, debounce ?? Timeout.InfiniteTimeSpan, approvalTimeout, retryDelays);

    private static GrabResult Add(PortalKeyGrabber g, ushort id, string name, int vk)
    {
        g.Describe(id, name, "Do " + name);
        return g.Register(id, CtrlAlt, vk, out _);
    }

    private static async Task Until(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.True(condition());
    }

    [DBusFact]
    public async Task NoService_Null()
    {
        Assert.Null(await Create());
        Assert.Null(await PortalKeyGrabber.TryCreateAsync(_log, busAddress: null, Timeout.InfiniteTimeSpan));
    }

    [DBusFact]
    public async Task Shortcuts_AreHandedToTheDesktop_ByStableName_AndPressesArrive()
    {
        await using var portal = await FakePortal.StartAsync();
        using var g = (await Create())!;
        Assert.NotNull(g);
        Assert.Equal([PortalKeyGrabber.AppId], portal.AppIds);

        var profile = Guid.NewGuid();
        Assert.Equal(GrabResult.Ok, Add(g, 7, ShortcutNames.Profile(profile), 0x47));
        Assert.Equal(GrabResult.Ok, Add(g, 8, ShortcutNames.Settings, 0x53));
        await g.FlushAsync();

        var (session, shortcuts) = Assert.Single(portal.Binds);
        Assert.Equal([(ShortcutNames.Profile(profile), "Do " + ShortcutNames.Profile(profile), "CTRL+ALT+g"), (ShortcutNames.Settings, "Do settings", "CTRL+ALT+s")],
            shortcuts.OrderBy(s => s.Id, StringComparer.Ordinal));
        Assert.Equal(PortalShortcutState.Bound, g.State);
        Assert.Equal("Desk CTRL+ALT+g", g.AssignedTriggers[ShortcutNames.Profile(profile)]);

        var pressed = new List<ushort>();
        g.Pressed += id => { lock (pressed) pressed.Add(id); };
        portal.Emit(portal.Activated(session, ShortcutNames.Settings));
        portal.Emit(portal.Activated("/org/freedesktop/portal/desktop/session/other/x", ShortcutNames.Settings)); // not ours
        portal.Emit(portal.Activated(session, ShortcutNames.Profile(profile)));
        await Until(() => { lock (pressed) return pressed.Count == 2; });
        await Task.Delay(100);
        lock (pressed) Assert.Equal(new ushort[] { 7, 8 }, pressed.Order());
    }

    [DBusFact]
    public async Task SameShortcutsAgain_DoesntAskAgain_AChange_StartsANewSession()
    {
        await using var portal = await FakePortal.StartAsync();
        using var g = (await Create())!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        Add(g, 2, ShortcutNames.MiniMode, 0x43);
        await g.FlushAsync();
        var first = portal.LastSession;

        // What TestHotkey does: release everything, try a probe, take everything back.
        g.Unregister(1); g.Unregister(2);
        Assert.Equal(GrabResult.Ok, Add(g, 3, ShortcutNames.Probe, 0x48));
        g.Unregister(3);
        g.Register(1, CtrlAlt, 0x53, out _);
        g.Register(2, CtrlAlt, 0x43, out _);
        await g.FlushAsync();
        Assert.Equal(1, portal.BindCount);

        g.Unregister(2);
        await g.FlushAsync();
        Assert.Equal(2, portal.BindCount);
        lock (portal.Binds)
        {
            Assert.Equal([first!], portal.ClosedSessions);
            Assert.Equal([ShortcutNames.Settings], portal.Binds[1].Shortcuts.Select(s => s.Id));
        }

        // Nothing left: the session is closed and nothing is bound.
        g.Unregister(1);
        await g.FlushAsync();
        Assert.Equal(2, portal.BindCount);
        Assert.Equal(PortalShortcutState.NotBound, g.State);
    }

    [DBusFact]
    public async Task Declined_IsReported_AndNotAskedAgainUntilSomethingChanges()
    {
        await using var portal = await FakePortal.StartAsync();
        portal.BindResponse = 1;
        using var g = (await Create())!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        await g.FlushAsync();
        Assert.Equal(PortalShortcutState.Declined, g.State);
        Assert.Single(_log.Warnings);

        g.Unregister(1);
        g.Register(1, CtrlAlt, 0x53, out _);
        await g.FlushAsync();
        Assert.Equal(1, portal.BindCount);

        portal.BindResponse = 0;
        Add(g, 2, ShortcutNames.MiniMode, 0x43);
        await g.FlushAsync();
        Assert.Equal(2, portal.BindCount);
        Assert.Equal(PortalShortcutState.Bound, g.State);
    }

    [DBusFact]
    public async Task KeysChangedInTheDesktopsSettings_AreTracked()
    {
        await using var portal = await FakePortal.StartAsync();
        using var g = (await Create())!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        await g.FlushAsync();
        int changes = 0;
        g.StateChanged += () => Interlocked.Increment(ref changes);

        portal.Emit(portal.ShortcutsChanged(portal.LastSession!, (ShortcutNames.Settings, "Super+S")));
        await Until(() => g.AssignedTriggers.GetValueOrDefault(ShortcutNames.Settings) == "Super+S");
        await Until(() => Volatile.Read(ref changes) > 0);
        Assert.Equal(PortalShortcutState.Bound, g.State);
    }

    [DBusFact]
    public async Task RegistrationRules_AndHandOverAfterAShortPause()
    {
        await using var portal = await FakePortal.StartAsync();
        using var g = (await Create(TimeSpan.FromMilliseconds(30)))!;
        Assert.Equal(GrabResult.Ok, Add(g, 1, ShortcutNames.Settings, 0x53));
        Assert.Equal(GrabResult.InUse, Add(g, 2, ShortcutNames.MiniMode, 0x53)); // same shortcut twice, as on Windows
        Assert.Equal(GrabResult.Unsupported, Add(g, 3, ShortcutNames.MiniMode, 0x10));
        await Until(() => portal.BindCount == 1);

        g.Dispose();
        Assert.Equal(GrabResult.Failed, g.Register(1, CtrlAlt, 0x53, out _));
        g.Unregister(1);
        g.Dispose();
    }

    [DBusFact]
    public async Task OnWayland_TheSelectionPicksThePortal()
    {
        await using var portal = await FakePortal.StartAsync();
        var setup = await LinuxHotkeys.CreateAsync(gnomeShortcutsAllowed: false,
            k => k == "XDG_SESSION_TYPE" ? "wayland" : null,
            () => throw new InvalidOperationException("X11 must not be used on Wayland"),
            () => Create(),
            () => throw new InvalidOperationException("the GNOME bridge isn't needed"),
            () => throw new InvalidOperationException("the GNOME bridge isn't needed"),
            () => throw new InvalidOperationException("not GNOME: nothing to clean up"));
        using var grabber = setup.Grabber;
        Assert.Equal(LinuxHotkeyBackend.Portal, setup.Backend);
        Assert.IsType<PortalKeyGrabber>(grabber);
    }

    [DBusFact]
    public async Task ChangedWhileTheDialogIsUp_ItIsWithdrawn_AndTheNewSetAskedFor()
    {
        await using var portal = await FakePortal.StartAsync();
        portal.HoldBind = true;
        using var g = (await Create())!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        var first = g.FlushAsync();
        await Until(() => portal.BindCount == 1);
        Assert.Equal(PortalShortcutState.WaitingForUser, g.State);

        // The same set again (TestHotkey's release and re-take) doesn't disturb the open dialog.
        g.Unregister(1);
        g.Register(1, CtrlAlt, 0x53, out _);
        _ = g.FlushAsync();
        await Task.Delay(100);
        Assert.False(first.IsCompleted);

        portal.HoldBind = false;
        Add(g, 2, ShortcutNames.MiniMode, 0x43);
        await g.FlushAsync();
        await first;

        Assert.Equal(2, portal.BindCount);
        lock (portal.Binds)
        {
            Assert.Single(portal.ClosedRequests);  // the first dialog was withdrawn
            Assert.Contains(portal.Binds[0].Session, portal.ClosedSessions);
            Assert.Equal(2, portal.Binds[1].Shortcuts.Count);
        }
        Assert.Equal(PortalShortcutState.Bound, g.State);
    }

    [DBusFact]
    public async Task ChangedAndChangedBack_WhileTheDialogIsUp_TheOriginalSetIsBoundAgain()
    {
        await using var portal = await FakePortal.StartAsync();
        using var g = (await Create())!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        await g.FlushAsync(); // set A approved

        portal.HoldBind = true;
        Add(g, 2, ShortcutNames.MiniMode, 0x43);
        var b = g.FlushAsync(); // set B: dialog up
        await Until(() => portal.BindCount == 2);

        portal.HoldBind = false;
        g.Unregister(2); // back to A before answering
        await g.FlushAsync();
        await b;

        Assert.Equal(3, portal.BindCount);
        Assert.Equal(PortalShortcutState.Bound, g.State);
        lock (portal.Binds)
        {
            Assert.Equal([ShortcutNames.Settings], portal.Binds[2].Shortcuts.Select(s => s.Id));
            Assert.Equal(portal.Binds[2].Session, portal.LastSession);
        }
        var pressed = new List<ushort>();
        g.Pressed += id => { lock (pressed) pressed.Add(id); };
        portal.Emit(portal.Activated(portal.LastSession!, ShortcutNames.Settings));
        await Until(() => { lock (pressed) return pressed.Count == 1; });
    }

    [DBusFact]
    public async Task ADialogNobodyAnswers_IsWithdrawn_AndNotAskedAgainForTheSameSet()
    {
        await using var portal = await FakePortal.StartAsync();
        portal.HoldBind = true;
        using var g = (await Create(approvalTimeout: TimeSpan.FromMilliseconds(200)))!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        await g.FlushAsync();

        Assert.Equal(PortalShortcutState.Declined, g.State);
        lock (portal.Binds)
        {
            Assert.Single(portal.ClosedRequests);
            Assert.Contains(portal.Binds[0].Session, portal.ClosedSessions);
        }
        await g.FlushAsync();
        Assert.Equal(1, portal.BindCount);
    }

    [DBusFact]
    public async Task AServiceNotReadyYet_IsRetried()
    {
        await using var portal = await FakePortal.StartAsync();
        portal.FailCreate = 1;
        using var g = (await Create(retryDelays: [TimeSpan.FromMilliseconds(500)]))!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        await g.FlushAsync();
        Assert.Equal(PortalShortcutState.Failed, g.State);

        await Until(() => g.State == PortalShortcutState.Bound); // the retry, on its own
        Assert.Equal(2, portal.CreateCount);
        Assert.Equal(1, portal.BindCount);
    }

    [DBusFact]
    public async Task RetriesRunOut_ThenLeftAloneUntilTheShortcutsChange()
    {
        await using var portal = await FakePortal.StartAsync();
        portal.FailCreate = 10;
        using var g = (await Create(retryDelays: [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)]))!;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        await g.FlushAsync();
        await Until(() => portal.CreateCount == 3);
        await Task.Delay(200);
        Assert.Equal(3, portal.CreateCount); // the first try and two retries, then no more
        Assert.Equal(PortalShortcutState.Failed, g.State);
        await g.FlushAsync();
        Assert.Equal(3, portal.CreateCount);

        portal.FailCreate = 0;
        Add(g, 2, ShortcutNames.MiniMode, 0x43);
        await g.FlushAsync();
        Assert.Equal(PortalShortcutState.Bound, g.State);
    }

    [DBusFact]
    public async Task PressesAfterTestHotkeysCycle_StillReachTheRightShortcut_OtherSessionsIgnored()
    {
        await using var portal = await FakePortal.StartAsync();
        using var g = (await Create())!;
        var svc = new VibeSwitcher.Services.HotkeyService(g, new FakeAppLogger(), new FakeSessionErrorTracker());
        var profile = new DeviceProfile { Name = "Gaming", Hotkey = new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true } };
        svc.RegisterAll([profile]);
        svc.RegisterSettingsHotkey(new HotkeyDefinition { VirtualKeyCode = 0x53, UseCtrl = true, UseAlt = true });
        await g.FlushAsync();
        Assert.False(svc.TestHotkey(new HotkeyDefinition { VirtualKeyCode = 0x48, UseCtrl = true, UseAlt = true }));
        await g.FlushAsync();
        Assert.Equal(1, portal.BindCount); // nothing new for the desktop to ask about

        var pressed = new List<ushort>();
        g.Pressed += id => { lock (pressed) pressed.Add(id); };
        portal.Emit(portal.Activated(portal.LastSession!, ShortcutNames.Profile(profile.Id)));
        await Until(() => { lock (pressed) return pressed.Count == 1; });
        lock (pressed) Assert.Same(profile, svc.HandleHotkey(pressed[0]));

        portal.Emit(portal.ShortcutsChanged("/org/freedesktop/portal/desktop/session/other/x", (ShortcutNames.Settings, "elsewhere")));
        portal.Emit(portal.ShortcutsChanged(portal.LastSession!, (ShortcutNames.Settings, "Super+S")));
        await Until(() => g.AssignedTriggers.GetValueOrDefault(ShortcutNames.Settings) == "Super+S");
    }

    [DBusFact]
    public async Task ClosingWhileTheDialogIsUp_IsQuiet()
    {
        await using var portal = await FakePortal.StartAsync();
        portal.HoldBind = true;
        var g = (await Create())!;
        int changes = 0;
        Add(g, 1, ShortcutNames.Settings, 0x53);
        var pending = g.FlushAsync();
        await Until(() => portal.BindCount == 1);
        g.StateChanged += () => Interlocked.Increment(ref changes);

        g.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        Assert.Empty(_log.Warnings);
        Assert.Equal(0, Volatile.Read(ref changes));
    }
}
