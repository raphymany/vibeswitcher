using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;

namespace VibeSwitcher.App.Tests;

// Screenshots of every dialog in light and dark, with the Linux frame and the macOS one. They're
// saved to screenshots/ next to the test binaries on every OS (CI keeps them for review). On Linux —
// where CI renders them the same way every time — they're also compared with Baselines/, so the
// look can't change unnoticed. After an intended change: VS_UPDATE_SCREENSHOTS=1 on Linux rewrites
// the baselines (review them before committing; scripts/wsl-test.sh works on a copy of the repo, so
// copy ~/vs-src/xplat/tests/VibeSwitcher.App.Tests/Baselines back from there).
public class ScreenshotTests
{
    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var name in DialogCatalog.Names)
            foreach (var theme in new[] { "light", "dark" })
                foreach (var chrome in new[] { "linux", "mac" })
                    data.Add(name, theme, chrome);
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Dialog(string name, string theme, string chrome)
    {
        var app = Application.Current!;
        var oldChrome = WindowChrome.Style;
        WindowChrome.Style = chrome == "mac" ? ChromeStyle.Mac : ChromeStyle.Custom;
        app.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var window = DialogCatalog.Create(name);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
            window.Close();

            var file = $"{name}-{theme}-{chrome}.png";
            var shot = Path.Combine(AppContext.BaseDirectory, "screenshots", file);
            Directory.CreateDirectory(Path.GetDirectoryName(shot)!);
            frame.Save(shot);

            if (!OperatingSystem.IsLinux()) return;
            var baseline = Path.Combine(Baselines, file);
            if (Environment.GetEnvironmentVariable("VS_UPDATE_SCREENSHOTS") == "1")
            {
                Directory.CreateDirectory(Baselines);
                File.Copy(shot, baseline, overwrite: true);
                return;
            }
            Assert.True(File.Exists(baseline), $"No baseline for {file}: run the tests on Linux with VS_UPDATE_SCREENSHOTS=1.");
            var diff = Difference(shot, baseline);
            Assert.True(diff.SameSize, $"{file}: size changed ({diff.Detail}).");
            Assert.True(diff.Fraction <= 0.002, $"{file}: {diff.Fraction:P2} of pixels differ from the baseline (see {shot}).");
        }
        finally
        {
            WindowChrome.Style = oldChrome;
            app.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    private static string Baselines =>
        Path.Combine(FindUp("VibeSwitcher.XPlat.sln"), "tests", "VibeSwitcher.App.Tests", "Baselines");

    private static string FindUp(string marker)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, marker))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("xplat folder not found");
    }

    // The share of pixels where any channel differs by more than a little (anti-aliasing noise).
    private static (bool SameSize, double Fraction, string Detail) Difference(string a, string b)
    {
        using var x = new Bitmap(a);
        using var y = new Bitmap(b);
        if (x.PixelSize != y.PixelSize) return (false, 1, $"{x.PixelSize} vs {y.PixelSize}");
        var pa = Pixels(x);
        var pb = Pixels(y);
        int differing = 0;
        for (int i = 0; i < pa.Length; i += 4)
        {
            for (int c = 0; c < 4; c++)
            {
                if (Math.Abs(pa[i + c] - pb[i + c]) > 24) { differing++; break; }
            }
        }
        return (true, differing / (pa.Length / 4.0), "");
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        int stride = bitmap.PixelSize.Width * 4;
        var bytes = new byte[stride * bitmap.PixelSize.Height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { handle.Free(); }
        return bytes;
    }
}
