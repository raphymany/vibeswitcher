using System.Windows;
using Microsoft.Win32;

namespace VibeSwitcher.Services;

public class ThemeService
{
    private const string RegKey   = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegValue = "AppsUseLightTheme";

    private const string LightUri = "pack://application:,,,/Themes/LightTheme.xaml";
    private const string DarkUri  = "pack://application:,,,/Themes/DarkTheme.xaml";

    private readonly IConfigService _configService;

    public event Action? ThemeApplied;

    public ThemeService(IConfigService configService)
    {
        _configService = configService;
    }

    public void Apply() => Apply(_configService.Current.Theme ?? "Auto");

    public void Apply(string mode)
    {
        bool isDark = mode switch
        {
            "Dark"  => true,
            "Light" => false,
            _       => IsOsDark()
        };
        SwapDictionary(isDark ? DarkUri : LightUri);
        ThemeApplied?.Invoke();
    }

    public void StartListening() =>
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

    public void StopListening() =>
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if ((_configService.Current.Theme ?? "Auto") != "Auto") return;
        Application.Current?.Dispatcher.InvokeAsync(Apply);
    }

    private static bool IsOsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey);
            return key?.GetValue(RegValue) is int v && v == 0;
        }
        catch { return false; }
    }

    private static void SwapDictionary(string uri)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d =>
            d.Source != null &&
            (d.Source.OriginalString.Contains("LightTheme") ||
             d.Source.OriginalString.Contains("DarkTheme")));

        if (existing != null && existing.Source.OriginalString == uri) return;

        var next = new ResourceDictionary { Source = new Uri(uri) };
        int idx = existing != null ? dicts.IndexOf(existing) : dicts.Count;
        if (existing != null) dicts.RemoveAt(idx);
        dicts.Insert(idx, next);
    }
}
