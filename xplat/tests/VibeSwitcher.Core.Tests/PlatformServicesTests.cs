using VibeSwitcher.Compat;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;

namespace VibeSwitcher.Core.Tests;

public class AppPathsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        k => vars.FirstOrDefault(v => v.Key == k).Value;

    [Fact]
    public void Linux_DefaultAndXdgConfigHome_LockAndSocketBeside()
    {
        var p = AppPaths.Resolve(false, Env(), "/home/ann");
        Assert.Equal(Path.Combine("/home/ann", ".config", "VibeSwitcher"), p.ConfigDir);
        Assert.Equal(p.ConfigDir, p.LogDir);
        Assert.Equal(Path.Combine(p.ConfigDir, "vibeswitcher.lock"), p.InstanceLock);
        Assert.Equal(Path.Combine(p.ConfigDir, "vibeswitcher.sock"), p.InstanceSocket);

        p = AppPaths.Resolve(false, Env(("XDG_CONFIG_HOME", "/cfg"), ("XDG_RUNTIME_DIR", "/run/user/1000")), "/home/ann");
        Assert.Equal(Path.Combine("/cfg", "VibeSwitcher"), p.ConfigDir); // never the (cleanable, per-session) runtime dir
    }

    [Fact]
    public void Linux_RelativeXdgValuesAreIgnored() =>
        Assert.Equal(Path.Combine("/home/ann", ".config", "VibeSwitcher"),
            AppPaths.Resolve(false, Env(("XDG_CONFIG_HOME", "relative/cfg")), "/home/ann").ConfigDir);

    [Fact]
    public void MacOS_ApplicationSupport() =>
        Assert.Equal(Path.Combine("/Users/ann", "Library", "Application Support", "VibeSwitcher"),
            AppPaths.Resolve(true, Env(("TMPDIR", "/var/folders/ab/cd/T/")), "/Users/ann").ConfigDir);

    [Fact]
    public void SocketLimit_CountsBytesNotCharacters()
    {
        Assert.True(AppPaths.Resolve(false, Env(), "/home/ann").SocketPathFits);
        // 40 two-byte characters: under 100 characters, over 100 bytes.
        Assert.False(AppPaths.Resolve(false, Env(), "/home/" + new string('é', 40)).SocketPathFits);
        Assert.False(AppPaths.Resolve(false, Env(), "/home/" + new string('x', 90)).SocketPathFits);
    }
}

public class CommandLineTests
{
    [Fact]
    public void NoArguments_OpensOrStarts() => Assert.Equal(AppCommand.Open, CommandLine.Parse([]).Command);

    [Theory]
    [InlineData("--switch", "Gaming", AppCommandKind.Switch, "Gaming")]
    [InlineData("--switch", "My Headset Profile", AppCommandKind.Switch, "My Headset Profile")]
    [InlineData("--switch", "-NSLooksLikeAnOption", AppCommandKind.Switch, "-NSLooksLikeAnOption")]
    [InlineData("--mute", "mic", AppCommandKind.Mute, "Mic")]
    [InlineData("--mute", "Speakers", AppCommandKind.Mute, "Speakers")]
    [InlineData("--mute", "both", AppCommandKind.Mute, "Both")]
    public void Commands(string option, string value, AppCommandKind kind, string argument)
    {
        var cl = CommandLine.Parse([option, value]);
        Assert.Null(cl.Error);
        Assert.Equal(new AppCommand(kind, argument), cl.Command);
    }

    [Fact]
    public void EqualsForm_IsAccepted()
    {
        Assert.Equal(new AppCommand(AppCommandKind.Switch, "Gaming=Night"), CommandLine.Parse(["--switch=Gaming=Night"]).Command);
        Assert.Equal(new AppCommand(AppCommandKind.Mute, "Mic"), CommandLine.Parse(["--mute=mic"]).Command);
    }

    [Fact]
    public void Open_Help_Version()
    {
        Assert.Equal(AppCommand.Open, CommandLine.Parse(["--open"]).Command);
        Assert.True(CommandLine.Parse(["--help"]).ShowHelp);
        Assert.True(CommandLine.Parse(["-h"]).ShowHelp);
        Assert.True(CommandLine.Parse(["--version"]).ShowVersion);
        Assert.Contains("--switch <profile>", CommandLine.Usage);
    }

