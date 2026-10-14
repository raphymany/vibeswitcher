using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VibeSwitcher.Helpers;
using VibeSwitcher.Views;

namespace WpfSnapshots;

// Renders the Windows dialogs the macOS/Linux app has ported, with the same content as its screenshot
// tests (xplat/tests/VibeSwitcher.App.Tests/DialogCatalog.cs), and writes a side-by-side page.
//
//   dotnet run --project xplat/tools/WpfSnapshots -- <output folder> [<Linux shots> [<macOS shots>]]
//
// The Windows app isn't started: its App.xaml resources (styles, icons) and theme dictionaries are
// loaded into a bare application object, then each dialog is laid out off-screen and drawn.
internal static class Program
{
    private static readonly string[] Themes = ["light", "dark"];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: WpfSnapshots <output folder> [<Linux screenshots folder> [<macOS screenshots folder>]]");
            return 2;
        }
        var outDir = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.Combine(outDir, "windows"));
        string? linuxShots = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        string? macShots = args.Length > 2 ? Path.GetFullPath(args[2]) : linuxShots;
        foreach (var dir in new[] { linuxShots, macShots })
            if (dir != null && !Directory.Exists(dir))
            {
                Console.Error.WriteLine($"No screenshots at {dir} (run the VibeSwitcher.App.Tests screenshot tests first).");
                return 2;
            }

        // A plain application with the Windows App.xaml resources (styles, shared icons) as its own
        // resources, as in the Windows app. The Windows App class is never created: WPF would run its
        // start-up (single instance, tray, logging).
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = AppResources() };

        var sizes = new Dictionary<string, Size>();
        foreach (var theme in Themes)
        {
            UseTheme(app, theme);
            foreach (var name in Dialogs.Names)
            {
                var file = $"{name}-{theme}-windows.png";
                sizes[file] = Render(Dialogs.Create(name), Path.Combine(outDir, "windows", file));
                Console.WriteLine($"rendered {file} ({sizes[file].Width}x{sizes[file].Height})");
            }
        }

        if (linuxShots != null) Report.Write(outDir, linuxShots, macShots!, sizes);
        app.Shutdown();
        return 0;
    }

    // App.xaml's <Application.Resources> dictionary, parsed on its own: the namespaces of the root
    // carried over, x:Class dropped, and the relative Icons.xaml source made absolute.
    private static ResourceDictionary AppResources()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Wpf.App.xaml")!;
        var doc = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dict = doc.Root!.Element(wpf + "Application.Resources")!.Element(wpf + "ResourceDictionary")!;
        foreach (var ns in doc.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
            dict.SetAttributeValue(ns.Name, ns.Value);
        foreach (var source in dict.Descendants(wpf + "ResourceDictionary").Select(d => d.Attribute("Source")).OfType<System.Xml.Linq.XAttribute>())
            if (!source.Value.StartsWith("pack:", StringComparison.Ordinal))
                source.Value = "pack://application:,,,/VibeSwitcher;component/" + source.Value.TrimStart('/');
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dict.ToString());
    }

    // What ThemeService does: the theme dictionary goes into the application resources' merged
    // dictionaries (so App.xaml's own keys still win over the theme's, as in the app).
    private static void UseTheme(Application app, string theme)
    {
        var dicts = app.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Theme.xaml") == true);
        if (old != null) dicts.Remove(old);
        var name = theme == "dark" ? "DarkTheme" : "LightTheme";
        dicts.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/VibeSwitcher;component/Themes/{name}.xaml") });
    }

    // Lays the dialog out off-screen and draws its content on the window background (WindowChrome
    // draws no frame of its own: the title bar is part of the content), at its exact size, with the
    // 8 px rounded corners Windows 11 gives the window (transparent outside them).
    private static Size Render(Window window, string path)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var content = (FrameworkElement)window.Content;
        var exact = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        int width = (int)Math.Ceiling(exact.Width);
        int height = (int)Math.Ceiling(exact.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), 8, 8));
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            // Unstretched, top-left: drawn 1:1 even when the layout size has a fraction.
            var brush = new VisualBrush(content)
            {
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = exact,
                ViewportUnits = BrushMappingMode.Absolute, Viewport = exact,
            };
            dc.DrawRectangle(brush, null, exact);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        window.Close();
        return new Size(width, height);
    }
}

// The same dialogs and texts as the macOS/Linux screenshot tests (keep in step with
// xplat/tests/VibeSwitcher.App.Tests/DialogCatalog.cs).
internal static class Dialogs
{
    public static readonly string[] Names =
        ["alert", "alert-info", "confirm", "confirm-delete", "conflict-retry", "error", "uninstall", "mini-setup"];

