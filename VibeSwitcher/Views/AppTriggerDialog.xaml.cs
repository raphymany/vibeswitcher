using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VibeSwitcher.Views;

public partial class AppTriggerDialog : Window
{
    private record ProcessEntry(string ExePath, string ExeName, string DisplayName, ImageSource? Icon,
        string? ConflictingProfile, bool IsRunning = false);

    private enum AppFilter { All, Running, Installed, InUse }

    private readonly List<string> _linked;
    private readonly IReadOnlyDictionary<string, string> _usedByOthers;
    private List<ProcessEntry> _runningEntries = [];
    private List<ProcessEntry> _installedEntries = [];
    private List<ProcessEntry> _allEntries = [];
    private AppFilter _activeFilter = AppFilter.All;

    public List<string>? ResultTriggers { get; private set; }

    public AppTriggerDialog(List<string> currentTriggers, IReadOnlyDictionary<string, string> usedByOthers)
    {
        InitializeComponent();
        _linked = new List<string>(currentTriggers);
        _usedByOthers = usedByOthers;

        UpdateFilterChips();
        RebuildLinkedPanel();
        LoadRunningAppsAsync();
        LoadInstalledAppsAsync();
    }

    // ── Linked apps panel ────────────────────────────────────────────────────

    private void RebuildLinkedPanel()
    {
        LinkedPanel.Children.Clear();
        EmptyLabel.Visibility = _linked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (var i = 0; i < _linked.Count; i++)
        {
            var path = _linked[i];
            var exeName = Path.GetFileName(path);
            var displayName = GetDisplayName(path);
            var icon = LoadIcon(path);
            var index = i;

            var row = new Grid { Margin = new Thickness(8, 3, 8, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (icon != null)
            {
                var img = new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
                Grid.SetColumn(img, 0);
                row.Children.Add(img);
            }

            var nameBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var displayBlock = new TextBlock
            {
                Text = displayName, FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            };
            displayBlock.SetResourceReference(ForegroundProperty, "PrimaryText");

            var exeBlock = new TextBlock
            {
                Text = exeName, FontSize = 11, Margin = new Thickness(0, 1, 0, 0),
            };
            exeBlock.SetResourceReference(ForegroundProperty, "SecondaryText");

            nameBlock.Children.Add(displayBlock);
            nameBlock.Children.Add(exeBlock);
            Grid.SetColumn(nameBlock, 1);
            row.Children.Add(nameBlock);

            var removeBtn = new Button
            {
                Content = "✕",
                FontSize = 11,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 2, 6, 2),
                ToolTip = "Remove",
            };
            removeBtn.SetResourceReference(StyleProperty, "ActionButton");
            removeBtn.Click += (_, _) =>
            {
                _linked.RemoveAt(index);
                RebuildLinkedPanel();
                ApplyFilter(SearchBox.Text);
            };
            Grid.SetColumn(removeBtn, 2);
            row.Children.Add(removeBtn);

            LinkedPanel.Children.Add(row);

            if (i < _linked.Count - 1)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(8, 3, 8, 3) };
                sep.SetResourceReference(BackgroundProperty, "CardSeparator");
                LinkedPanel.Children.Add(sep);
            }
        }
    }

    // ── Filter chips ─────────────────────────────────────────────────────────

    private void FilterChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border chip) return;
        _activeFilter = (chip.Tag as string) switch
        {
            "Running"   => AppFilter.Running,
            "Installed" => AppFilter.Installed,
            "InUse"     => AppFilter.InUse,
            _           => AppFilter.All,
        };
        UpdateFilterChips();
        ApplyFilter(SearchBox.Text);
    }

    private void UpdateFilterChips()
    {
        SetChip(ChipAll,       _activeFilter == AppFilter.All);
        SetChip(ChipRunning,   _activeFilter == AppFilter.Running);
        SetChip(ChipInstalled, _activeFilter == AppFilter.Installed);
        SetChip(ChipInUse,     _activeFilter == AppFilter.InUse);
    }

    private static void SetChip(Border chip, bool active)
    {
        if (active)
        {
            chip.Background  = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            chip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            if (chip.Child is TextBlock tb) tb.Foreground = Brushes.White;
        }
        else
        {
            chip.Background = Brushes.Transparent;
            chip.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            if (chip.Child is TextBlock tb)
                tb.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        }
    }

    // ── App discovery ────────────────────────────────────────────────────────

    private void LoadRunningAppsAsync()
    {
        _ = Task.Run(DiscoverRunning).ContinueWith(t =>
            Dispatcher.InvokeAsync(() =>
            {
                _runningEntries = t.Result;
                RebuildAllEntries();
                ApplyFilter(SearchBox.Text);
            }), TaskScheduler.Default);
    }

    private void LoadInstalledAppsAsync()
    {
        _ = Task.Run(DiscoverInstalled).ContinueWith(t =>
            Dispatcher.InvokeAsync(() =>
            {
                _installedEntries = t.Result;
                RebuildAllEntries();
                ApplyFilter(SearchBox.Text);
            }), TaskScheduler.Default);
    }

    private void RebuildAllEntries()
    {
        var runningNames = _runningEntries
            .Select(e => Path.GetFileNameWithoutExtension(e.ExePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var installedOnly = _installedEntries
            .Where(e => !runningNames.Contains(Path.GetFileNameWithoutExtension(e.ExePath)));

        _allEntries = _runningEntries
            .Concat(installedOnly)
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<ProcessEntry> DiscoverRunning()
    {
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var sysWow = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var selfPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ProcessEntry>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) continue;
                if (path.StartsWith(systemDir, StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith(sysWow, StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith(winDir + @"\System", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(path, selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(path)) continue;

                var exeName = Path.GetFileName(path);
                var displayName = GetDisplayName(path);
                var icon = LoadIcon(path);
                _usedByOthers.TryGetValue(path, out var conflict);

                results.Add(new ProcessEntry(path, exeName, displayName, icon, conflict, IsRunning: true));
            }
            catch { }
        }

        results.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private List<ProcessEntry> DiscoverInstalled()
    {
        var selfPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ProcessEntry>();

        // Source 1: registry Uninstall keys — covers Win32 installers on every drive
        results.AddRange(ScanUninstallRegistry(seen, selfPath));

        // Source 2: AppPaths registry — apps registered for the Run dialog
        results.AddRange(ScanAppPaths(seen, selfPath));

        // Source 3: Start Menu shortcuts — catches Store apps and anything with a Start Menu entry
        results.AddRange(ScanStartMenuShortcuts(seen, selfPath));

        results.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private List<ProcessEntry> ScanUninstallRegistry(HashSet<string> seen, string selfPath)
    {
        var results = new List<ProcessEntry>();
        var hiveKeys = new (RegistryKey Hive, string SubPath)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        foreach (var (hive, subPath) in hiveKeys)
        {
            try
            {
                using var key = hive.OpenSubKey(subPath);
                if (key == null) continue;

                foreach (var name in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(name);
                        if (sub == null) continue;
                        if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (sub.GetValue("ParentKeyName") is string parent && !string.IsNullOrEmpty(parent)) continue;

                        var displayName = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        var exePath = ExtractExePath(sub);
                        if (string.IsNullOrEmpty(exePath)) continue;
                        if (!File.Exists(exePath)) continue;
                        if (string.Equals(exePath, selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!seen.Add(exePath)) continue;

                        _usedByOthers.TryGetValue(exePath, out var conflict);
                        results.Add(new ProcessEntry(exePath, Path.GetFileName(exePath),
                            displayName.Trim(), LoadIcon(exePath), conflict));
                    }
                    catch { }
                }
            }
            catch { }
        }

        return results;
    }

    private List<ProcessEntry> ScanAppPaths(HashSet<string> seen, string selfPath)
    {
        var results = new List<ProcessEntry>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\AppPaths");
            if (key == null) return results;

            foreach (var name in key.GetSubKeyNames())
            {
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var sub = key.OpenSubKey(name);
                    var path = (sub?.GetValue(null) as string)?.Trim().Trim('"');
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(path)) continue;
                    if (string.Equals(path, selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(path)) continue;

                    _usedByOthers.TryGetValue(path, out var conflict);
                    results.Add(new ProcessEntry(path, Path.GetFileName(path),
                        GetDisplayName(path), LoadIcon(path), conflict));
                }
                catch { }
            }
        }
        catch { }

        return results;
    }

    private List<ProcessEntry> ScanStartMenuShortcuts(HashSet<string> seen, string selfPath)
    {
        var lnkFiles = new List<string>();
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),       // per-user
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), // all-users
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            try { lnkFiles.AddRange(Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories)); }
            catch { }
        }

        if (lnkFiles.Count == 0) return [];

        // Resolve all shortcut targets in a single STA thread (WScript.Shell COM requirement)
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var staThread = new Thread(() =>
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType)!;

                foreach (var lnk in lnkFiles)
                {
                    try
                    {
                        dynamic shortcut = shell.CreateShortcut(lnk);
                        string target = shortcut.TargetPath;
                        if (!string.IsNullOrEmpty(target))
                            resolved[lnk] = target;
                    }
                    catch { }
                }
            }
            catch { }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();
        staThread.Join(TimeSpan.FromSeconds(15));

        var results = new List<ProcessEntry>();
        foreach (var (lnk, target) in resolved)
        {
            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(target)) continue;
            if (string.Equals(target, selfPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(target)) continue;

            _usedByOthers.TryGetValue(target, out var conflict);
            var displayName = Path.GetFileNameWithoutExtension(lnk);
            results.Add(new ProcessEntry(target, Path.GetFileName(target),
                displayName, LoadIcon(target), conflict));
        }

        return results;
    }

    private static string? ExtractExePath(RegistryKey key)
    {
        // DisplayIcon is the most reliable source: "C:\path\app.exe,0" or just "C:\path\app.exe"
        var icon = key.GetValue("DisplayIcon") as string;
        if (!string.IsNullOrEmpty(icon))
        {
            var comma = icon.LastIndexOf(',');
            var candidate = (comma >= 0 ? icon[..comma] : icon).Trim().Trim('"');
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // ── App list display ─────────────────────────────────────────────────────

    private void ApplyFilter(string filter)
    {
        RunningPanel.Children.Clear();

        IEnumerable<ProcessEntry> source = _activeFilter switch
        {
            AppFilter.Running   => _allEntries.Where(e => e.IsRunning),
            AppFilter.Installed => _allEntries.Where(e => !e.IsRunning),
            AppFilter.InUse     => _allEntries.Where(e =>
                e.ConflictingProfile != null ||
                _linked.Any(l => string.Equals(Path.GetFileNameWithoutExtension(l),
                    Path.GetFileNameWithoutExtension(e.ExePath), StringComparison.OrdinalIgnoreCase))),
            _                   => _allEntries,
        };

        var matches = source
            .Where(e => string.IsNullOrWhiteSpace(filter)
                || e.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.ExeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var emptyMessage = (string.IsNullOrWhiteSpace(filter), _activeFilter) switch
        {
            (false, _)                      => "No matches found.",
            (true, AppFilter.Running)       => "No running apps detected.",
            (true, AppFilter.Installed)     => "No installed apps found.",
            (true, AppFilter.InUse)         => "No apps are in use yet.",
            _                               => "No apps detected.",
        };

        if (matches.Count == 0)
        {
            var empty = new TextBlock { Text = emptyMessage, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
            empty.SetResourceReference(ForegroundProperty, "SecondaryText");
            RunningPanel.Children.Add(empty);
            return;
        }

        foreach (var entry in matches)
        {
            var isLinkedHere = _linked.Any(l =>
                string.Equals(Path.GetFileNameWithoutExtension(l),
                              Path.GetFileNameWithoutExtension(entry.ExePath),
                              StringComparison.OrdinalIgnoreCase));

            var isDisabled = isLinkedHere || entry.ConflictingProfile != null;
            RunningPanel.Children.Add(BuildRow(entry, isLinkedHere, isDisabled));
        }
    }

    private UIElement BuildRow(ProcessEntry entry, bool isLinkedHere, bool isDisabled)
    {
        var row = new Grid { Margin = new Thickness(8, 4, 8, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (entry.Icon != null)
        {
            var img = new Image
            {
                Source = entry.Icon, Width = 18, Height = 18,
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Opacity = isDisabled ? 0.4 : 1.0,
            };
            Grid.SetColumn(img, 0);
            row.Children.Add(img);
        }

        var nameBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var displayTb = new TextBlock
        {
            Text = entry.DisplayName, FontSize = 12,
            Opacity = isDisabled ? 0.45 : 1.0,
        };
        displayTb.SetResourceReference(ForegroundProperty, "PrimaryText");

        var exeTb = new TextBlock
        {
            Text = entry.ExeName, FontSize = 10,
            Margin = new Thickness(0, 1, 0, 0),
            Opacity = isDisabled ? 0.45 : 1.0,
        };
        exeTb.SetResourceReference(ForegroundProperty, "SecondaryText");

        nameBlock.Children.Add(displayTb);
        nameBlock.Children.Add(exeTb);

        if (entry.IsRunning)
        {
            nameBlock.Children.Add(new TextBlock
            {
                Text = "● Running",
                FontSize = 9,
                Margin = new Thickness(0, 1, 0, 0),
                Opacity = isDisabled ? 0.45 : 1.0,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
            });
        }

        Grid.SetColumn(nameBlock, 1);
        row.Children.Add(nameBlock);

        if (isLinkedHere)
        {
            var badge = MakeBadge("Added", "#4CAF50", "#E8F5E9");
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
        }
        else if (entry.ConflictingProfile != null)
        {
            var badge = MakeBadge($"Used by \"{entry.ConflictingProfile}\"", "#E65C00", "#FFF8E1");
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
        }
        else
        {
            var addBtn = new Button
            {
                Content = "+ Add", FontSize = 11, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 3, 8, 3),
            };
            addBtn.SetResourceReference(StyleProperty, "ActionButton");
            addBtn.Click += (_, _) =>
            {
                _linked.Add(entry.ExePath);
                RebuildLinkedPanel();
                ApplyFilter(SearchBox.Text);
            };
            Grid.SetColumn(addBtn, 2);
            row.Children.Add(addBtn);
        }

        if (!isDisabled)
        {
            var wrapper = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 1, 0, 1) };
            wrapper.Child = row;
            return wrapper;
        }

        return row;
    }

    private static Border MakeBadge(string text, string fgHex, string bgHex)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)),
            },
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetDisplayName(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = info.ProductName ?? info.FileDescription;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    private static ImageSource? LoadIcon(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            var bmp = icon.ToBitmap();
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var src = new BitmapImage();
            src.BeginInit();
            src.StreamSource = ms;
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        => SearchPlaceholder.Visibility = Visibility.Collapsed;

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        => SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter(SearchBox.Text);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Executable",
            Filter = "Executables (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;

        var alreadyLinked = _linked.Any(l =>
            string.Equals(Path.GetFileNameWithoutExtension(l),
                          Path.GetFileNameWithoutExtension(path),
                          StringComparison.OrdinalIgnoreCase));
        if (alreadyLinked)
        {
            new AlertDialog("Already Added", $"{Path.GetFileName(path)} is already linked to this profile.")
            { Owner = this }.ShowDialog();
            return;
        }

        if (_usedByOthers.TryGetValue(path, out var otherProfile))
        {
            new AlertDialog("Already Linked",
                $"{Path.GetFileName(path)} is already linked to \"{otherProfile}\". Remove it from that profile first.")
            { Owner = this }.ShowDialog();
            return;
        }

        _linked.Add(path);
        RebuildLinkedPanel();
        ApplyFilter(SearchBox.Text);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        ResultTriggers = new List<string>(_linked);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