    [Fact]
    public void MacOSLaunchArguments_AreIgnored()
    {
        Assert.Equal(AppCommand.Open, CommandLine.Parse(["-psn_0_1234567"]).Command);
        Assert.Equal(AppCommand.Open, CommandLine.Parse(["-NSDocumentRevisionsDebugMode", "YES"]).Command);
        Assert.Equal(new AppCommand(AppCommandKind.Switch, "Gaming"),
            CommandLine.Parse(["-NSDocumentRevisionsDebugMode", "YES", "--switch", "Gaming"]).Command);
    }

    [Theory]
    [InlineData("--switch")]
    [InlineData("--switch", " ")]
    [InlineData("--mute")]
    [InlineData("--mute", "loud")]
    [InlineData("--frobnicate")]
    [InlineData("--open", "extra")]
    [InlineData("--switch", "A", "B")]
    public void Errors(params string[] args)
    {
        var cl = CommandLine.Parse(args);
        Assert.NotNull(cl.Error);
        Assert.Null(cl.Command);
    }

    [Fact]
    public void Command_WireRoundTrip_AndBadRequestsRejected()
    {
        var cmd = new AppCommand(AppCommandKind.Switch, "Gaming \"Night\" ✨");
        Assert.Equal(cmd, AppCommand.Deserialize(cmd.Serialize()));
        Assert.Null(AppCommand.Deserialize("not json"));
        Assert.Null(AppCommand.Deserialize("{\"Kind\":\"Explode\"}"));
        Assert.Null(AppCommand.Deserialize("{\"Kind\":\"99\"}"));
        // Same rules as the command line, whoever sends them.
        Assert.Null(AppCommand.Deserialize(new AppCommand(AppCommandKind.Switch, " ").Serialize()));
        Assert.Null(AppCommand.Deserialize(new AppCommand(AppCommandKind.Mute, "loud").Serialize()));
    }
}

