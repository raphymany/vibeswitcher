namespace VibeSwitcher.Controls;

// Fork of the Windows LogoAnimator's mode switch (Settings → Appearance → Logo animation), which the
// linked SettingsViewModel sets. The animated logo controls in the app follow Mode / ModeChanged:
//   Full    — default 60fps
//   Reduced — capped to ~24fps (roughly half the CPU)
//   Static  — no animation at all
internal static class LogoAnimator
{
    public enum AnimMode { Full, Reduced, Static }

    public const int ReducedFps = 24;

    private static AnimMode _mode = AnimMode.Full;
    public static event Action? ModeChanged;

    public static AnimMode Mode
    {
        get => _mode;
        set { if (_mode == value) return; _mode = value; ModeChanged?.Invoke(); }
    }

    // Maps the persisted config string to a mode (unknown -> Full).
    public static AnimMode Parse(string? value) => value switch
    {
        "Reduced" => AnimMode.Reduced,
        "Static"  => AnimMode.Static,
        _         => AnimMode.Full,
    };
}
