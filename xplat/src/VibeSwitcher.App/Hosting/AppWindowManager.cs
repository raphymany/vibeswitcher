using Avalonia.Controls;
using VibeSwitcher.App.Settings;

namespace VibeSwitcher.App.Hosting;

// Fork of AppWindowManager.cs: the one Settings window (created on first use; closing it to the tray
// only hides it), opened, toggled, sent to a panel or into Mini Mode. Avalonia has no list of the
// app's windows to search, so the window is kept here.
internal sealed class AppWindowManager(Func<SettingsWindow> create)
{
    private SettingsWindow? _window;

    internal SettingsWindow? Window => _window;

    private SettingsWindow GetOrCreate()
    {
        if (_window != null) return _window;
        var window = create();
        window.Closed += (_, _) => { if (_window == window) _window = null; };
        return _window = window;
    }

    public void OpenSettingsWindow(bool expandSettings = false)
    {
        var existing = _window;
        if (existing != null)
        {
            // (Windows hides an active window here even for the menu's "Settings"; a native menu here
            // can leave the window active, so that one always shows Settings. A maximized window stays
            // maximized — Windows resets it to normal.)
            if (existing.IsVisible && existing.IsActive && !expandSettings)
            {
                existing.Hide();
                return;
            }
            Bring(existing);
            if (expandSettings) existing.ExpandSettings();
            return;
        }

        var window = GetOrCreate();
        window.Show();
        if (expandSettings) window.ExpandSettings();
    }

    // Settings in front, never hidden (macOS "reopen": the app was opened again).
    public void ShowSettingsWindow() => Bring(GetOrCreate());

    public void NotifyProfileSwitched() => _window?.RefreshActiveStates();

    // A profile was re-bound to a device's new ID — reload the cards' device lists.
    public void NotifyDevicesRebound() => _window?.RefreshDevices();

    public void OpenAbout() => OpenToPanel(w => w.OpenAboutPanel());
    public void OpenFaq() => OpenToPanel(w => w.OpenFaqPanel());

    // Hotkey / menu path: flips an open window between full and mini; opens straight into mini when hidden.
    public void ToggleCompactMode()
    {
        var window = _window;
        if (window != null && window.IsVisible)
        {
            Bring(window);
            window.ToggleCompact();
            return;
        }
        window = GetOrCreate();
        Bring(window);
        if (!window.IsCompact) window.EnterCompact();
    }

    public bool IsMiniModeActive() => _window is { IsVisible: true, IsCompact: true };

    private void OpenToPanel(Action<SettingsWindow> navigate)
    {
        var window = GetOrCreate();
        Bring(window);
        navigate(window);
    }

    private static void Bring(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