public sealed class SingleInstanceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vs-si").FullName;
    private readonly TimeSpan _savedWindow = SingleInstance.ConnectWindow;
    private AppPaths Paths => new(_dir);

    public SingleInstanceTests() => SingleInstance.ConnectWindow = TimeSpan.FromMilliseconds(300);

    public void Dispose()
    {
        SingleInstance.ConnectWindow = _savedWindow;
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    [Fact]
    public async Task SecondLaunch_DeliversItsCommand_ToTheFirst()
    {
        var received = new List<AppCommand>();
        using var first = new SingleInstance(Paths);
        Assert.True(first.TryAcquire(cmd => { lock (received) received.Add(cmd); return Task.FromResult($"ok: {cmd.Kind}"); }));

        using var second = new SingleInstance(Paths);
        Assert.False(second.TryAcquire(_ => Task.FromResult("")));

        Assert.Equal("ok: Switch", await SingleInstance.SendAsync(Paths, new AppCommand(AppCommandKind.Switch, "Gaming")));
        Assert.Equal("ok: Open", await SingleInstance.SendAsync(Paths, AppCommand.Open));
        lock (received) Assert.Equal([new AppCommand(AppCommandKind.Switch, "Gaming"), AppCommand.Open], received);
    }

    [Fact]
    public async Task HandlerError_IsReportedToTheSender()
    {
        using var first = new SingleInstance(Paths);
        first.TryAcquire(_ => throw new InvalidOperationException("No profile named 'X'."));
        Assert.Equal("error: No profile named 'X'.", await SingleInstance.SendAsync(Paths, new AppCommand(AppCommandKind.Switch, "X")));
    }

    [Fact]
    public async Task Unreachable_SendReturnsNull_AfterRetrying()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await SingleInstance.SendAsync(Paths, AppCommand.Open));
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(250)); // kept trying (it may be starting up)
        File.WriteAllText(Paths.InstanceSocket, ""); // a stale file left by a crash: nobody listening
        Assert.Null(await SingleInstance.SendAsync(Paths, AppCommand.Open));
    }

    [Fact]
    public async Task StartingUp_TheCommandWaitsForTheChannel()
    {
        // The lock is held but the listener isn't up yet (launched twice at the same moment).
        var send = SingleInstance.SendAsync(Paths, AppCommand.Open);
        await Task.Delay(100);
        using var first = new SingleInstance(Paths);
        Assert.True(first.TryAcquire(_ => Task.FromResult("got it")));
        Assert.Equal("got it", await send);
    }

    [Fact]
    public async Task AfterTheFirstExits_ANewInstanceCanStart_EvenWithAStaleSocketFile()
    {
        var first = new SingleInstance(Paths);
        Assert.True(first.TryAcquire(_ => Task.FromResult("one")));
        first.Dispose();
        first.Dispose(); // twice is fine
        File.WriteAllText(Paths.InstanceSocket, ""); // as if it had crashed

        using var next = new SingleInstance(Paths);
        Assert.True(next.TryAcquire(_ => Task.FromResult("two")));
        Assert.Equal("two", await SingleInstance.SendAsync(Paths, AppCommand.Open));
    }

    [Fact]
    public async Task ALiveSocket_IsNeverTakenOver()
    {
        // Someone is listening although the lock file is free (e.g. the lock file was deleted).
        using var other = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        other.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(Paths.InstanceSocket));
        other.Listen(1);

        using var candidate = new SingleInstance(Paths);
        Assert.False(candidate.TryAcquire(_ => Task.FromResult("")));
        Assert.True(File.Exists(Paths.InstanceSocket));

        using var later = new SingleInstance(Paths); // and it didn't keep the lock either
        other.Dispose();
        Assert.True(later.TryAcquire(_ => Task.FromResult("")));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SlowHandler_DoesNotBlockOtherSenders_NorAnIdleConnection()
    {
        var release = new TaskCompletionSource();
        using var first = new SingleInstance(Paths);
        first.TryAcquire(async cmd =>
        {
            if (cmd.Argument == "slow") await release.Task;
            return cmd.Argument ?? "open";
        });
        using var idle = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        idle.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(Paths.InstanceSocket)); // connects, says nothing

        var slow = SingleInstance.SendAsync(Paths, new AppCommand(AppCommandKind.Switch, "slow"));
        Assert.Equal("fast", await SingleInstance.SendAsync(Paths, new AppCommand(AppCommandKind.Switch, "fast")));
        release.SetResult();
        Assert.Equal("slow", await slow);
    }

    [Fact]
    public async Task OversizedRequest_IsRejected()
    {
        using var first = new SingleInstance(Paths);
        bool called = false;
        first.TryAcquire(_ => { called = true; return Task.FromResult("ok"); });
        using var s = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        await s.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(Paths.InstanceSocket));
        await s.SendAsync(new byte[10_000].Select(_ => (byte)'x').ToArray(), System.Net.Sockets.SocketFlags.None);
        var buf = new byte[256];
        int n = await s.ReceiveAsync(buf, System.Net.Sockets.SocketFlags.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.StartsWith("error:", System.Text.Encoding.UTF8.GetString(buf, 0, n));
        Assert.False(called);
    }

    [Fact]
    public void PathTooLongForASocket_StillSingleInstance_WithoutTheCommandLine()
    {
        var deep = Path.Combine(_dir, new string('d', 60), new string('e', 60));
        var paths = new AppPaths(deep);
        var warnings = new List<string>();
        using var first = new SingleInstance(paths, warnings.Add);
        Assert.True(first.TryAcquire(_ => Task.FromResult("")));
        Assert.Single(warnings);
        Assert.False(File.Exists(paths.InstanceSocket));
        using var second = new SingleInstance(paths);
        Assert.False(second.TryAcquire(_ => Task.FromResult("")));
    }

    [Fact]
    public void LockContention_IsTheOnlyIoErrorMeaningRunning()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(SingleInstance.IsLockContention(new IOException("x", unchecked((int)0x80070020))));
            Assert.False(SingleInstance.IsLockContention(new IOException("disk full", unchecked((int)0x80070070))));
        }
        else
        {
            Assert.True(SingleInstance.IsLockContention(new IOException("x", OperatingSystem.IsMacOS() ? 35 : 11)));
            Assert.False(SingleInstance.IsLockContention(new IOException("read-only", 30)));
        }
    }

    [Fact]
    public void SocketAndFolder_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return;
        using var first = new SingleInstance(Paths);
        first.TryAcquire(_ => Task.FromResult(""));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Paths.InstanceSocket));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_dir));
    }
}

public class ResumeDetectorTests
{
    private DateTime _wall = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private TimeSpan _mono = TimeSpan.FromHours(1);

