using Avalonia;
using Avalonia.Styling;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Theming;

// FORK of VibeSwitcher/Services/ThemeService.cs (tracked in xplat/forks.txt). Same settings and API:
// AppConfig.Theme "Auto" (follow the system), "Light" or "Dark"; Apply() at start-up and when the
// setting changes; ThemeApplied when the look changed (the tray menu rebuilds on it). What changed:
// instead of reading the Windows registry and swapping resource dictionaries, the app's theme
// variant is set — both Windows theme dictionaries are loaded (Theming/WpfResources.cs) and Avalonia
// picks the matching one, following the system's light/dark setting live on macOS and on Linux
// desktops that publish it.
public class ThemeService
{
    private readonly IConfigService _configService;
    private Application? _app;

    public event Action? ThemeApplied;

    public ThemeService(IConfigService configService)
    {
        _configService = configService;
    }

    public void Apply() => Apply(_configService.Current.Theme ?? "Auto");

    public void Apply(string mode)
    {
        if (Application.Current is not { } app) return;
        _applying = true; // (the look changing here is this call's doing, not the system's)
        try
        {
            app.RequestedThemeVariant = mode switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default, // the system's setting
            };
        }
        finally { _applying = false; }
        ThemeApplied?.Invoke();
    }

    private bool _applying;

    public void StartListening()
    {
        _app = Application.Current;
        if (_app != null) _app.ActualThemeVariantChanged += OnSystemThemeChanged;
    }

    public void StopListening()
    {
        if (_app != null) _app.ActualThemeVariantChanged -= OnSystemThemeChanged;
        _app = null;
    }

    // The look changed by itself only when following the system.
    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (_applying || (_configService.Current.Theme ?? "Auto") != "Auto") return;
        ThemeApplied?.Invoke();
    }
}
