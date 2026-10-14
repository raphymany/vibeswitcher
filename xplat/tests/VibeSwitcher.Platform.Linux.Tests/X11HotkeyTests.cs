using System.Diagnostics;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hotkeys;

namespace VibeSwitcher.Platform.Linux.Tests;

public class X11KeysymTests
{
    [Theory]
    [InlineData(0x41, 0x61u)]       // A → a
    [InlineData(0x5A, 0x7Au)]       // Z → z
    [InlineData(0x30, 0x30u)]       // 0
    [InlineData(0x70, 0xFFBEu)]     // F1
    [InlineData(0x87, 0xFFD5u)]     // F24
    [InlineData(0x60, 0xFFB0u)]     // numpad 0
    [InlineData(0x25, 0xFF51u)]     // Left
    [InlineData(0xBA, 0x3Bu)]       // ;
    [InlineData(0xDE, 0x27u)]       // '
    [InlineData(0xAD, 0x1008FF12u)] // Volume Mute
    public void WindowsKeyToKeysym(int vk, uint keysym) => Assert.Equal(keysym, X11Keysyms.FromVirtualKey(vk));

    [Fact]
    public void EveryKeyTheWindowsAppNames_HasAnX11Equivalent_OrIsAModifier()
    {
        // The key-name table generated from Windows (VirtualKeyMap) lists every key a shortcut can use.
        var modifiersAndOddities = new HashSet<int>
        {
            0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5,       // modifiers themselves
            0x15, 0x17, 0x18, 0x19, 0x1C, 0x1D, 0x1E, 0x1F, 0xE5,                    // input-method (IME) keys
            0xC1, 0xC2, 0xDF,                                                        // Brazilian ABNT / OEM_8 extras
            0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFB, 0xFC, 0xFD, 0xFE, // legacy terminal / IME keys
            0x2A, 0xFA, // VK_PRINT / VK_PLAY: X has one keysym each for these and Print Screen / Play-Pause
        };
        var missing = Enumerable.Range(1, 254)
            .Where(vk => Keys.VirtualKeyMap.GetName(vk) != null && !modifiersAndOddities.Contains(vk) && X11Keysyms.FromVirtualKey(vk) == null)
            .Select(vk => $"0x{vk:X2} {Keys.VirtualKeyMap.GetName(vk)}").ToList();
        Assert.True(missing.Count == 0, "No X11 keysym for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Modifiers()
    {
        Assert.Equal(X11.ControlMask | X11.Mod1Mask, X11Keysyms.Modifiers(HotkeyDefinition.MOD_CTRL | HotkeyDefinition.MOD_ALT));
        Assert.Equal(X11.ShiftMask | X11.Mod4Mask, X11Keysyms.Modifiers(HotkeyDefinition.MOD_SHIFT | HotkeyDefinition.MOD_WIN));
        Assert.Equal(0u, X11Keysyms.Modifiers(0));
    }
}

// Real X server (Xvfb in CI): grabs, key presses simulated with xdotool, and conflicts between two
// X clients. Runs with VS_X11_IT=1 under an X server (xvfb-run).
public sealed class X11FactAttribute : FactAttribute
{
    public X11FactAttribute()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("VS_X11_IT") != "1"
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            Skip = "Needs an X server: VS_X11_IT=1 xvfb-run -a dotnet test (Linux, with xdotool).";
    }
}

[Trait("Category", "X11")]
public sealed class X11KeyGrabberTests : IDisposable
{
    private const int Ctrl = HotkeyDefinition.MOD_CTRL, Alt = HotkeyDefinition.MOD_ALT, Shift = HotkeyDefinition.MOD_SHIFT;
    private readonly List<IDisposable> _cleanup = [];

    public void Dispose()
    {
        foreach (var d in _cleanup) d.Dispose();
    }

    private X11KeyGrabber Grabber()
    {
        var g = X11KeyGrabber.TryCreate() ?? throw new InvalidOperationException("no X display");
        _cleanup.Add(g);
        return g;
    }

    private static void Xdotool(params string[] args)
    {
        using var p = Process.Start(new ProcessStartInfo("xdotool", args) { RedirectStandardError = true })!;
        Assert.True(p.WaitForExit(5000), "xdotool hung");
        Assert.True(p.ExitCode == 0, "xdotool " + string.Join(' ', args) + ": " + p.StandardError.ReadToEnd());
    }