    private ResumeDetector Detector() =>
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), () => _wall, () => _mono, startTimer: false);

    private void Advance(TimeSpan wall, TimeSpan mono) { _wall += wall; _mono += mono; }

    [Fact]
    public void NormalTicks_NoResume()
    {
        var d = Detector();
        int resumed = 0;
        d.Resumed += _ => resumed++;
        for (int i = 0; i < 10; i++) { Advance(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)); d.Check(); }
        Advance(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(9)); d.Check(); // a slow tick under load
        Assert.Equal(0, resumed);
    }

    [Fact]
    public void Sleep_WallClockJumps_MonotonicDoesNot_Resumed()
    {
        var d = Detector();
        TimeSpan? slept = null;
        d.Resumed += s => slept = s;
        Advance(TimeSpan.FromMinutes(45) + TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        d.Check();
        Assert.Equal(TimeSpan.FromMinutes(45), slept);
    }

    [Fact]
    public void ClockSetBackwards_OrSmallNtpNudge_NotAResume()
    {
        var d = Detector();
        int resumed = 0;
        d.Resumed += _ => resumed++;
        Advance(TimeSpan.FromHours(-1), TimeSpan.FromSeconds(5)); d.Check();
        Advance(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(5)); d.Check();
        Assert.Equal(0, resumed);
    }

    [Fact]
    public void SubscriberThrowing_DoesNotStopTheOthers()
    {
        var d = Detector();
        int after = 0;
        d.Resumed += _ => throw new InvalidOperationException();
        d.Resumed += _ => after++;
        Advance(TimeSpan.FromHours(1), TimeSpan.Zero); d.Check();
        Advance(TimeSpan.FromHours(1), TimeSpan.Zero); d.Check();
        Assert.Equal(2, after);
    }
}

public class InstallInfoTests
{
    [Fact]
    public void MacAppBundle()
    {
        var i = InstallInfo.Detect("/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher", null, true, "2.1.0");
        Assert.Equal(InstallKind.MacApp, i.Kind);
        Assert.Equal("/Applications/VibeSwitcher.app", i.Location);
        Assert.Equal("2.1.0", i.Version);
        Assert.False(i.Translocated);
    }

    [Fact]
    public void MacApp_RunFromDownloads_IsTranslocated()
    {
        var i = InstallInfo.Detect("/private/var/folders/ab/cd/T/AppTranslocation/1F2E/d/VibeSwitcher.app/Contents/MacOS/VibeSwitcher", null, true, "2.1.0");
        Assert.Equal(InstallKind.MacApp, i.Kind);
        Assert.True(i.Translocated);
    }

    [Fact]
    public void AppImage_UsesTheImageFileNotTheMount()
    {
        var i = InstallInfo.Detect("/tmp/.mount_VibeSwXYZ/usr/bin/VibeSwitcher", "/home/ann/Apps/VibeSwitcher.AppImage", false, "2.1.0");
        Assert.Equal(InstallKind.AppImage, i.Kind);
        Assert.Equal("/home/ann/Apps/VibeSwitcher.AppImage", i.Location);
    }

    [Fact]
    public void AppImage_OnlyTrustedWhenWeRunFromItsMount()
    {
        Func<string, string?> env = k => k switch
        {
            "APPIMAGE" => "/home/ann/Apps/VibeSwitcher.AppImage",
            "APPDIR" => "/tmp/.mount_VibeSwXYZ",
            _ => null,
        };
        Assert.Equal("/home/ann/Apps/VibeSwitcher.AppImage", InstallInfo.TrustedAppImage(env, "/tmp/.mount_VibeSwXYZ/usr/bin/VibeSwitcher"));
        // Inherited from a terminal or editor that is itself an AppImage (VS Code, Kitty…):
        Assert.Null(InstallInfo.TrustedAppImage(env, "/usr/lib/vibeswitcher/VibeSwitcher"));
        Assert.Null(InstallInfo.TrustedAppImage(env, "/tmp/.mount_VibeSwXYZother/usr/bin/VibeSwitcher"));
        Assert.Null(InstallInfo.TrustedAppImage(k => k == "APPIMAGE" ? "/x.AppImage" : null, "/tmp/.mount_a/b"));
        Assert.Null(InstallInfo.TrustedAppImage(_ => null, "/usr/bin/x"));
    }

    [Fact]
    public void DebPackage_AndDevelopment()
    {
        Assert.Equal(InstallKind.Package, InstallInfo.Detect("/usr/lib/vibeswitcher/VibeSwitcher", null, false, "1").Kind);
        Assert.Equal(InstallKind.Development, InstallInfo.Detect("/home/ann/src/bin/VibeSwitcher", null, false, "1").Kind);
        Assert.Equal(InstallKind.Development, InstallInfo.Detect("/Users/ann/src/bin/VibeSwitcher", null, true, "1").Kind);
    }