    public static Window Create(string name) => name switch
    {
        "alert" => new AlertDialog("Device Not Found", "The headset in this profile isn't connected. Plug it in, or pick another device."),
        "alert-info" => new AlertDialog("Mini Mode", "Mini Mode keeps your profiles one click away in a small window.", AlertKind.Info),
        "confirm" => new ConfirmDialog("Replace Configuration?", "This replaces all your profiles and settings with the imported ones.", "Replace", subtitle: ""),
        "confirm-delete" => new ConfirmDeleteDialog("Gaming"),
        "conflict-retry" => new ConflictRetryDialog("Hotkey Already in Use", "Ctrl + Alt + G is used by another app. Try a different shortcut?"),
        "error" => new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
            "Couldn't switch to \"Gaming\": the playback device isn't available.", new NullLogger(), new SessionErrorTracker()),
        "uninstall" => new UninstallDialog(),
        "mini-setup" => new MiniModeSetupDialog("Rows",
        [
            new MiniModeSetupDialog.ProfileChoice { Name = "Gaming", IsSelected = true },
            new MiniModeSetupDialog.ProfileChoice { Name = "Work", IsSelected = true },
            new MiniModeSetupDialog.ProfileChoice { Name = "Calls" },
        ]),
        _ => throw new ArgumentException(name),
    };

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string context, string message) { }
        public void Warning(string context, string message) { }
        public void Error(string context, string message) { }
        public void Error(string context, Exception ex) { }
        public void Debug(string context, string message) { }
    }
}

// parity.html: each dialog's Windows, Linux-frame and macOS-frame versions side by side.
internal static class Report
{
    // Linux-frame images come from linuxShots and macOS-frame ones from macShots (CI: each rendered on
    // its own system).
    public static void Write(string outDir, string linuxShots, string macShots, IReadOnlyDictionary<string, Size> windowsSizes)
    {
        var xplatOut = Path.Combine(outDir, "xplat");
        Directory.CreateDirectory(xplatOut);
        foreach (var (dir, suffix) in new[] { (linuxShots, "-linux.png"), (macShots, "-mac.png") })
            foreach (var png in Directory.EnumerateFiles(dir, "*" + suffix))
                File.Copy(png, Path.Combine(xplatOut, Path.GetFileName(png)), overwrite: true);

        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html><head><meta charset="utf-8"><title>VibeSwitcher: Windows vs macOS/Linux</title>
            <style>
              body { font: 13px system-ui, sans-serif; background: #f3f3f3; color: #222; margin: 24px; }
              h1 { font-size: 18px; } h2 { font-size: 14px; margin: 28px 0 8px; }
              .row { display: flex; gap: 16px; align-items: flex-start; flex-wrap: wrap; }
              figure { margin: 0; background: #fff; padding: 8px; border-radius: 6px; box-shadow: 0 1px 3px #0002; }
              figcaption { font-size: 11px; color: #666; margin-top: 4px; }
              .diff { color: #b00; }
            </style></head><body>
            <h1>VibeSwitcher dialogs: Windows (WPF) vs macOS/Linux (Avalonia)</h1>
            <p>Same dialogs and text (the uninstall dialog says what happens on each system on purpose). Windows is
            drawn by the Windows app itself; Linux and macOS by the new app, each on its own system in CI (macOS
            leaves room for its system window buttons; Linux draws the Windows-style ones). Text wraps a little
            differently where Open Sans stands in for Segoe UI.</p>
            """);
        foreach (var name in Dialogs.Names)
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                html.Append($"<h2>{WebUtility.HtmlEncode(name)} — {theme}</h2><div class=\"row\">");
                var win = $"{name}-{theme}-windows.png";
                var winSize = windowsSizes.TryGetValue(win, out var s) ? s : Size.Empty;
                html.Append(Figure($"windows/{win}", "Windows", winSize, null));
                foreach (var (chrome, label) in new[] { ("linux", "Linux"), ("mac", "macOS") })
                {
                    var file = $"{name}-{theme}-{chrome}.png";
                    var path = Path.Combine(xplatOut, file);
                    if (!File.Exists(path)) { html.Append($"<figure><figcaption class=\"diff\">{label}: missing</figcaption></figure>"); continue; }
                    html.Append(Figure($"xplat/{file}", label, ImageSize(path), winSize));
                }
                html.Append("</div>");
            }
        }
        html.Append("</body></html>");
        File.WriteAllText(Path.Combine(outDir, "parity.html"), html.ToString());
        Console.WriteLine($"wrote {Path.Combine(outDir, "parity.html")}");
    }

    private static string Figure(string src, string label, Size size, Size? windowsSize)
    {
        var caption = $"{label} — {size.Width}×{size.Height}";
        if (windowsSize is { IsEmpty: false } w && (w.Width != size.Width || w.Height != size.Height))
            caption += $" <span class=\"diff\">({size.Width - w.Width:+0;-0;0}×{size.Height - w.Height:+0;-0;0} vs Windows)</span>";
        return $"<figure><img src=\"{src}\" width=\"{size.Width}\" height=\"{size.Height}\" alt=\"{WebUtility.HtmlEncode(label)}\"><figcaption>{caption}</figcaption></figure>";
    }

    private static Size ImageSize(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return new Size(frame.PixelWidth, frame.PixelHeight);
    }
}
