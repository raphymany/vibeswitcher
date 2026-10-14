using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Imaging;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hid;
using VibeSwitcher.Services;
using static VibeSwitcher.App.Tests.Ui;

namespace VibeSwitcher.App.Tests;

// The last dialogs: the app-trigger wizard, the start-up splash, and (Linux) the headset permission.
[Collection("Shared UI settings")] // (changes Wording.Mac)
public class AppsDialogsTests
{
    private static void Key(Window w, Key key) =>
        w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    // ── App launch triggers ────────────────────────────────────────────────────────────────────────

    // An app catalog with fixed lists. Gate holds Installed() back until released; Fail makes both
    // lists fail.
    internal sealed class FakeCatalog : IAppCatalog
    {
        public List<AppEntry> InstalledApps { get; } =
        [
            new("/usr/lib/firefox/firefox", "Firefox", "firefox", true),
            new("/usr/bin/steam", "Steam", "steam", false),
            new("flatpak:com.spotify.Client", "Spotify", "com.spotify.Client", false),
            new("/opt/discord/Discord", "Discord", "discord", false),
        ];
        public List<AppEntry> RunningApps { get; } =
        [
            new("/usr/lib/firefox/firefox", "Firefox", "firefox", true),
            new("Game.exe", "Game", null, true), // a Windows game under Wine
        ];
        public Func<string, string?> ForFile { get; set; } = path => path;
        public ManualResetEventSlim? Gate { get; set; }
        public bool Fail { get; set; }
        public bool FailRunning { get; set; }

        public IReadOnlyList<AppEntry> Installed(CancellationToken ct = default)
        {
            Gate?.Wait(TimeSpan.FromSeconds(5));
            if (Fail) throw new IOException("no access");
            return InstalledApps;
        }

        public IReadOnlyList<AppEntry> Running() => Fail || FailRunning ? throw new IOException("no access") : RunningApps;
        public string? TriggerForFile(string path) => ForFile(path);
    }

