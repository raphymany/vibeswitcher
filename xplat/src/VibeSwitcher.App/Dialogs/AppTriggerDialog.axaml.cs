using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/AppTriggerDialog.xaml.cs: the apps that switch to this profile when they start. The
// lists come from the system's app catalog (macOS: app bundles; Linux: app-menu entries, Flatpak,
// Snap, AppImages and programs), loaded off the UI thread as on Windows; what's stored is each app's
// trigger (see AppTriggerMatch). Windows compares apps by program name; here two triggers are the
// same app when they're equal or name the same program (which is how they'd match a process too).
public partial class AppTriggerDialog : VsDialog
{
    // One app in the list. Detail: the second line (Windows: the .exe file name).
    internal sealed record AppItem(string Trigger, string Name, string Detail, IImage? Icon, string? ConflictingProfile, bool IsRunning);

    internal enum AppFilter { All, Running, Installed, InUse }

    private readonly List<string> _linked;
    private readonly IReadOnlyDictionary<string, string> _usedByOthers;
    private readonly IAppCatalog? _catalog;
    private readonly Func<AppEntry, IImage?> _loadIcon;
    private List<AppItem> _runningEntries = [];
    private List<AppItem> _installedEntries = [];
    private List<AppItem> _allEntries = [];
    private AppFilter _activeFilter = AppFilter.All;
    private readonly CancellationTokenSource _loadCts = new();
    private bool _loadStarted, _listsLoaded, _iconsLoaded;

    public List<string>? ResultTriggers { get; private set; }

    public AppTriggerDialog() : this([], new Dictionary<string, string>(), null) { } // (for the XAML loader)

