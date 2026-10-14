using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Core.Tests;

// The forked HotkeyService: the Windows bookkeeping on a platform key grabber.
public class HotkeyServiceForkTests
{
    // Behaves like the OS: a shortcut held by another app (or already registered) can't be taken.
    private sealed class FakeGrabber : IGlobalKeyGrabber
    {
        public readonly HashSet<(int Mods, int Vk)> TakenByOtherApps = [];
        public readonly HashSet<int> Unsupported = [];
        public readonly Dictionary<ushort, (int Mods, int Vk)> Registered = [];
        public readonly Dictionary<ushort, string> Names = [];
        public readonly List<string> UndescribedRegistrations = [];
        public int Calls;
        public event Action<ushort>? Pressed;

        public void Describe(ushort id, string name, string description)
        {
            Assert.False(string.IsNullOrWhiteSpace(description));
            Names[id] = name;
        }

        public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error)
        {
            Calls++;
            if (!Names.ContainsKey(id)) UndescribedRegistrations.Add($"{id}");
            error = null;
            if (Unsupported.Contains(virtualKey)) { error = "no such key here"; return GrabResult.Unsupported; }
            if (TakenByOtherApps.Contains((modifiers, virtualKey)) || Registered.ContainsValue((modifiers, virtualKey)))
                return GrabResult.InUse;
            Registered[id] = (modifiers, virtualKey);
            return GrabResult.Ok;
        }

