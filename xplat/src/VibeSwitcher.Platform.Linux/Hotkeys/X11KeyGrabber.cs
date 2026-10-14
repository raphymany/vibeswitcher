using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VibeSwitcher.Platform;

namespace VibeSwitcher.Platform.Linux.Hotkeys;

// Global hotkeys on an X11 session: key grabs on the root window — the X11 counterpart of Win32
// RegisterHotKey. Another client already grabbing the key makes the X server answer BadAccess, which
// is reported as InUse, just like Windows' "hotkey already registered".
//
// One dedicated thread owns a private X connection and makes every X call: callers hand it work and
// wake it through a pipe (so a registration takes microseconds, not a poll interval), and it waits
// for X events and wake-ups together with poll(). Pressed is raised on the thread pool, never on the
// X thread, so app code can't stall or re-enter it. Nothing here throws to callers.
//
// Each shortcut is grabbed four times — with and without Caps Lock and Num Lock — because X treats
// those as modifiers too; otherwise a shortcut would silently stop working with Num Lock on. Keyboard
// layout changes (MappingNotify) re-grab everything on the new key codes.
public sealed class X11KeyGrabber : IGlobalKeyGrabber
{
    private const uint RelevantMods = X11.ShiftMask | X11.ControlMask | X11.Mod1Mask | X11.Mod4Mask;

    // What stock evdev layouts put on the F13–F24 keys (or nothing), plus F13–F24 themselves.
    private static readonly HashSet<ulong> F13To24StandIns =
    [
        0, 0x1008FF81, 0x1008FF45, 0x1008FF46, 0x1008FF47, 0x1008FF48, 0x1008FF49, 0x1008FFB2, 0x1008FFA9,
        0x1008FFB0, 0x1008FFB1, .. Enumerable.Range(0xFFCA, 12).Select(k => (ulong)k),
    ];
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly IntPtr _display;
    private readonly IntPtr _xcb;
    private readonly UIntPtr _root;
    private readonly Thread _thread;
    private readonly int[] _wake = new int[2];
    private readonly ConcurrentQueue<Action> _requests = new();
    // Worker thread only: id → what was asked for, and the key code it currently grabs.
    private readonly Dictionary<ushort, (int Vk, uint Mods, byte Keycode)> _grabs = new();
    private uint _numLockMask;
    private volatile bool _stop;
    private int _disposed;

    public event Action<ushort>? Pressed;

    private X11KeyGrabber(IntPtr display)
    {
        _display = display;
        _xcb = X11.XGetXCBConnection(display);
        _root = X11.XDefaultRootWindow(display);
        // Close-on-exec: programs the app starts (pactl, launched apps) mustn't inherit it.
        if (LibC.pipe2(_wake, LibC.O_CLOEXEC | LibC.O_NONBLOCK) != 0) throw new InvalidOperationException("pipe2() failed");
        _numLockMask = NumLockMask();
        _thread = new Thread(Run) { IsBackground = true, Name = "VibeSwitcher X11 hotkeys" };
        _thread.Start();
    }

