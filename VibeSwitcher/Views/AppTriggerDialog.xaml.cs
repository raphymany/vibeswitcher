using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VibeSwitcher.Views;

public partial class AppTriggerDialog : Window
{
    private record ProcessEntry(string ExePath, string ExeName, string DisplayName, ImageSource? Icon,
        string? ConflictingProfile);

    private readonly List<string> _linked;
    private readonly IReadOnlyDictionary<string, string> _usedByOthers;
    private List<ProcessEntry> _runningEntries = [];

    public List<string>? ResultTriggers { get; private set; }

    public AppTriggerDialog(List<string> currentTriggers, IReadOnlyDictionary<string, string> usedByOthers)
    {
        InitializeComponent();
        _linked = new List<string>(currentTriggers);
        _usedByOthers = usedByOthers;

        RebuildLinkedPanel();
        LoadRunningAppsAsync();
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

    // ── Running apps discovery ───────────────────────────────────────────────

    private void LoadRunningAppsAsync()
    {
        _ = Task.Run(DiscoverRunning).ContinueWith(t =>
            Dispatcher.InvokeAsync(() =>
            {
                _runningEntries = t.Result;
                ApplyFilter(SearchBox.Text);
            }), TaskScheduler.Default);
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

                results.Add(new ProcessEntry(path, exeName, displayName, icon, conflict));
            }
            catch { }
        }

        results.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private void ApplyFilter(string filter)
    {
        RunningPanel.Children.Clear();

        var matches = _runningEntries
            .Where(e => string.IsNullOrWhiteSpace(filter)
                || e.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.ExeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(filter)
                    ? "No running apps detected."
                    : "No matches found.",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 12),
            };
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

            var row = BuildRunningRow(entry, isLinkedHere, isDisabled);
            RunningPanel.Children.Add(row);
        }
    }

    private UIElement BuildRunningRow(ProcessEntry entry, bool isLinkedHere, bool isDisabled)
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

        // Wrap in a border for hover if clickable
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
