using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Input;
using VibeSwitcher.Models;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/HotkeyCaptureDialog.xaml.cs: shows the key combination as it's pressed; OK keeps
// it, Clear empties it, Cancel (or Escape) changes nothing. Keys are stored as Windows codes, so a
// config moves between systems; the label uses this system's names (⌃⌥⇧⌘ on macOS, Super on Linux).
public partial class HotkeyCaptureDialog : VsDialog
{
    private HotkeyDefinition _captured;

    // The modifier keys held right now. Tracked from their own presses and releases: X11 reports a
    // modifier's own press with the state from before it.
    private readonly HashSet<Key> _held = [];

    public HotkeyDefinition? CapturedHotkey { get; private set; }

    public HotkeyCaptureDialog() : this(new HotkeyDefinition()) { } // (for the XAML loader)

    public HotkeyCaptureDialog(HotkeyDefinition current, string? subtitle = null)
    {
        AvaloniaXamlLoader.Load(this);
        _captured = CloneHotkey(current);
        Preview.Text = _captured.ToDisplayString();
        if (subtitle != null) this.FindControl<TextBlock>("HotkeySubtitleText")!.Text = subtitle;

        // Before any button sees the key (Space would press a focused one): as WPF, where the window
        // itself has the keyboard.
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, Window_KeyUp, RoutingStrategies.Tunnel);
        Opened += (_, _) => Focus();
        Deactivated += (_, _) =>
        {
            _held.Clear(); // (releases can happen in another window)
            UpdateModifierPreview();
        };
    }

    private TextBlock Preview => this.FindControl<TextBlock>("HotkeyPreviewText")!;

    internal void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // Navigation/system keys that must not be assignable as hotkeys (Enter and Escape still
        // reach OK and Cancel).
        if (e.Key is Key.Enter or Key.Escape or Key.Tab or Key.Apps or Key.Pause or Key.PrintScreen or Key.Scroll) return;
        // (Keys an input method or a dead key is still composing aren't keys of their own.)
        if (e.Key is Key.ImeProcessed or Key.DeadCharProcessed) return;

        if (IsModifierKey(e.Key))
        {
            _held.Add(e.Key);
            UpdateModifierPreview();
            return;
        }

        e.Handled = true;

        // The layout's key; for a letter a non-Latin layout has no Latin key for (Cyrillic, Greek…),
        // the key at the same place on a US keyboard — the code Windows gives.
        int vk = VirtualKeys.For(e.Key);
        if (vk == 0) vk = VirtualKeys.For(e.PhysicalKey.ToQwertyKey());
        if (vk == 0) return;

        var modifiers = e.KeyModifiers;
        _captured = new HotkeyDefinition
        {
            VirtualKeyCode = vk,
            UseAlt = modifiers.HasFlag(KeyModifiers.Alt),
            UseCtrl = modifiers.HasFlag(KeyModifiers.Control),
            UseShift = modifiers.HasFlag(KeyModifiers.Shift),
            UseWin = modifiers.HasFlag(KeyModifiers.Meta), // (Command on macOS, Super on Linux)
        };

        Preview.Text = _captured.ToDisplayString();
    }

    internal void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        if (!IsModifierKey(e.Key)) return;
        _held.Remove(e.Key);
        UpdateModifierPreview();
    }

    private void UpdateModifierPreview()
    {
        bool ctrl = _held.Overlaps([Key.LeftCtrl, Key.RightCtrl]);
        bool alt = _held.Overlaps([Key.LeftAlt, Key.RightAlt]);
        bool shift = _held.Overlaps([Key.LeftShift, Key.RightShift]);
        bool win = _held.Overlaps([Key.LWin, Key.RWin]);
        Preview.Text = ctrl || alt || shift || win
            ? HotkeyDefinition.ModifierPreview(ctrl, alt, shift, win)
            : _captured.ToDisplayString();
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        CapturedHotkey = _captured;
        CloseWith(true);
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        _captured = new HotkeyDefinition();
        Preview.Text = _captured.ToDisplayString();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => CloseWith(false);

    private static HotkeyDefinition CloneHotkey(HotkeyDefinition src) => new()
    {
        VirtualKeyCode = src.VirtualKeyCode,
        UseAlt = src.UseAlt,
        UseCtrl = src.UseCtrl,
        UseShift = src.UseShift,
        UseWin = src.UseWin,
    };
}