    // True on a Wayland session: an X11 grab there only sees keys while an X11 window has focus, so
    // it isn't a global hotkey — the Wayland path must be used instead.
    public static bool IsWaylandSession(Func<string, string?> env) =>
        string.Equals(env("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(env("WAYLAND_DISPLAY"));

    // Null when there's no X server to talk to (no $DISPLAY, or the X libraries are missing).
    // (XInitThreads must be Xlib's first call in the process on libX11 < 1.8; the UI toolkit makes it
    // at startup, so it's a no-op here — and on newer libX11 it's automatic.)
    public static X11KeyGrabber? TryCreate()
    {
        try
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return null;
            X11.XInitThreads();
            var display = X11.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;
            try { return new X11KeyGrabber(display); }
            catch { X11.XCloseDisplay(display); throw; }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { return null; }
    }

    public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error)
    {
        var (result, message) = Invoke(() => RegisterOnThread(id, modifiers, virtualKey),
            onLate: r => { if (r.Item1 == GrabResult.Ok) UngrabOnThread(id); }, // an abandoned grab is undone
            fallback: (GrabResult.Failed, "the X server didn't answer"));
        error = message;
        return result;
    }

    public void Unregister(ushort id) => Invoke(() => { UngrabOnThread(id); return true; }, onLate: null, fallback: false);

    // Worker thread: what the user wants, on whichever key code the current layout uses.
    private (GrabResult, string?) RegisterOnThread(ushort id, int modifiers, int virtualKey)
    {
        UngrabOnThread(id);
        if (X11Keysyms.FromVirtualKey(virtualKey) == null)
            return (GrabResult.Unsupported, $"key code {virtualKey} has no X11 equivalent");
        byte keycode = KeycodeFor(virtualKey);
        if (keycode == 0) return (GrabResult.Unsupported, "this keyboard layout has no key for it");
        uint mods = X11Keysyms.Modifiers(modifiers);

        // The same shortcut twice in this app is a conflict too, as on Windows.
        if (_grabs.Values.Any(g => g.Keycode == keycode && g.Mods == mods)) return (GrabResult.InUse, null);

        var (result, error) = Grab(keycode, mods);
        if (result == GrabResult.Ok) _grabs[id] = (virtualKey, mods, keycode);
        return (result, error);
    }

    private byte KeycodeFor(int vk)
    {
        if (X11Keysyms.FromVirtualKey(vk) is not uint keysym) return 0;
        byte keycode = X11.XKeysymToKeycode(_display, (UIntPtr)keysym);
        // Stock layouts put XF86 keysyms (Tools, Launch5…) on the F13–F24 keys rather than F13–F24
        // themselves; those keys are evdev KEY_F13..KEY_F24 = X key codes 191–202. Only used when that
        // key code really carries one of those stand-in symbols (other servers number keys differently).
        if (keycode == 0 && vk is >= 0x7C and <= 0x87)
        {
            byte candidate = (byte)(191 + vk - 0x7C);
            ulong sym = (ulong)X11.XkbKeycodeToKeysym(_display, candidate, 0, 0);
            if (F13To24StandIns.Contains(sym)) keycode = candidate;
        }
        return keycode;
    }

    private uint[] LockVariants() => [0, X11.LockMask, _numLockMask, X11.LockMask | _numLockMask];

    private (GrabResult, string?) Grab(byte keycode, uint mods)
    {
        var cookies = LockVariants().Distinct()
            .Select(extra => X11.xcb_grab_key_checked(_xcb, 1, (uint)_root, (ushort)(mods | extra), keycode,
                X11.GrabModeAsync, X11.GrabModeAsync))
            .ToList();
        byte error = 0;
        foreach (var cookie in cookies)
        {
            var e = X11.xcb_request_check(_xcb, cookie);
            if (e == IntPtr.Zero) continue;
            if (error == 0) error = Marshal.ReadByte(e, X11.XcbErrorCodeOffset);
            LibC.free(e);
        }
        if (error == 0) return (GrabResult.Ok, null);
        // Release whichever variants did get grabbed.
        foreach (var extra in LockVariants()) X11.XUngrabKey(_display, keycode, mods | extra, _root);
        X11.XSync(_display, 0);
        return error == X11.BadAccess ? (GrabResult.InUse, null) : (GrabResult.Failed, $"X error {error}");
    }

    private void UngrabOnThread(ushort id)
    {
        if (!_grabs.Remove(id, out var g)) return;
        foreach (var extra in LockVariants()) X11.XUngrabKey(_display, g.Keycode, g.Mods | extra, _root);
        X11.XSync(_display, 0);
    }

    // Num Lock is Mod2 on nearly every layout, but ask the server which modifier it really is.
    private uint NumLockMask()
    {
        byte numLock = X11.XKeysymToKeycode(_display, (UIntPtr)X11.XK_Num_Lock);
        var map = X11.XGetModifierMapping(_display);
        if (map == IntPtr.Zero || numLock == 0) return 16;
        try
        {
            int perMod = Marshal.ReadInt32(map);
            var keys = Marshal.ReadIntPtr(map, X11.ModmapKeysOffset);
            for (int mod = 0; mod < 8; mod++)
                for (int k = 0; k < perMod; k++)
                    if (Marshal.ReadByte(keys, mod * perMod + k) == numLock) return 1u << mod;
            return 16;
        }
        finally { X11.XFreeModifiermap(map); }
    }

    // After a layout change: refresh, then grab every shortcut again on its new key code.
    private void Regrab()
    {
        var wanted = _grabs.ToList();
        foreach (var (id, _) in wanted) UngrabOnThread(id);
        _numLockMask = NumLockMask();
        // (A shortcut whose key no longer exists on the new layout, or that now lands on the same key as
        // another one, stays off until the app registers its shortcuts again.)
        foreach (var (id, g) in wanted)
        {
            byte keycode = KeycodeFor(g.Vk);
            if (keycode == 0 || _grabs.Values.Any(o => o.Keycode == keycode && o.Mods == g.Mods)) continue;
            if (Grab(keycode, g.Mods).Item1 == GrabResult.Ok) _grabs[id] = (g.Vk, g.Mods, keycode);
        }
    }

    // For tests: the pointer's modifier state (shows whether Num Lock / Caps Lock are on).
    internal uint CurrentModifierState() => Invoke(() =>
    {
        X11.XQueryPointer(_display, _root, out _, out _, out _, out _, out _, out _, out uint mask);
        return mask;
    }, onLate: null, fallback: 0u);

    internal uint NumLockModifier => _numLockMask;

    // For tests: the key code a registration grabbed (0 if none).
    internal byte GrabbedKeycode(ushort id) => Invoke(() => _grabs.TryGetValue(id, out var g) ? g.Keycode : (byte)0, onLate: null, fallback: (byte)0);

    // Runs `work` on the X thread and waits for it. If the caller gives up (timeout) or the grabber is
    // gone, the work is skipped — or, if it already ran, `onLate` undoes it — and `fallback` is returned.
    private T Invoke<T>(Func<T> work, Action<T>? onLate, T fallback)
    {
        if (Volatile.Read(ref _disposed) != 0) return fallback;
        if (Thread.CurrentThread == _thread) return work();
        // 0 = pending, 1 = the worker finished it, 2 = the caller gave up. Whoever moves it first owns
        // the outcome: a result the caller no longer wants is undone by the worker (onLate).
        int state = 0;
        T result = fallback;
        using var finished = new ManualResetEventSlim();
        _requests.Enqueue(() =>
        {
            if (Volatile.Read(ref state) != 0 || Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.CompareExchange(ref state, 2, 0);
                SetQuietly(finished);
                return;
            }
            T r;
            try { r = work(); }
            catch (Exception) { r = fallback; }
            if (Interlocked.CompareExchange(ref state, 1, 0) == 0)
            {
                result = r;
                SetQuietly(finished);
            }
            else onLate?.Invoke(r);
        });
        // Disposed meanwhile? The worker may already have drained the queue and left.
        if (Volatile.Read(ref _disposed) != 0) Interlocked.CompareExchange(ref state, 2, 0);
        else Wake();
        if (Volatile.Read(ref state) == 0) finished.Wait(CallTimeout);
        return Interlocked.CompareExchange(ref state, 2, 0) == 1 ? result : fallback;
    }

    private static void SetQuietly(ManualResetEventSlim e)
    {
        try { e.Set(); } catch (ObjectDisposedException) { /* the caller already returned */ }
    }

    // Writes to the wake pipe only while it's open (closing happens under the same lock), so a late
    // caller can never write into a reused file descriptor.
    private readonly object _wakeGate = new();
    private bool _pipeClosed;

    private void Wake()
    {
        lock (_wakeGate)
        {
            if (_pipeClosed) return;
            try { LibC.write(_wake[1], [1], 1); } catch (Exception) { }
        }
    }

    private void Run()
    {
        var buffer = Marshal.AllocHGlobal(X11.XEventSize);
        var fds = new[]
        {
            new LibC.PollFd { Fd = X11.XConnectionNumber(_display), Events = LibC.PollIn },
            new LibC.PollFd { Fd = _wake[0], Events = LibC.PollIn },
        };
        var drain = new byte[64];
        try
        {
            while (!_stop)
            {
                try
                {
                    while (_requests.TryDequeue(out var request)) request();
                    var fired = new List<ushort>();
                    while (!_stop && X11.XPending(_display) > 0)
                    {
                        X11.XNextEvent(_display, buffer);
                        int type = Marshal.ReadInt32(buffer);
                        if (type == X11.MappingNotify)
                        {
                            X11.XRefreshKeyboardMapping(buffer);
                            Regrab();
                            continue;
                        }
                        if (type != X11.KeyPress) continue;
                        uint state = unchecked((uint)Marshal.ReadInt32(buffer, X11.KeyEventStateOffset));
                        byte keycode = (byte)Marshal.ReadInt32(buffer, X11.KeyEventKeycodeOffset);
                        foreach (var (id, g) in _grabs)
                            if (g.Keycode == keycode && g.Mods == (state & RelevantMods)) fired.Add(id);
                    }
                    // App code runs off this thread (it may call back into the grabber, or block), one
                    // press after another in the order they happened.
                    foreach (var id in fired) _presses.Enqueue(id);
                    if (fired.Count > 0 && Interlocked.Exchange(ref _delivering, 1) == 0)
                        ThreadPool.QueueUserWorkItem(_ => DeliverPresses());

                    fds[0].Revents = fds[1].Revents = 0;
                    LibC.poll(fds, 2, 1000);
                    if ((fds[1].Revents & LibC.PollIn) != 0)
                        while (LibC.read(_wake[0], drain, (nuint)drain.Length) > 0) { }
                }
                catch (Exception) { /* keep the hotkey thread alive whatever happens */ }
            }
        }
        finally
        {
            // Anyone still waiting gets their fallback straight away (the requests see the disposal and cancel).
            while (_requests.TryDequeue(out var pending)) { try { pending(); } catch (Exception) { } }
            Marshal.FreeHGlobal(buffer);
            X11.XCloseDisplay(_display); // releases every grab
            lock (_wakeGate)
            {
                _pipeClosed = true;
                LibC.close(_wake[0]);
                LibC.close(_wake[1]);
            }
        }
    }

    private readonly ConcurrentQueue<ushort> _presses = new();
    private int _delivering;

    private void DeliverPresses()
    {
        while (true)
        {
            while (_presses.TryDequeue(out var id))
            {
                if (Volatile.Read(ref _disposed) != 0) continue;
                try { Pressed?.Invoke(id); } catch (Exception) { /* the app logs its own handler errors */ }
            }
            Volatile.Write(ref _delivering, 0);
            // A press queued while we were finishing up: carry on (unless another delivery took over).
            if (_presses.IsEmpty || Interlocked.Exchange(ref _delivering, 1) == 1) return;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _stop = true;
        Wake();
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
    }
}