        public void Unregister(ushort id) => Registered.Remove(id);
        public void Press(ushort id) => Pressed?.Invoke(id);
        public ushort IdOf(HotkeyDefinition hk) => Registered.Single(r => r.Value == (hk.GetModifierFlags(), hk.VirtualKeyCode)).Key;
        public void Dispose() { }
    }

    private readonly FakeGrabber _grabber = new();
    private readonly FakeSessionErrorTracker _errors = new();
    private readonly HotkeyService _svc;

    public HotkeyServiceForkTests() => _svc = new HotkeyService(_grabber, new FakeAppLogger(), _errors);

    private static HotkeyDefinition Key(int vk, bool ctrl = true, bool alt = true) => new() { VirtualKeyCode = vk, UseCtrl = ctrl, UseAlt = alt };
    private static DeviceProfile Profile(string name, HotkeyDefinition hk) => new() { Name = name, Hotkey = hk };

    [Fact]
    public void RegisterAll_RegistersEach_AndDispatchesByTheirIds()
    {
        var a = Profile("A", Key(0x41));
        var b = Profile("B", Key(0x42));
        Assert.Empty(_svc.RegisterAll([a, b, Profile("No key", new HotkeyDefinition())]));
        Assert.Equal(2, _grabber.Registered.Count);

        Assert.Same(a, _svc.HandleHotkey(_grabber.IdOf(a.Hotkey)));
        Assert.Same(b, _svc.HandleHotkey(_grabber.IdOf(b.Hotkey)));
        Assert.Null(_svc.HandleHotkey(999));
    }

    [Fact]
    public void Conflicts_AreCollected_OthersStillRegistered()
    {
        _grabber.TakenByOtherApps.Add((Key(0x41).GetModifierFlags(), 0x41));
        var conflicts = _svc.RegisterAll([Profile("A", Key(0x41)), Profile("B", Key(0x42)), Profile("B2", Key(0x42))]);

        Assert.Equal(2, conflicts.Count); // another app's, and the same shortcut twice in this app (as Windows)
        Assert.Single(_grabber.Registered);
    }

    [Fact]
    public void UnsupportedKey_RecordsARegistrationFailure()
    {
        _grabber.Unsupported.Add(0xAD);
        Assert.Empty(_svc.RegisterAll([Profile("Mute key", Key(0xAD))]));
        Assert.Equal(ErrorCode.HotkeyRegistrationFailed, Assert.Single(_errors.Errors).Code);
    }

    [Fact]
    public void SettingsCompactMute_EachRegisteredAndRecognised()
    {
        Assert.Null(_svc.RegisterSettingsHotkey(Key(0x53)));
        Assert.Null(_svc.RegisterCompactHotkey(Key(0x43)));
        Assert.Null(_svc.RegisterMuteHotkey(MuteScope.Mic, Key(0x4D)));

        Assert.True(_svc.IsSettingsHotkey(_grabber.IdOf(Key(0x53))));
        Assert.True(_svc.IsCompactHotkey(_grabber.IdOf(Key(0x43))));
        Assert.True(_svc.IsMuteHotkey(_grabber.IdOf(Key(0x4D)), out var scope));
        Assert.Equal(MuteScope.Mic, scope);
        Assert.False(_svc.IsSettingsHotkey(_grabber.IdOf(Key(0x43))));

        _grabber.TakenByOtherApps.Add((Key(0x50).GetModifierFlags(), 0x50));
        Assert.NotNull(_svc.RegisterSettingsHotkey(Key(0x50))); // conflict returned, like Windows
        Assert.False(_svc.IsSettingsHotkey(1));
    }

    [Fact]
    public void TestHotkey_IgnoresOwnShortcuts_DetectsOtherApps_AndRestoresEverything()
    {
        var a = Profile("A", Key(0x41));
        _svc.RegisterAll([a]);
        _svc.RegisterSettingsHotkey(Key(0x53));
        _svc.RegisterMuteHotkey(MuteScope.Both, Key(0x4D));
        var before = _grabber.Registered.ToDictionary();

        Assert.False(_svc.TestHotkey(Key(0x41))); // ours, not another app's
        _grabber.TakenByOtherApps.Add((Key(0x47).GetModifierFlags(), 0x47));
        Assert.True(_svc.TestHotkey(Key(0x47)));
        Assert.False(_svc.TestHotkey(Key(0x48))); // free

        Assert.Equal(before, _grabber.Registered.ToDictionary()); // same ids, same keys
        Assert.Same(a, _svc.HandleHotkey(_grabber.IdOf(a.Hotkey)));
        Assert.True(_svc.IsSettingsHotkey(_grabber.IdOf(Key(0x53))));
    }

    [Fact]
    public void EveryShortcut_IsDescribedWithItsStableName_BeforeRegistering()
    {
        var a = Profile("A", Key(0x41));
        _svc.RegisterAll([a]);
        _svc.RegisterSettingsHotkey(Key(0x53));
        _svc.RegisterCompactHotkey(Key(0x43));
        _svc.RegisterMuteHotkey(MuteScope.Mic, Key(0x4D));

        Assert.Equal(ShortcutNames.Profile(a.Id), _grabber.Names[_grabber.IdOf(a.Hotkey)]);
        Assert.Equal(ShortcutNames.Settings, _grabber.Names[_grabber.IdOf(Key(0x53))]);
        Assert.Equal(ShortcutNames.MiniMode, _grabber.Names[_grabber.IdOf(Key(0x43))]);
        Assert.Equal(ShortcutNames.Mute(MuteScope.Mic), _grabber.Names[_grabber.IdOf(Key(0x4D))]);

        // TestHotkey's check is marked as a probe (so a desktop-owned grabber can skip it), and the
        // shortcuts it re-takes afterwards keep their names.
        _svc.TestHotkey(Key(0x48));
        Assert.Contains(ShortcutNames.Probe, _grabber.Names.Values);
        Assert.Empty(_grabber.UndescribedRegistrations);
        Assert.Equal(ShortcutNames.Profile(a.Id), _grabber.Names[_grabber.IdOf(a.Hotkey)]);
    }

    [Fact]
    public void TestHotkey_LosingAShortcutMeanwhile_DropsItCleanly()
    {
        var a = Profile("A", Key(0x41));
        _svc.RegisterAll([a]);
        var id = _grabber.IdOf(a.Hotkey);
        // While it's released for the test, another app grabs it, so re-registering fails.
        _grabber.TakenByOtherApps.Add((Key(0x41).GetModifierFlags(), 0x41));

        _svc.TestHotkey(Key(0x42));
        Assert.Null(_svc.HandleHotkey(id));
        Assert.Empty(_grabber.Registered);
    }

    [Fact]
    public void TestHotkey_AKeyThisSystemCantUse_IsNotBlamedOnAnotherApp()
    {
        _grabber.Unsupported.Add(0xAD);
        Assert.False(_svc.TestHotkey(Key(0xAD)));
    }

    [Fact]
    public void TestHotkey_SettingsOrMiniModeLostMeanwhile_IsClearedCleanly()
    {
        _svc.RegisterSettingsHotkey(Key(0x53));
        _svc.RegisterCompactHotkey(Key(0x43));
        _svc.RegisterMuteHotkey(MuteScope.Mic, Key(0x4D));
        _grabber.TakenByOtherApps.Add((Key(0x53).GetModifierFlags(), 0x53));
        _grabber.TakenByOtherApps.Add((Key(0x43).GetModifierFlags(), 0x43));

        _svc.TestHotkey(Key(0x48));

        Assert.Single(_grabber.Registered); // only the mute shortcut came back
        Assert.True(_svc.IsMuteHotkey(_grabber.Registered.Keys.Single(), out _));
        Assert.Null(_svc.RegisterSettingsHotkey(new HotkeyDefinition())); // state is clean: no stale id
    }

    [Fact]
    public void SettingsMuteCompact_UnsupportedKey_NoConflict_NothingRegistered()
    {
        _grabber.Unsupported.Add(0xAD);
        Assert.Null(_svc.RegisterSettingsHotkey(Key(0xAD)));
        Assert.Null(_svc.RegisterCompactHotkey(Key(0xAD)));
        Assert.Null(_svc.RegisterMuteHotkey(MuteScope.Speakers, Key(0xAD)));
        Assert.Empty(_grabber.Registered);
    }

    [Fact]
    public void UnregisterProfile_AndUnregisterAll_ReleaseEverything()
    {
        var a = Profile("A", Key(0x41));
        var b = Profile("B", Key(0x42));
        _svc.RegisterAll([a, b]);
        _svc.RegisterCompactHotkey(Key(0x43));

        _svc.UnregisterProfile(a.Id);
        Assert.Equal(2, _grabber.Registered.Count);
        _svc.RegisterProfile(a); // back again
        Assert.Equal(3, _grabber.Registered.Count);

        _svc.UnregisterAll();
        Assert.Empty(_grabber.Registered);
        _svc.Dispose();
    }

    [Fact]
    public void RegisterProfile_Conflict_IsRecordedLikeWindows()
    {
        _grabber.TakenByOtherApps.Add((Key(0x41).GetModifierFlags(), 0x41));
        _svc.RegisterProfile(Profile("A", Key(0x41)));
        Assert.Equal(ErrorCode.HotkeyConflict, Assert.Single(_errors.Errors).Code);
    }

    [Fact]
    public void Ids_AreUniqueAndNeverZero_EvenAfterManyRegistrations()
    {
        var p = Profile("A", Key(0x41));
        for (int i = 0; i < 70_000; i++) // wraps the 16-bit id space
        {
            _svc.UnregisterProfile(p.Id);
            _svc.RegisterProfile(p);
        }
        _svc.RegisterSettingsHotkey(Key(0x53));
        Assert.Equal(2, _grabber.Registered.Count);
        Assert.DoesNotContain((ushort)0, _grabber.Registered.Keys);
        Assert.Equal(2, _grabber.Registered.Keys.Distinct().Count());
    }
}