    [System.Runtime.InteropServices.DllImport("libXtst.so.6")]
    private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, ulong delay);
    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr name);
    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);
    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keysym);
    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    private static void PressKeycode(byte keycode, bool withCtrl)
    {
        Assert.NotEqual(0, keycode);
        var d = XOpenDisplay(IntPtr.Zero);
        try
        {
            byte ctrl = XKeysymToKeycode(d, (UIntPtr)0xFFE3); // Control_L
            if (withCtrl) XTestFakeKeyEvent(d, ctrl, 1, 0);
            XTestFakeKeyEvent(d, keycode, 1, 0);
            XTestFakeKeyEvent(d, keycode, 0, 0);
            if (withCtrl) XTestFakeKeyEvent(d, ctrl, 0, 0);
            XFlush(d);
        }
        finally { XCloseDisplay(d); }
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (condition()) return true; await Task.Delay(20); }
        return condition();
    }

    [X11Fact]
    public async Task PressingTheShortcut_RaisesPressed_WithItsId()
    {
        var g = Grabber();
        var pressed = new List<ushort>();
        g.Pressed += id => { lock (pressed) pressed.Add(id); };

        Assert.Equal(GrabResult.Ok, g.Register(7, Ctrl | Alt, 0x47, out _)); // Ctrl+Alt+G
        Assert.Equal(GrabResult.Ok, g.Register(8, Ctrl | Shift, 0x75, out _)); // Ctrl+Shift+F6

        Xdotool("key", "ctrl+alt+g");
        Assert.True(await WaitFor(() => { lock (pressed) return pressed.Contains(7); }), "Ctrl+Alt+G never arrived");
        Xdotool("key", "ctrl+shift+F6");
        Assert.True(await WaitFor(() => { lock (pressed) return pressed.Contains(8); }), "Ctrl+Shift+F6 never arrived");
        Xdotool("key", "ctrl+g"); // not registered
        await Task.Delay(200);
        lock (pressed) Assert.Equal([7, 8], pressed);
    }

    [X11Fact]
    public async Task WorksWithNumLockOn()
    {
        var g = Grabber();
        int hits = 0;
        g.Pressed += _ => Interlocked.Increment(ref hits);
        g.Register(1, Ctrl | Alt, 0x4A, out _); // Ctrl+Alt+J

        Xdotool("key", "Num_Lock");
        try
        {
            Assert.True(await WaitFor(() => (g.CurrentModifierState() & g.NumLockModifier) != 0), "Num Lock didn't turn on");
            Xdotool("key", "ctrl+alt+j");
            Assert.True(await WaitFor(() => Volatile.Read(ref hits) == 1), "lost with Num Lock on");
        }
        finally { Xdotool("key", "Num_Lock"); }
    }

    [X11Fact]
    public async Task WorksWithCapsLockOn()
    {
        var g = Grabber();
        int hits = 0;
        g.Pressed += _ => Interlocked.Increment(ref hits);
        g.Register(1, Ctrl | Alt, 0x48, out _); // Ctrl+Alt+H

        Xdotool("key", "Caps_Lock");
        try
        {
            Assert.True(await WaitFor(() => (g.CurrentModifierState() & X11.LockMask) != 0), "Caps Lock didn't turn on");
            Xdotool("key", "ctrl+alt+h");
            Assert.True(await WaitFor(() => Volatile.Read(ref hits) == 1), "lost with Caps Lock on");
        }
        finally { Xdotool("key", "Caps_Lock"); }
    }

    [X11Fact]
    public void ManyRegistrations_AreFast()
    {
        // Capturing a shortcut in Settings re-registers every own shortcut; that must not freeze the UI.
        var g = Grabber();
        var sw = Stopwatch.StartNew();
        for (ushort i = 1; i <= 40; i++)
        {
            Assert.Equal(GrabResult.Ok, g.Register(i, Ctrl | Alt | Shift, 0x41 + (i % 26), out _));
            g.Unregister(i);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(800), $"80 calls took {sw.ElapsedMilliseconds} ms");
    }

    [X11Fact]
    public async Task AHandlerThatRegistersAnotherShortcut_IsFine()
    {
        var g = Grabber();
        int second = 0, registered = 0;
        g.Pressed += id =>
        {
            if (id == 1 && g.Register(2, Ctrl | Alt, 0x52, out _) == GrabResult.Ok) // Ctrl+Alt+R, from inside the handler
                Volatile.Write(ref registered, 1);
            if (id == 2) Interlocked.Increment(ref second);
        };
        g.Register(1, Ctrl | Alt, 0x51, out _); // Ctrl+Alt+Q

        Xdotool("key", "ctrl+alt+q");
        Assert.True(await WaitFor(() => Volatile.Read(ref registered) == 1), "the handler could not register");
        Xdotool("key", "ctrl+alt+r");
        Assert.True(await WaitFor(() => Volatile.Read(ref second) == 1), "the shortcut registered from the handler never worked");
    }

    [X11Fact]
    public async Task F13ToF24_WorkOnStockLayouts()
    {
        // Stock layouts put other symbols on those keys (F13 carries XF86Tools); the grabber falls back
        // to their key codes, so pressing the physical key fires.
        var g = Grabber();
        int hits = 0;
        g.Pressed += id => { if (id == 1) Interlocked.Increment(ref hits); };
        Assert.True(g.Register(1, Ctrl, 0x7C, out var e13) == GrabResult.Ok, e13); // F13
        Assert.True(g.Register(2, Ctrl, 0x87, out var e24) == GrabResult.Ok, e24); // F24
        // Press the physical key the grab is on (by key code, as a keyboard would), with Ctrl held.
        PressKeycode(g.GrabbedKeycode(1), withCtrl: true);
        Assert.True(await WaitFor(() => Volatile.Read(ref hits) == 1), "F13 never arrived");
    }

    [X11Fact]
    public void AfterDispose_CallsReturnQuietly()
    {
        var g = X11KeyGrabber.TryCreate()!;
        g.Register(1, Ctrl | Alt, 0x53, out _);
        g.Dispose();
        var sw = Stopwatch.StartNew();
        Assert.Equal(GrabResult.Failed, g.Register(2, Ctrl | Alt, 0x54, out _));
        g.Unregister(1); // no-op
        g.Dispose();     // twice is fine
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
    }

    [X11Fact]
    public void AShortcutAnotherClientHolds_IsInUse()
    {
        var other = Grabber(); // a separate X connection = another application to the X server
        Assert.Equal(GrabResult.Ok, other.Register(1, Ctrl | Alt, 0x4B, out _));

        var us = Grabber();
        Assert.Equal(GrabResult.InUse, us.Register(1, Ctrl | Alt, 0x4B, out _));
        Assert.Equal(GrabResult.Ok, us.Register(2, Ctrl | Alt, 0x4C, out _)); // a different one is fine

        other.Unregister(1);
        Assert.Equal(GrabResult.Ok, us.Register(1, Ctrl | Alt, 0x4B, out _)); // free again
    }

    [X11Fact]
    public void SameShortcutTwiceInOneApp_IsInUse_LikeWindows()
    {
        var g = Grabber();
        Assert.Equal(GrabResult.Ok, g.Register(1, Ctrl | Alt, 0x4E, out _));
        Assert.Equal(GrabResult.InUse, g.Register(2, Ctrl | Alt, 0x4E, out _));
    }

    [X11Fact]
    public async Task Unregistered_NoLongerFires()
    {
        var g = Grabber();
        int hits = 0;
        g.Pressed += _ => Interlocked.Increment(ref hits);
        g.Register(3, Ctrl | Alt, 0x50, out _); // Ctrl+Alt+P
        g.Unregister(3);
        Xdotool("key", "ctrl+alt+p");
        await Task.Delay(300);
        Assert.Equal(0, hits);
    }

    [X11Fact]
    public void UnknownKey_IsUnsupported()
    {
        var g = Grabber();
        Assert.Equal(GrabResult.Unsupported, g.Register(1, Ctrl, 0xE5, out var error)); // IME "process key"
        Assert.NotNull(error);
    }

    [X11Fact]
    public async Task ThroughTheHotkeyService_EndToEnd()
    {
        var g = Grabber();
        using var svc = new VibeSwitcher.Services.HotkeyService(g, new VibeSwitcher.Tests.FakeAppLogger(), new VibeSwitcher.Tests.FakeSessionErrorTracker());
        var profile = new DeviceProfile { Name = "Gaming", Hotkey = new HotkeyDefinition { VirtualKeyCode = 0x31, UseCtrl = true, UseAlt = true } };
        Assert.Empty(svc.RegisterAll([profile]));
        DeviceProfile? switched = null;
        g.Pressed += id => switched = svc.HandleHotkey(id);

        Xdotool("key", "ctrl+alt+1");
        Assert.True(await WaitFor(() => switched != null));
        Assert.Same(profile, switched);
        Assert.False(svc.TestHotkey(profile.Hotkey)); // ours, so not "taken"
    }
}