    [Fact]
    public void Version_IsMajorMinorBuild() => Assert.Matches(@"^\d+\.\d+\.\d+$", InstallInfo.AppVersion());
}

// The forked MuteService: same toggle rules and sounds as Windows, on the platform audio service.
public sealed class MuteServiceTests : IDisposable
{
    private sealed class FakePlatformAudio : IPlatformAudioService
    {
        public bool? MicMuted = false, SpeakersMuted = false;
        public bool FailSet;
        public List<string> Calls { get; } = [];
        public bool? GetDefaultDeviceMute(bool playback) => playback ? SpeakersMuted : MicMuted;
        public bool SetDefaultDeviceMute(bool playback, bool mute)
        {
            Calls.Add($"{(playback ? "speakers" : "mic")}={mute}");
            if (FailSet) return false;
            if (playback) SpeakersMuted = mute; else MicMuted = mute;
            return true;
        }
        public event Action? DevicesChanged { add { } remove { } }
        public event Action<string>? DevicePropertyChanged { add { } remove { } }
        public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => [];
        public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => [];
        public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) => throw new NotSupportedException();
        public Task TestSoundAsync(string deviceId) => Task.CompletedTask;
        public string? GetAudioEndpointPath(string audioDeviceId) => null;
        public void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel) { }
        public void Dispose() { }
    }

    private sealed class Player : IWavPlayer
    {
        public readonly List<byte[]> Played = [];
        public readonly SemaphoreSlim Signal = new(0);
        public void PlayWav(byte[] wav) { lock (Played) Played.Add(wav); Signal.Release(); }
    }

    private readonly FakePlatformAudio _audio = new();
    private readonly Player _player = new();
    private readonly MuteService _mute;

    public MuteServiceTests()
    {
        WavPlayback.Player = _player;
        _mute = new MuteService(new VibeSwitcher.Tests.FakeAppLogger(), _audio);
    }

    // Sounds play on background tasks; let this test's finish before the next test's player is installed.
    public void Dispose()
    {
        while (_player.Signal.Wait(400)) { }
        WavPlayback.Player = null;
    }

    [Fact]
    public async Task Mic_TogglesFromTheDevicesRealState_AndPlaysTheMuteSound()
    {
        (MuteScope, bool)? toggled = null;
        _mute.MuteToggled += (s, m) => toggled = (s, m);

        _mute.Toggle(MuteScope.Mic);
        Assert.True(_audio.MicMuted);
        Assert.Equal((MuteScope.Mic, true), toggled);
        Assert.True(await _player.Signal.WaitAsync(5000));
        Assert.Equal(VibeSwitcher.Audio.WavSampleFormat.S16, VibeSwitcher.Audio.WavData.Parse(_player.Played[0]).Format);

        _audio.MicMuted = false; // unmuted outside the app (e.g. the OS sound settings)
        _mute.Toggle(MuteScope.Mic);
        Assert.True(_audio.MicMuted); // based on the real state, not a cached flag
    }

    [Fact]
    public async Task MutingSpeakers_PlaysNoSound_UnmutingDoes()
    {
        _mute.Toggle(MuteScope.Speakers);
        Assert.True(_audio.SpeakersMuted);
        Assert.False(await _player.Signal.WaitAsync(300)); // nobody would hear it
        _mute.Toggle(MuteScope.Speakers);
        Assert.False(_audio.SpeakersMuted);
        Assert.True(await _player.Signal.WaitAsync(5000));
    }

    [Fact]
    public void Both_MutesUnlessBothAlreadyMuted()
    {
        _audio.MicMuted = true; // only one muted → "both" mutes both
        _mute.Toggle(MuteScope.Both);
        Assert.True(_audio.MicMuted == true && _audio.SpeakersMuted == true);
        _mute.Toggle(MuteScope.Both);
        Assert.True(_audio.MicMuted == false && _audio.SpeakersMuted == false);
    }

    [Fact]
    public void SetFails_ReportsTheUnchangedState()
    {
        _audio.FailSet = true;
        (MuteScope, bool)? toggled = null;
        _mute.MuteToggled += (s, m) => toggled = (s, m);
        _mute.Toggle(MuteScope.Mic);
        Assert.Equal((MuteScope.Mic, false), toggled);
    }

    [Fact]
    public void UnknownState_FallsBackToTheLastKnownOne()
    {
        _audio.MicMuted = null; // no default microphone readable
        _mute.Toggle(MuteScope.Mic);
        Assert.Equal(["mic=True"], _audio.Calls);
    }
}
