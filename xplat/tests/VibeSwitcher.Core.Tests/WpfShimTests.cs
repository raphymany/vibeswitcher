using VibeSwitcher.Compat;

namespace VibeSwitcher.Tests;

// The shims stand in for WPF inside linked Windows code, so they must behave like WPF in the ways
// that code relies on. Every test restores the global state it touches (tests run serially).
public class WpfShimTests : IDisposable
{
    private sealed class FakeUi : IUiDispatcher
    {
        public bool OnUiThread { get; set; }
        public List<Action> Posted { get; } = new();
        public List<(TimeSpan Interval, Action Tick, Handle Handle)> Timers { get; } = new();

        public bool CheckAccess() => OnUiThread;
        public void Post(Action action) => Posted.Add(action);

        public IDisposable StartTimer(TimeSpan interval, Action tick)
        {
            var handle = new Handle();
            Timers.Add((interval, tick, handle));
            return handle;
        }

        // Runs what's queued now; work queued *by* those callbacks waits for the next call.
        public void RunPosted()
        {
            var batch = Posted.ToList();
            Posted.Clear();
            foreach (var a in batch) a();
        }

        public sealed class Handle : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }
    }

    private sealed class FakeWav : IWavPlayer
    {
        public List<byte[]> Played { get; } = new();
        public void PlayWav(byte[] wav) => Played.Add(wav);
    }

    // Start from a clean slate: other test classes may have started timers with no dispatcher.
    public WpfShimTests() => UiThread.Unregister();

    public void Dispose()
    {
        UiThread.Unregister();
        WavPlayback.Player = null;
    }

    [Fact]
    public void Application_IsNull_UntilRegistered_SoLinkedCodeRunsInline()
    {
        Assert.Null(System.Windows.Application.Current);
        UiThread.Register(new FakeUi());
        Assert.NotNull(System.Windows.Application.Current);
        UiThread.Unregister();
        Assert.Null(System.Windows.Application.Current);
    }

    [Fact]
    public async Task InvokeAsync_RunsOnUiThread_AndCapturesExceptions()
    {
        var ui = new FakeUi();
        UiThread.Register(ui);
        var dispatcher = System.Windows.Application.Current!.Dispatcher;

        bool ran = false;
        var ok = dispatcher.InvokeAsync(() => ran = true);
        var failed = dispatcher.InvokeAsync(() => throw new InvalidOperationException("boom"));
        var value = dispatcher.InvokeAsync(() => 42);
        Assert.False(ran);                // queued, not inline

        ui.RunPosted();
        await ok;
        Assert.True(ran);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        Assert.Equal(42, await value);
    }

    [Fact]
    public void BeginInvoke_PostsTheDelegateWithArguments()
    {
        var ui = new FakeUi();
        UiThread.Register(ui);
        string? got = null;
        System.Windows.Application.Current!.Dispatcher.BeginInvoke(new Action<string>(s => got = s), "hi");
        ui.RunPosted();
        Assert.Equal("hi", got);
    }

    [Fact]
    public void DispatcherTimer_WithoutUiDispatcher_IsEnabledButNeverTicks()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Start();
        Assert.True(timer.IsEnabled);
        timer.Stop();
        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public void DispatcherTimer_TicksThroughUiDispatcher_AndStopDisposesIt()
    {
        var ui = new FakeUi();
        UiThread.Register(ui);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        int ticks = 0;
        timer.Tick += (_, _) => ticks++;

        timer.Start();
        timer.Start();                    // second Start is a no-op, like WPF
        Assert.Single(ui.Timers);
        Assert.Equal(TimeSpan.FromSeconds(5), ui.Timers[0].Interval);

        ui.Timers[0].Tick();
        Assert.Equal(1, ticks);

        timer.Stop();
        Assert.True(ui.Timers[0].Handle.Disposed);
    }

    [Fact]
    public void CommandManager_RaisesInline_WithoutDispatcher_ElseQueuesOneBatchedRefresh()
    {
        int raised = 0;
        EventHandler h = (_, _) => raised++;
        System.Windows.Input.CommandManager.RequerySuggested += h;
        try
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            Assert.Equal(1, raised);

            // Like WPF: queued even when already on the UI thread, and repeated calls collapse.
            var ui = new FakeUi { OnUiThread = true };
            UiThread.Register(ui);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            Assert.Equal(1, raised);
            Assert.Single(ui.Posted);
            ui.RunPosted();
            Assert.Equal(2, raised);

            // After it ran, the next invalidation queues again.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            ui.RunPosted();
            Assert.Equal(3, raised);
        }
        finally
        {
            System.Windows.Input.CommandManager.RequerySuggested -= h;
        }
    }

    [Fact]
    public void BeginInvoke_SurfacesTheOriginalException()
    {
        var ui = new FakeUi();
        UiThread.Register(ui);
        System.Windows.Application.Current!.Dispatcher.BeginInvoke(new Action<int>(_ => throw new InvalidOperationException("x")), 1);
        Assert.Throws<InvalidOperationException>(() => ui.RunPosted());
    }

    [Fact]
    public void DispatcherTimer_StartedBeforeRegister_IsBoundWhenTheDispatcherArrives()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();

        var ui = new FakeUi();
        UiThread.Register(ui);
        Assert.Single(ui.Timers);
        ui.Timers[0].Tick();
        Assert.Equal(1, ticks);
        timer.Stop();
    }

    [Fact]
    public void DispatcherTimer_StoppedBeforeRegister_IsNotBound()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Start();
        timer.Stop();

        var ui = new FakeUi();
        UiThread.Register(ui);
        Assert.Empty(ui.Timers);
    }

    [Fact]
    public void DispatcherTimer_DropsATickQueuedBeforeStop()
    {
        var ui = new FakeUi();
        UiThread.Register(ui);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();
        var staleTick = ui.Timers[0].Tick;

        timer.Stop();
        timer.Start();                    // restarted: a new timer on the dispatcher
        staleTick();                      // the old one's tick arrives late
        Assert.Equal(0, ticks);

        ui.Timers[1].Tick();
        Assert.Equal(1, ticks);
        timer.Stop();
    }

    // ── linked services running through a registered UI dispatcher ─────────────

    [Fact]
    public void SchedulerService_FiresThroughUiTimer_AndRearmsForTheNextOccurrence()
    {
        var ui = new FakeUi { OnUiThread = true };
        UiThread.Register(ui);
        var now = new DateTime(2026, 1, 5, 8, 59, 0);              // Monday 08:59
        var profile = new Models.DeviceProfile { Name = "Work" };
        profile.Schedules.Add(new Models.ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] });
        var config = new FakeConfigService();
        config.Current.Profiles.Add(profile);
        var switched = new List<Models.DeviceProfile>();
        var svc = new Services.SchedulerService(config, (p, _) => switched.Add(p), (_, _) => { }, () => now);

        svc.EvaluateNow();
        Assert.Empty(switched);
        Assert.Single(ui.Timers);
        Assert.Equal(TimeSpan.FromMinutes(1), ui.Timers[0].Interval);

        now = now.AddMinutes(1);                                   // 09:00 — the timer fires
        ui.Timers[0].Tick();
        Assert.Single(switched);
        Assert.True(ui.Timers[0].Handle.Disposed);                 // old timer stopped
        Assert.Equal(2, ui.Timers.Count);                          // re-armed for next Monday
        svc.Dispose();
        Assert.True(ui.Timers[1].Handle.Disposed);
    }

    [Fact]
    public void DeviceTriggerService_MarshalsDeviceEvents_ThenSwitchesOnTheUiThread()
    {
        var ui = new FakeUi { OnUiThread = false };                // events arrive on a background thread
        UiThread.Register(ui);
        var audio = new FakeAudioService();
        var profile = new Models.DeviceProfile { Name = "Headset", Mode = Models.ProfileMode.Playback, PlaybackDeviceId = "hs", TriggerOnConnect = true };
        var config = new FakeConfigService();
        config.Current.Profiles.Add(profile);
        var switched = new List<Models.DeviceProfile>();
        using var svc = new Services.DeviceTriggerService(audio, config, p => switched.Add(p), new FakeAppLogger());

        audio.PlaybackResult = [new Services.AudioDeviceInfo("hs", "Headset", IsPlayback: true)];
        audio.RaiseDevicesChanged();
        Assert.Empty(switched);                                    // handler queued, not run inline

        ui.OnUiThread = true;
        ui.RunPosted();                                            // handler runs, queues the switch
        ui.RunPosted();                                            // switch runs
        Assert.Single(switched);
        Assert.Same(profile, switched[0]);
    }

    [Fact]
    public void RelayCommand_CanExecuteChanged_FollowsCommandManager()
    {
        var cmd = new Helpers.RelayCommand(() => { });
        int changed = 0;
        EventHandler h = (_, _) => changed++;
        cmd.CanExecuteChanged += h;
        cmd.RaiseCanExecuteChanged();
        cmd.CanExecuteChanged -= h;
        cmd.RaiseCanExecuteChanged();
        Assert.Equal(1, changed);
    }

    [Fact]
    public void SoundPlayer_ForwardsWholeWavToPlayer_OrSkipsWhenNone()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        using (var p = new System.Media.SoundPlayer(new MemoryStream(bytes))) p.PlaySync();   // no player: no-op

        var wav = new FakeWav();
        WavPlayback.Player = wav;
        using (var p = new System.Media.SoundPlayer(new MemoryStream(bytes))) p.PlaySync();
        Assert.Single(wav.Played);
        Assert.Equal(bytes, wav.Played[0]);
    }
}
