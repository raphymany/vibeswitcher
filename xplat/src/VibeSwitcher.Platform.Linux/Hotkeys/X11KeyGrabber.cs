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
        if (LibC.pipe(_wake) != 0) throw new InvalidOperationException("pipe() failed");
        LibC.fcntl(_wake[0], LibC.F_SETFL, LibC.O_NONBLOCK_LINUX);
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
        // themselves; those keys are evdev KEY_F13..KEY_F24 = X key codes 191–202.
        if (keycode == 0 && vk is >= 0x7C and <= 0x87) keycode = (byte)(191 + vk - 0x7C);
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
        foreach (var (id, g) in wanted)
        {
            byte keycode = KeycodeFor(g.Vk);
            if (keycode != 0 && Grab(keycode, g.Mods).Item1 == GrabResult.Ok) _grabs[id] = (g.Vk, g.Mods, keycode);
        }
    }

    // For tests: the pointer's modifier state (shows whether Num Lock / Caps Lock are on).
    internal uint CurrentModifierState() => Invoke(() =>
    {
        X11.XQueryPointer(_display, _root, out _, out _, out _, out _, out _, out _, out uint mask);
        return mask;
    }, onLate: null, fallback: 0u);

    internal uint NumLockModifier => _numLockMask;

    // Runs `work` on the X thread and waits for it. If the caller gives up (timeout) or the grabber is
    // gone, the work is skipped — or, if it already ran, `onLate` undoes it — and `fallback` is returned.
    private T Invoke<T>(Func<T> work, Action<T>? onLate, T fallback)
    {
        if (Volatile.Read(ref _disposed) != 0) return fallback;
        if (Thread.CurrentThread == _thread) return work();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        int abandoned = 0;
        _requests.Enqueue(() =>
        {
            if (Volatile.Read(ref abandoned) != 0 || Volatile.Read(ref _disposed) != 0) { done.TrySetCanceled(); return; }
            try
            {
                var r = work();
                if (!done.TrySetResult(r) || Volatile.Read(ref abandoned) != 0) onLate?.Invoke(r);
            }
            catch (Exception ex) { done.TrySetException(ex); }
        });
        Wake();
        try
        {
            if (done.Task.Wait(CallTimeout)) return done.Task.Result;
        }
        catch (AggregateException) { }
        Volatile.Write(ref abandoned, 1);
        return fallback;
    }

    private void Wake()
    {
        try { LibC.write(_wake[1], [1], 1); } catch (Exception) { }
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
                    // App code runs off this thread: it may call back into the grabber, or block.
                    foreach (var id in fired)
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            if (Volatile.Read(ref _disposed) != 0) return;
                            try { Pressed?.Invoke(id); } catch (Exception) { /* the app logs its own handler errors */ }
                        });

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
            LibC.close(_wake[0]);
            LibC.close(_wake[1]);
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