    public AppTriggerDialog(List<string> currentTriggers, IReadOnlyDictionary<string, string> usedByOthers,
        IAppCatalog? catalog, Func<AppEntry, IImage?>? loadIcon = null)
    {
        AvaloniaXamlLoader.Load(this);
        _linked = new List<string>(currentTriggers);
        _usedByOthers = usedByOthers;
        _catalog = catalog;
        _loadIcon = loadIcon ?? (_ => null);
        PickFile = PickFileAsync;
        ShowAlert = (title, message) => new AlertDialog(title, message).ShowDialog(this);

        Named<TextBlock>("BackgroundNote").Text = Wording.AppTriggerBackgroundNote;
        Named<Button>("BrowseButton").Content = Wording.BrowseApp;

        UpdateFilterChips();
        RebuildLinkedPanel();
        // (Loaded once the dialog is up, so a dialog that never shows lists nothing.)
        Opened += (_, _) => StartLoading();
        Closed += (_, _) => _loadCts.Cancel();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseWith(false); e.Handled = true; }
        };
    }

    private T Named<T>(string name) where T : Control => this.FindControl<T>(name)!;

    private Avalonia.Styling.ControlTheme? ActionTheme => this.FindResource("ActionButton") as Avalonia.Styling.ControlTheme;

    // The file picker and the dialog's own alerts (tests stand in for them).
    internal Func<Task<string?>> PickFile { get; set; }
    internal Func<string, string, Task> ShowAlert { get; set; }

    // True once the lists and their icons have arrived (or failed).
    internal bool AppsLoaded => _listsLoaded && _iconsLoaded;

    // ── Linked apps ────────────────────────────────────────────────────────────────────────────────

    private void RebuildLinkedPanel()
    {
        var panel = Named<StackPanel>("LinkedPanel");
        panel.Children.Clear();
        Named<TextBlock>("EmptyLabel").IsVisible = _linked.Count == 0;

        for (var i = 0; i < _linked.Count; i++)
        {
            var trigger = _linked[i];
            // What the lists know about it (its name and icon), once they've loaded.
            var known = _allEntries.FirstOrDefault(e => e.Trigger == trigger) ?? _allEntries.FirstOrDefault(e => SameApp(e.Trigger, trigger));
            var index = i;

            var row = new Grid { Margin = new Thickness(8, 3, 8, 3), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            if (known?.Icon is { } icon)
                row.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });

            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var display = new TextBlock { Text = known?.Name ?? AppTriggerMatch.DisplayName(trigger), FontSize = 12, FontWeight = FontWeight.SemiBold };
            Res.Bind(display, TextBlock.ForegroundProperty, "PrimaryText");
            var detail = new TextBlock { Text = Detail(trigger), FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };
            Res.Bind(detail, TextBlock.ForegroundProperty, "SecondaryText");
            names.Children.Add(display);
            names.Children.Add(detail);
            Grid.SetColumn(names, 1);
            row.Children.Add(names);

            var remove = new Button
            {
                Content = "✕", FontSize = 11, Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2, 6, 2),
            };
            ToolTip.SetTip(remove, "Remove");
            remove.Theme = ActionTheme;
            remove.Click += (_, _) =>
            {
                _linked.RemoveAt(index);
                RebuildLinkedPanel();
                ApplyFilter();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            panel.Children.Add(row);

            if (i < _linked.Count - 1)
            {
                var separator = new Border { Height = 1, Margin = new Thickness(8, 3, 8, 3) };
                Res.Bind(separator, Border.BackgroundProperty, "CardSeparator");
                panel.Children.Add(separator);
            }
        }
    }

    internal IReadOnlyList<string> Linked => _linked;

    // ── Filter chips ───────────────────────────────────────────────────────────────────────────────

    private void FilterChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button chip) return;
        _activeFilter = (chip.Tag as string) switch
        {
            "Running" => AppFilter.Running,
            "Installed" => AppFilter.Installed,
            "InUse" => AppFilter.InUse,
            _ => AppFilter.All,
        };
        UpdateFilterChips();
        ApplyFilter();
    }

    private void UpdateFilterChips()
    {
        Named<Button>("ChipAll").Classes.Set("on", _activeFilter == AppFilter.All);
        Named<Button>("ChipRunning").Classes.Set("on", _activeFilter == AppFilter.Running);
        Named<Button>("ChipInstalled").Classes.Set("on", _activeFilter == AppFilter.Installed);
        Named<Button>("ChipInUse").Classes.Set("on", _activeFilter == AppFilter.InUse);
    }

    internal AppFilter ActiveFilter => _activeFilter;

    // ── App discovery ──────────────────────────────────────────────────────────────────────────────

    // Both lists in one go off the UI thread, installed first (the Linux catalog works out the running
    // apps from that list: it would scan every app again otherwise), then shown — unless the dialog
    // has closed meanwhile. A list that fails to load stays empty.
    private void StartLoading()
    {
        if (_loadStarted) return;
        _loadStarted = true;
        var ct = _loadCts.Token;
        _ = Task.Run(() =>
        {
            var installed = Read("installed", () => _catalog?.Installed(ct) ?? []);
            ct.ThrowIfCancellationRequested();
            var running = Read("running", () => _catalog?.Running() ?? []);
            return (Installed: installed, Running: running);
        }, ct).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested) return;
            IReadOnlyList<AppEntry> installed = t.IsCompletedSuccessfully ? t.Result.Installed : [];
            IReadOnlyList<AppEntry> running = t.IsCompletedSuccessfully ? t.Result.Running : [];
            _installedEntries = installed.Select(ToItem).ToList();
            _runningEntries = running.Select(ToItem).ToList();
            _listsLoaded = true;
            Named<TextBlock>("LoadingLabel").IsVisible = false;
            Refresh();
            LoadIcons([.. installed, .. running], ct);
        }), TaskScheduler.Default);
    }

    private static IReadOnlyList<AppEntry> Read(string which, Func<IReadOnlyList<AppEntry>> read)
    {
        try { return read(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Warning("AppTriggerDialog", $"Couldn't list the {which} apps: {ex.Message}");
            return [];
        }
    }

    // The icons, once the lists are showing: they're other apps' files, and a slow one mustn't hold
    // the lists back.
    private void LoadIcons(List<AppEntry> apps, CancellationToken ct)
    {
        _ = Task.Run(() =>
        {
            var icons = new Dictionary<string, IImage?>(StringComparer.Ordinal);
            foreach (var app in apps)
            {
                if (ct.IsCancellationRequested) break;
                if (!icons.ContainsKey(app.Trigger)) icons[app.Trigger] = IconFor(app);
            }
            return icons;
        }, ct).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested) return;
            if (t.IsCompletedSuccessfully && t.Result.Values.Any(icon => icon != null))
            {
                _installedEntries = _installedEntries.Select(e => e with { Icon = t.Result.GetValueOrDefault(e.Trigger) }).ToList();
                _runningEntries = _runningEntries.Select(e => e with { Icon = t.Result.GetValueOrDefault(e.Trigger) }).ToList();
                Refresh();
            }
            _iconsLoaded = true;
        }), TaskScheduler.Default);
    }

    private IImage? IconFor(AppEntry app)
    {
        try { return _loadIcon(app); }
        catch (Exception) { return null; }
    }

    private AppItem ToItem(AppEntry app) =>
        new(app.Trigger, app.DisplayName, Detail(app.Trigger), null, ConflictFor(app.Trigger), app.IsRunning);

    // The other profile that has this app (by the same test as "Added": the same app, not only the
    // same text — a Windows game linked from an imported config is the one running under Wine).
    private string? ConflictFor(string trigger) =>
        _usedByOthers.TryGetValue(trigger, out var profile) ? profile
        : _usedByOthers.FirstOrDefault(kv => SameApp(kv.Key, trigger)).Value;

    private void Refresh()
    {
        RebuildAllEntries();
        RebuildLinkedPanel();
        ApplyFilter();
    }

    // Running apps, then the installed ones that aren't running, by name (Windows: the same).
    private void RebuildAllEntries()
    {
        var installedOnly = _installedEntries.Where(e => !_runningEntries.Any(r => SameApp(r.Trigger, e.Trigger)));
        _allEntries = _runningEntries.Select(e => e with { IsRunning = true })
            .Concat(installedOnly.Select(e => e with { IsRunning = false }))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ── The list ───────────────────────────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var filter = Named<TextBox>("SearchBox").Text ?? "";
        var panel = Named<StackPanel>("RunningPanel");
        panel.Children.Clear();

        IEnumerable<AppItem> source = _activeFilter switch
        {
            AppFilter.Running => _allEntries.Where(e => e.IsRunning),
            AppFilter.Installed => _allEntries.Where(e => !e.IsRunning),
            AppFilter.InUse => _allEntries.Where(e => e.ConflictingProfile != null || IsLinkedHere(e.Trigger)),
            _ => _allEntries,
        };

        var matches = source
            .Where(e => string.IsNullOrWhiteSpace(filter)
                || e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            // (Nothing to say while the lists are still loading: the loading text says it.)
            if (!_listsLoaded) return;
            var message = (string.IsNullOrWhiteSpace(filter), _activeFilter) switch
            {
                (false, _) => "No matches found.",
                (true, AppFilter.Running) => "No running apps detected.",
                (true, AppFilter.Installed) => "No installed apps found.",
                (true, AppFilter.InUse) => "No apps are in use yet.",
                _ => "No apps detected.",
            };
            var empty = new TextBlock { Text = message, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
            Res.Bind(empty, TextBlock.ForegroundProperty, "SecondaryText");
            panel.Children.Add(empty);
            return;
        }

        foreach (var entry in matches)
        {
            var linkedHere = IsLinkedHere(entry.Trigger);
            panel.Children.Add(BuildRow(entry, linkedHere, linkedHere || entry.ConflictingProfile != null));
        }
    }

    private bool IsLinkedHere(string trigger) => _linked.Any(l => SameApp(l, trigger));

    private Control BuildRow(AppItem entry, bool isLinkedHere, bool isDisabled)
    {
        var row = new Grid { Margin = new Thickness(8, 4, 8, 4), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var dim = isDisabled ? 0.45 : 1.0;

        if (entry.Icon != null)
            row.Children.Add(new Image
            {
                Source = entry.Icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, Opacity = isDisabled ? 0.4 : 1.0,
            });

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var display = new TextBlock { Text = entry.Name, FontSize = 12, Opacity = dim };
        Res.Bind(display, TextBlock.ForegroundProperty, "PrimaryText");
        var detail = new TextBlock { Text = entry.Detail, FontSize = 10, Margin = new Thickness(0, 1, 0, 0), Opacity = dim };
        Res.Bind(detail, TextBlock.ForegroundProperty, "SecondaryText");
        names.Children.Add(display);
        names.Children.Add(detail);
        if (entry.IsRunning)
        {
            var running = new TextBlock { Text = "● Running", FontSize = 9, Margin = new Thickness(0, 1, 0, 0), Opacity = dim };
            Res.Bind(running, TextBlock.ForegroundProperty, "VSGreen");
            names.Children.Add(running);
        }
        Grid.SetColumn(names, 1);
        row.Children.Add(names);

        Control action;
        if (isLinkedHere) action = MakeBadge("Added", "SuccessBadgeText", "SuccessBadgeBg");
        else if (entry.ConflictingProfile != null) action = MakeBadge($"Used by \"{entry.ConflictingProfile}\"", "WarningBadgeText", "WarningBadgeBg");
        else
        {
            var add = new Button
            {
                Content = "+ Add", FontSize = 11, Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 3, 8, 3),
            };
            add.Theme = ActionTheme;
            add.Click += (_, _) =>
            {
                _linked.Add(entry.Trigger);
                RebuildLinkedPanel();
                ApplyFilter();
            };
            action = add;
        }
        Grid.SetColumn(action, 2);
        row.Children.Add(action);

        // (WPF wraps an addable row in a border with a little padding.)
        return isDisabled ? row : new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 1, 0, 1), Child = row };
    }

    private static Border MakeBadge(string text, string foregroundKey, string backgroundKey)
    {
        var label = new TextBlock { Text = text, FontSize = 10 };
        Res.Bind(label, TextBlock.ForegroundProperty, foregroundKey);
        var badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, Child = label };
        Res.Bind(badge, Border.BackgroundProperty, backgroundKey);
        return badge;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    // Two triggers for the same app: equal (Mac bundles ignoring case, as macOS does), or naming the
    // same program — the Windows dialog's test, and what matching a running process falls back to.
    internal static bool SameApp(string a, string b)
    {
        if (string.Equals(a, b, AppTriggerMatch.IsBundle(a) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
        var name = AppTriggerMatch.TriggerName(a);
        return name.Length > 0 && string.Equals(name, AppTriggerMatch.TriggerName(b), StringComparison.OrdinalIgnoreCase);
    }

    // A trigger's second line: the program or app's file name (as Windows shows "app.exe"), or the
    // Flatpak / Snap id.
    internal static string Detail(string trigger)
    {
        if (trigger.StartsWith(AppTriggerMatch.FlatpakPrefix, StringComparison.Ordinal))
            return $"{trigger[AppTriggerMatch.FlatpakPrefix.Length..]} (Flatpak)";
        if (trigger.StartsWith(AppTriggerMatch.SnapPrefix, StringComparison.Ordinal))
            return $"{trigger[AppTriggerMatch.SnapPrefix.Length..]} (Snap)";
        var path = trigger.TrimEnd('/', '\\');
        return path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
    }

    // ── Event handlers ─────────────────────────────────────────────────────────────────────────────

    private void SearchBox_FocusChanged(object? sender, RoutedEventArgs e) => UpdatePlaceholder();

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        ApplyFilter();
    }

    private void UpdatePlaceholder()
    {
        var box = Named<TextBox>("SearchBox");
        Named<TextBlock>("SearchPlaceholder").IsVisible = !box.IsFocused && string.IsNullOrEmpty(box.Text);
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        // (Linux pickers don't block the dialog: no second picker meanwhile, and nothing more to do
        // if the dialog was closed while the picker was up.)
        var browse = Named<Button>("BrowseButton");
        browse.IsEnabled = false;
        try
        {
            var path = await PickFile();
            if (path == null || !IsVisible) return;
            var trigger = _catalog != null ? _catalog.TriggerForFile(path) : File.Exists(path) ? path : null;
            var fileName = Detail(path);
            if (trigger == null)
            {
                await ShowAlert("Not an App", $"{fileName} isn't an app or a program, so it can't switch this profile.");
                return;
            }
            if (_linked.Any(l => SameApp(l, trigger)))
            {
                await ShowAlert("Already Added", $"{Detail(trigger)} is already linked to this profile.");
                return;
            }
            if (ConflictFor(trigger) is { } otherProfile)
            {
                await ShowAlert("Already Linked", $"{Detail(trigger)} is already linked to \"{otherProfile}\". Remove it from that profile first.");
                return;
            }
            _linked.Add(trigger);
            RebuildLinkedPanel();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            AppLog.Warning("AppTriggerDialog.Browse", ex.Message);
        }
        finally
        {
            browse.IsEnabled = true;
        }
    }

    // The system's file picker: an app on macOS (Windows: an .exe), any file on Linux — a program, an
    // AppImage or an app-menu entry.
    private async Task<string?> PickFileAsync()
    {
        var options = new FilePickerOpenOptions { Title = Wording.BrowseAppTitle, AllowMultiple = false };
        if (Wording.Mac)
            options.FileTypeFilter =
            [
                new FilePickerFileType("Applications")
                {
                    Patterns = ["*.app"],
                    AppleUniformTypeIdentifiers = ["com.apple.application-bundle"],
                },
            ];
        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Count == 1 ? DialogService.LocalPath(files[0]) : null;
    }

    private void Done_Click(object? sender, RoutedEventArgs e)
    {
        ResultTriggers = new List<string>(_linked);
        CloseWith(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