    internal static readonly Dictionary<string, string> UsedByOthers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flatpak:com.spotify.Client"] = "Music",
    };

    private static async Task<AppTriggerDialog> Open(List<string> linked, FakeCatalog? catalog = null, Func<AppEntry, IImage?>? icons = null)
    {
        var dialog = new AppTriggerDialog(linked, UsedByOthers, catalog ?? new FakeCatalog(), icons);
        dialog.Show();
        await Until(() => dialog.AppsLoaded);
        return dialog;
    }

    // Each row of the app list, as its texts (name, detail, "● Running", badge or button).
    private static List<List<string?>> Rows(AppTriggerDialog dialog) =>
        Named<StackPanel>(dialog, "RunningPanel").Children
            .Select(row => row is TextBlock message ? [message.Text] : Texts(row).ToList())
            .ToList();

    private static List<string?> RowNames(AppTriggerDialog dialog) => Rows(dialog).Select(r => r[0]).ToList();

    private static Button AddButton(AppTriggerDialog dialog, string app) =>
        Named<StackPanel>(dialog, "RunningPanel").Children.First(r => Texts(r).First() == app)
            .GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "+ Add");

    [AvaloniaFact]
    public async Task TheList_ShowsRunningAndInstalledApps_WithTheirState()
    {
        var dialog = await Open(["/usr/bin/steam"]);
        Assert.False(Named<TextBlock>(dialog, "LoadingLabel").IsVisible);
        // Running apps and the installed ones that aren't, by name; each app once.
        Assert.Equal(new[] { "Discord", "Firefox", "Game", "Spotify", "Steam" }, RowNames(dialog));
        var rows = Rows(dialog);
        Assert.Equal(new[] { "Discord", "Discord", "+ Add" }, rows[0]);
        Assert.Equal(new[] { "Firefox", "firefox", "● Running", "+ Add" }, rows[1]);
        Assert.Equal(new[] { "Game", "Game.exe", "● Running", "+ Add" }, rows[2]);
        Assert.Equal(new[] { "Spotify", "com.spotify.Client (Flatpak)", "Used by \"Music\"" }, rows[3]);
        Assert.Equal(new[] { "Steam", "steam", "Added" }, rows[4]);
        // The linked app, with what the lists know about it.
        Assert.False(Named<TextBlock>(dialog, "EmptyLabel").IsVisible);
        Assert.Equal(new[] { "Steam", "steam", "✕" }, Texts(Named<StackPanel>(dialog, "LinkedPanel")));
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task FiltersAndSearch_NarrowTheList()
    {
        var dialog = await Open(["/usr/bin/steam"]);
        Click(Named<Button>(dialog, "ChipRunning"));
        Assert.Equal(AppTriggerDialog.AppFilter.Running, dialog.ActiveFilter);
        Assert.Contains("on", Named<Button>(dialog, "ChipRunning").Classes);
        Assert.DoesNotContain("on", Named<Button>(dialog, "ChipAll").Classes);
        Assert.Equal(new[] { "Firefox", "Game" }, RowNames(dialog));
        Click(Named<Button>(dialog, "ChipInstalled"));
        Assert.Equal(new[] { "Discord", "Spotify", "Steam" }, RowNames(dialog));
        Click(Named<Button>(dialog, "ChipInUse"));
        Assert.Equal(new[] { "Spotify", "Steam" }, RowNames(dialog));
        Click(Named<Button>(dialog, "ChipAll"));

        // By name or by the second line; the placeholder hides while there's text.
        var search = Named<TextBox>(dialog, "SearchBox");
        search.Text = "FIRE";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Firefox" }, RowNames(dialog));
        Assert.False(Named<TextBlock>(dialog, "SearchPlaceholder").IsVisible);
        search.Text = "flatpak";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Spotify" }, RowNames(dialog));
        search.Text = "zzz";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "No matches found." }, RowNames(dialog));
        search.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<TextBlock>(dialog, "SearchPlaceholder").IsVisible);
        dialog.Close();

        // The empty messages per filter.
        var empty = new FakeCatalog();
        empty.InstalledApps.Clear();
        empty.RunningApps.Clear();
        dialog = await Open([], empty);
        Assert.Equal(new[] { "No apps detected." }, RowNames(dialog));
        Click(Named<Button>(dialog, "ChipRunning"));
        Assert.Equal(new[] { "No running apps detected." }, RowNames(dialog));
        Click(Named<Button>(dialog, "ChipInstalled"));
        Assert.Equal(new[] { "No installed apps found." }, RowNames(dialog));
        Click(Named<Button>(dialog, "ChipInUse"));
        Assert.Equal(new[] { "No apps are in use yet." }, RowNames(dialog));
        Assert.True(Named<TextBlock>(dialog, "EmptyLabel").IsVisible);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task AddingAndRemovingApps_AndDone_ReturnTheLinkedApps()
    {
        var dialog = await Open(["/usr/bin/steam"]);
        Click(AddButton(dialog, "Discord"));
        Assert.Equal(new[] { "/usr/bin/steam", "/opt/discord/Discord" }, dialog.Linked);
        Assert.Equal("Added", Rows(dialog)[0][^1]);
        Assert.Equal(new[] { "Steam", "steam", "✕", "Discord", "Discord", "✕" }, Texts(Named<StackPanel>(dialog, "LinkedPanel")));

        // ✕ on the first linked app.
        var remove = Named<StackPanel>(dialog, "LinkedPanel").GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "✕");
        Click(remove);
        Assert.Equal(new[] { "/opt/discord/Discord" }, dialog.Linked);
        Assert.Equal("+ Add", Rows(dialog)[4][^1]); // Steam can be added again

        Click(dialog, "Done");
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.Result);
        Assert.Equal(new[] { "/opt/discord/Discord" }, dialog.ResultTriggers);
    }

    [AvaloniaFact]
    public async Task CancelAndEscape_ReturnNothing_ThroughTheDialogService()
    {
        var owner = new Window();
        owner.Show();
        var catalog = new FakeCatalog();
        IDialogService service = new DialogService(() => owner) { Apps = catalog };

        // Add Discord once the lists are in, then Done.
        void AddThenDone(DateTime giveUp) => Dispatcher.UIThread.Post(() =>
        {
            if (owner.OwnedWindows.FirstOrDefault() is not AppTriggerDialog { AppsLoaded: true } d)
            {
                if (DateTime.UtcNow < giveUp) AddThenDone(giveUp);
                return;
            }
            Click(AddButton(d, "Discord"));
            Click(d, "Done");
        }, DispatcherPriority.Background);
        AddThenDone(DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(new[] { "/usr/bin/steam", "/opt/discord/Discord" },
            service.ShowAppTriggerWizard(["/usr/bin/steam"], UsedByOthers));

        ClickLater(owner, "Cancel");
        Assert.Null(service.ShowAppTriggerWizard(["/usr/bin/steam"], UsedByOthers));

        Dispatcher.UIThread.Post(() => Key(owner.OwnedWindows.Single(), Avalonia.Input.Key.Escape), DispatcherPriority.Background);
        Assert.Null(service.ShowAppTriggerWizard([], UsedByOthers));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task Browsing_AddsTheFilesApp_OrSaysWhyNot()
    {
        var catalog = new FakeCatalog
        {
            ForFile = path => path.EndsWith(".txt") ? null : path.EndsWith("steam-launcher") ? "/usr/bin/steam" : path,
        };
        var dialog = await Open(["/usr/bin/steam"], catalog);
        var alerts = new List<(string Title, string Message)>();
        dialog.ShowAlert = (title, message) => { alerts.Add((title, message)); return Task.CompletedTask; };
        string? picked = "/home/me/Apps/Tool.AppImage";
        dialog.PickFile = () => Task.FromResult<string?>(picked);
        var browse = Named<Button>(dialog, "BrowseButton");

        Click(browse);
        await Until(() => dialog.Linked.Count == 2);
        Assert.Equal("/home/me/Apps/Tool.AppImage", dialog.Linked[1]);
        Assert.Contains("Tool.AppImage", Texts(Named<StackPanel>(dialog, "LinkedPanel")));
        Assert.True(browse.IsEnabled);

        picked = "/home/me/notes.txt";
        Click(browse);
        await Until(() => alerts.Count == 1);
        Assert.Equal("Not an App", alerts[0].Title);
        Assert.Contains("notes.txt", alerts[0].Message);

        picked = "/home/me/steam-launcher"; // → Steam, already here
        Click(browse);
        await Until(() => alerts.Count == 2);
        Assert.Equal(("Already Added", "steam is already linked to this profile."), alerts[1]);

        picked = "flatpak:com.spotify.Client";
        Click(browse);
        await Until(() => alerts.Count == 3);
        Assert.Equal("Already Linked", alerts[2].Title);
        Assert.Contains("\"Music\"", alerts[2].Message);

        picked = null; // cancelled
        Click(browse);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, dialog.Linked.Count);
        Assert.Equal(3, alerts.Count);
        Assert.True(browse.IsEnabled);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task IconsShow_WhereTheCatalogHasThem()
    {
        using var icon = new WriteableBitmap(new PixelSize(36, 36), new Vector(96, 96));
        var dialog = await Open(["/usr/lib/firefox/firefox"], icons: app => app.Icon == "firefox" ? icon : null);
        // Firefox in the list and in the linked apps; nobody else has one.
        Assert.Single(Named<StackPanel>(dialog, "RunningPanel").GetVisualDescendants().OfType<Image>());
        Assert.Single(Named<StackPanel>(dialog, "LinkedPanel").GetVisualDescendants().OfType<Image>());
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task ListsThatFail_OrArriveAfterClosing_AreHandled()
    {
        var failing = new FakeCatalog { Fail = true };
        var dialog = await Open(["C:\\Games\\Game.exe"], failing);
        Assert.Equal(new[] { "No apps detected." }, RowNames(dialog));
        // A trigger from a config imported from Windows, shown by its program name.
        Assert.Equal(new[] { "Game", "Game.exe", "✕" }, Texts(Named<StackPanel>(dialog, "LinkedPanel")));
        dialog.Close();

        using var gate = new ManualResetEventSlim();
        var slow = new FakeCatalog { Gate = gate };
        dialog = new AppTriggerDialog([], UsedByOthers, slow);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<TextBlock>(dialog, "LoadingLabel").IsVisible);
        dialog.Close();
        gate.Set();
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.AppsLoaded); // (nothing touched the closed dialog)

        // A Windows-imported trigger is the same app as the Windows game running under Wine.
        dialog = await Open(["C:\\Games\\Game.exe"]);
        Assert.Equal("Added", Rows(dialog)[2][^1]);
        dialog.Close();

        // Only the running list fails: the installed apps still show.
        dialog = await Open([], new FakeCatalog { FailRunning = true });
        Assert.Equal(new[] { "Discord", "Firefox", "Spotify", "Steam" }, RowNames(dialog));
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task AnAppAnotherProfileHas_IsFoundByTheSameTestAsAdded()
    {
        // Another profile has the Windows game from an imported config: the one running under Wine is it.
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["C:\\Games\\Game.exe"] = "Games" };
        var dialog = new AppTriggerDialog([], used, new FakeCatalog());
        dialog.Show();
        await Until(() => dialog.AppsLoaded);
        Assert.Equal("Used by \"Games\"", Rows(dialog)[2][^1]);
        var alerts = new List<string>();
        dialog.ShowAlert = (title, _) => { alerts.Add(title); return Task.CompletedTask; };
        dialog.PickFile = () => Task.FromResult<string?>("Game.exe");
        Click(Named<Button>(dialog, "BrowseButton"));
        await Until(() => alerts.Count == 1);
        Assert.Equal("Already Linked", alerts[0]);
        Assert.Empty(dialog.Linked);
        dialog.Close();
    }

    [AvaloniaFact]
    public void TheListsLoad_OnlyOnceTheDialogIsUp()
    {
        var catalog = new CountingCatalog();
        var dialog = new AppTriggerDialog([], UsedByOthers, catalog);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, catalog.Calls);
        dialog.Close();
    }

    private sealed class CountingCatalog : IAppCatalog
    {
        public int Calls;
        public IReadOnlyList<AppEntry> Installed(CancellationToken ct = default) { Calls++; return []; }
        public IReadOnlyList<AppEntry> Running() { Calls++; return []; }
        public string? TriggerForFile(string path) => null;
    }

    [Theory]
    [InlineData("/Applications/Safari.app", "/applications/safari.app/", true)]
    [InlineData("flatpak:org.a.App", "flatpak:org.a.App", true)]
    [InlineData("flatpak:org.a.App", "/usr/bin/App", false)]
    [InlineData("/usr/bin/firefox", "/opt/firefox/firefox", true)]
    [InlineData("C:\\Games\\Game.exe", "Game.exe", true)]
    [InlineData("/usr/bin/vlc", "/usr/bin/mpv", false)]
    public void SameApp_IsEqualOrTheSameProgram(string a, string b, bool same) =>
        Assert.Equal(same, AppTriggerDialog.SameApp(a, b));

    [Theory]
    [InlineData("flatpak:org.mozilla.firefox", "org.mozilla.firefox (Flatpak)")]
    [InlineData("snap:spotify", "spotify (Snap)")]
    [InlineData("/Applications/Safari.app/", "Safari.app")]
    [InlineData("C:\\Games\\Game.exe", "Game.exe")]
    [InlineData("/usr/bin/steam", "steam")]
    public void TheSecondLine_IsTheFileNameOrSandboxId(string trigger, string detail) =>
        Assert.Equal(detail, AppTriggerDialog.Detail(trigger));

    [AvaloniaFact]
    public void TheWording_FollowsTheSystem()
    {
        var old = Wording.Mac;
        try
        {
            Wording.Mac = true;
            var mac = new AppTriggerDialog([], UsedByOthers, null);
            Assert.Equal("Browse for an app...", Named<Button>(mac, "BrowseButton").Content);
            Assert.Contains("Activity Monitor", Named<TextBlock>(mac, "BackgroundNote").Text);
            Wording.Mac = false;
            var linux = new AppTriggerDialog([], UsedByOthers, null);
            Assert.Equal("Browse for a program...", Named<Button>(linux, "BrowseButton").Content);
            Assert.Contains("System Monitor", Named<TextBlock>(linux, "BackgroundNote").Text);
            Assert.DoesNotContain("Task Manager", Named<TextBlock>(linux, "BackgroundNote").Text);
        }
        finally { Wording.Mac = old; }
    }

    // ── App icons ──────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SvgAndPngIcons_BecomeSmallPictures()
    {
        var dir = Directory.CreateTempSubdirectory("vs-appicons-").FullName;
        try
        {
            // A tall red bar: drawn centred, the sides left clear.
            var svg = System.IO.Path.Combine(dir, "app.svg");
            File.WriteAllText(svg, "<?xml version=\"1.0\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"20\"><rect width=\"10\" height=\"20\" fill=\"#ff0000\"/></svg>");
            using var fromSvg = AppIcons.FromFile(svg, AppIcons.Pixels)!;
            Assert.Equal(AppIcons.Pixels, fromSvg.PixelSize.Width);
            var middle = Pixel(fromSvg, AppIcons.Pixels / 2, AppIcons.Pixels / 2);
            Assert.True(middle[2] > 200 && middle[1] < 50 && middle[3] > 200, "the middle should be red");
            Assert.Equal(0, Pixel(fromSvg, 1, AppIcons.Pixels / 2)[3]);

            var png = System.IO.Path.Combine(dir, "app.png");
            using (var big = new RenderTargetBitmap(new PixelSize(128, 128))) big.Save(png);
            using var fromPng = AppIcons.FromFile(png, AppIcons.Pixels)!;
            Assert.Equal(AppIcons.Pixels, fromPng.PixelSize.Width);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"url(#g)\"/><use xlink:href=\"#a\"/></svg>", true)]
    [InlineData("<svg><image href=\"data:image/png;base64,AAAA\"/></svg>", true)]
    [InlineData("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg/>", true)]
    [InlineData("<svg><image href=\"http://example.com/x.png\"/></svg>", false)]
    [InlineData("<svg><image xlink:href = 'file:///etc/passwd'/></svg>", false)]
    [InlineData("<svg><rect style=\"fill:url(http://example.com/a)\"/></svg>", false)]
    [InlineData("<svg><style>@import 'x.css';</style></svg>", false)]
    [InlineData("<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><svg>&x;</svg>", false)]
    public void SvgIcons_AreDrawnOnlyWhenTheyReferToNothingElse(string svg, bool drawn) =>
        Assert.Equal(drawn, AppIcons.IsSelfContained(svg));

    [Theory]
    [InlineData("flatpak:org.gnome.Calculator", null, "org.gnome.Calculator")]
    [InlineData("/usr/bin/steam", "steam", "steam")]
    [InlineData("/opt/tool/tool", null, null)]
    public void LinuxIcons_ComeFromTheEntry_OrTheFlatpakId(string trigger, string? icon, string? name) =>
        Assert.Equal(name, AppIcons.LinuxIconName(new AppEntry(trigger, "App", icon, false)));

    private static byte[] Pixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4); }
        finally { handle.Free(); }
        return bytes; // B G R A
    }

    // ── Splash ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSplashAnimation_FollowsTheWindowsKeyFrames()
    {
        var start = SplashTimeline.At(0);
        Assert.Equal(0, start.Content);
        Assert.Equal(0, start.Text);
        Assert.Equal(6, start.TextY);
        Assert.Equal(new double[] { 1, 1, 1, 1, 1 }, start.Bars);
        Assert.Equal(1, SplashTimeline.At(0.3).Content, 3);
        // The V's deepest press (0.42 s + 0.624 s) and the centre bar's peak (0.42 s + 1.04 s).
        Assert.Equal(-16, SplashTimeline.At(1.044).V, 3);
        Assert.Equal(2.6, SplashTimeline.At(1.46).Bars[2], 3);
        Assert.Equal(0, SplashTimeline.At(2.03).V, 3);
        var settled = SplashTimeline.At(2.07);
        Assert.Equal(1, settled.Text, 3);
        Assert.Equal(0, settled.TextY, 3);

        // The equalizer: each bar's first value; a short move holds until all restart (every 0.52 s).
        Assert.Equal(new[] { 0.60, 0.70, 0.50, 0.65, 0.55 }, SplashTimeline.At(SplashTimeline.LoopStart).Bars.Select(b => Math.Round(b, 3)));
        Assert.Equal(0.50, SplashTimeline.At(SplashTimeline.LoopStart + 0.40).Bars[2], 3);
        Assert.Equal(2.40, SplashTimeline.At(SplashTimeline.LoopStart + 0.52 + 0.26).Bars[0], 3);
        Assert.Equal(0.50, SplashTimeline.At(SplashTimeline.LoopStart + 0.52).Bars[2], 3); // bar 3 starts again
        Assert.Equal(1.70, SplashTimeline.At(SplashTimeline.LoopStart + 0.52 + 0.108).Bars[2], 3);
        // The loop stops at 3.4 s: the bars back at rest as the fade begins.
        var stopped = SplashTimeline.At(SplashTimeline.LoopEnd);
        Assert.Equal(new double[] { 1, 1, 1, 1, 1 }, stopped.Bars);
        Assert.Equal(1, stopped.Content, 3);

        // Then still, fading out.
        var fading = SplashTimeline.At(3.5);
        Assert.Equal(new double[] { 1, 1, 1, 1, 1 }, fading.Bars);
        Assert.InRange(fading.Content, 0.01, 0.99);
        Assert.Equal(0, SplashTimeline.At(SplashTimeline.End).Content, 3);
    }

    [AvaloniaFact]
    public void TheSplash_PlaysThenCloses()
    {
        var splash = new SplashWindow();
        var t = TimeSpan.Zero;
        splash.Elapsed = () => t;
        var completed = 0;
        splash.AnimationComplete += (_, _) => completed++;
        splash.Show();
        Assert.True(splash.IsPlaying);
        Assert.Equal(0, splash.Shown.Content);

        t = TimeSpan.FromSeconds(1.044);
        splash.Tick();
        Assert.Equal(-16, splash.Shown.V, 3);
        Assert.Equal(1, splash.Shown.Content);

        t = TimeSpan.FromSeconds(3.8);
        splash.Tick();
        Assert.Equal(1, completed);
        Assert.False(splash.IsVisible);
        Assert.False(splash.IsPlaying);

        // Shown still (screenshots): everything in place, no clock.
        var still = new SplashWindow(play: false);
        still.Show();
        Assert.False(still.IsPlaying);
        Assert.Equal(SplashTimeline.Rest.Content, still.Shown.Content);
        Assert.Equal(1, still.Shown.Text);
        still.Close();
    }

    [AvaloniaFact]
    public void TheSplash_FillsTheWindow_WhereItCantBeSeeThrough()
    {
        var splash = new SplashWindow(play: false) { CanSeeThrough = () => false };
        splash.Show();
        Assert.Equal(default, Named<Border>(splash, "ShadowSpace").Margin);
        Assert.Equal(default, Named<Border>(splash, "Card").CornerRadius);
        splash.Close();

        var clear = new SplashWindow(play: false) { CanSeeThrough = () => true };
        clear.Show();
        Assert.Equal(new Thickness(20), Named<Border>(clear, "ShadowSpace").Margin);
        Assert.Equal(new CornerRadius(20), Named<Border>(clear, "Card").CornerRadius);
        clear.Close();
    }

    // ── Headset permission (Linux) ─────────────────────────────────────────────────────────────────

    private static HeadsetAccessDialog OpenAccess(HeadsetAccess state, Func<RuleInstallResult> install, Func<HeadsetAccess>? check = null)
    {
        var dialog = new HeadsetAccessDialog(state, install, check ?? (() => HeadsetAccess.Accessible));
        dialog.Show();
        return dialog;
    }

    [AvaloniaFact]
    public async Task AllowingAccess_InstallsTheRule_ThenChecksAgain()
    {
        var installs = 0;
        var dialog = OpenAccess(HeadsetAccess.NeedsRule, () => { installs++; return RuleInstallResult.Installed; });
        Assert.Contains("Allow Access", dialog.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string));
        Assert.Contains("password", Named<TextBlock>(dialog, "Message").Text);
        Assert.True(Named<TextBlock>(dialog, "Note").IsVisible); // the Bluetooth note

        Click(Named<Button>(dialog, "AllowBtn"));
        await Until(() => dialog.State == HeadsetAccess.Accessible);
        Assert.Equal(1, installs);
        Assert.Equal("Done — headset detection is on.", Named<TextBlock>(dialog, "Status").Text);
        Assert.True(Named<Button>(dialog, "OkBtn").IsVisible);
        Assert.False(Named<Grid>(dialog, "AskButtons").IsVisible);
        Click(Named<Button>(dialog, "OkBtn"));
        Assert.False(dialog.IsVisible);

        // Installed, but the receiver needs replugging.
        dialog = OpenAccess(HeadsetAccess.NeedsRule, () => RuleInstallResult.Installed, () => HeadsetAccess.NeedsReconnect);
        Click(Named<Button>(dialog, "AllowBtn"));
        await Until(() => dialog.State == HeadsetAccess.NeedsReconnect);
        Assert.Contains("Unplug", Named<TextBlock>(dialog, "Message").Text);
        dialog.Close();
    }

    [AvaloniaTheory]
    [InlineData(RuleInstallResult.Cancelled, "Permission wasn't given", true)]
    [InlineData(RuleInstallResult.Failed, "Couldn't add the rule", true)]
    [InlineData(RuleInstallResult.NoPrivilegeTool, ".deb package", false)]
    public async Task AFailedInstall_SaysWhy(RuleInstallResult result, string says, bool canRetry)
    {
        var dialog = OpenAccess(HeadsetAccess.NeedsRule, () => result);
        var allow = Named<Button>(dialog, "AllowBtn");
        Click(allow);
        await Until(() => dialog.LastInstall == result && allow.IsEnabled);
        Assert.Contains(says, Named<TextBlock>(dialog, "Status").Text);
        Assert.Equal(canRetry, Named<Grid>(dialog, "AskButtons").IsVisible);
        Assert.Equal("Allow Access", allow.Content);
        Assert.Equal(HeadsetAccess.NeedsRule, dialog.State);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task ARuleTheUserTurnedOff_IsLeftAlone()
    {
        var dialog = OpenAccess(HeadsetAccess.TurnedOff, () => throw new InvalidOperationException("never called"));
        Assert.Contains(LinuxHidAccess.RuleFileName, Named<TextBlock>(dialog, "Message").Text);
        Assert.False(Named<TextBlock>(dialog, "Note").IsVisible);
        Assert.False(Named<Grid>(dialog, "AskButtons").IsVisible);
        Key(dialog, Avalonia.Input.Key.Escape);
        Assert.False(dialog.IsVisible);

        // Turned off between the check and the install.
        dialog = OpenAccess(HeadsetAccess.NeedsRule, () => RuleInstallResult.TurnedOffByUser);
        Click(Named<Button>(dialog, "AllowBtn"));
        await Until(() => dialog.State == HeadsetAccess.TurnedOff);
        Assert.True(Named<Button>(dialog, "OkBtn").IsVisible);
        dialog.Close();

        // An install that throws is reported, not raised.
        dialog = OpenAccess(HeadsetAccess.NeedsRule, () => throw new IOException("boom"));
        Click(Named<Button>(dialog, "AllowBtn"));
        await Until(() => Named<TextBlock>(dialog, "Status").IsVisible);
        Assert.Contains("Couldn't add the rule", Named<TextBlock>(dialog, "Status").Text);
        dialog.Close();
    }

    [AvaloniaFact]
    public void TurningOnAutoSwitch_OffersHeadsetAccess_OnlyWhenItsNeeded()
    {
        var owner = new Window();
        owner.Show();
        var checks = 0;
        var state = HeadsetAccess.NeedsRule;
        IDialogService service = new DialogService(() => owner)
        {
            Headsets = new HeadsetAccessHooks(() => { checks++; return state; }, () => RuleInstallResult.Cancelled),
        };

        // Needed: the permission dialog follows (and "Not Now" still turns auto-switch on).
        ClickLater(owner, "Enable Auto-Switch");
        ClickLater(owner, "Not Now", d => Assert.IsType<HeadsetAccessDialog>(d));
        Assert.True(service.ShowSupportedHeadsets());
        Assert.Equal(1, checks);

        // Not needed: no second dialog.
        state = HeadsetAccess.NoHeadset;
        ClickLater(owner, "Enable Auto-Switch");
        Assert.True(service.ShowSupportedHeadsets());
        Assert.Equal(2, checks);
        Assert.DoesNotContain(owner.OwnedWindows, w => w.IsVisible);

        // Closed: nothing checked.
        ClickLater(owner, "Close");
        Assert.False(service.ShowSupportedHeadsets());
        Assert.Equal(2, checks);
        owner.Close();
    }
}
