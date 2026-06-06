using System.Windows;
using System.Windows.Input;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Views;

public partial class HotkeyCaptureDialog : Window
{
    private HotkeyDefinition _captured;

    public HotkeyDefinition? CapturedHotkey { get; private set; }

    public HotkeyCaptureDialog(HotkeyDefinition current, string? subtitle = null)
    {
        InitializeComponent();
        _captured = CloneHotkey(current);
        HotkeyPreviewText.Text = _captured.ToDisplayString();
        if (subtitle != null) HotkeySubtitleText.Text = subtitle;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Focusable = true;
        Focus();
        try { HotkeyAppIcon.Source = IconHelper.GetAppIconImageSource(); } catch { }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Navigation/system keys that must not be assignable as hotkeys
        if (key is Key.Enter or Key.Escape or Key.Tab
                or Key.Apps or Key.Pause or Key.PrintScreen or Key.Scroll) return;

        if (IsModifierKey(key))
        {
            UpdateModifierPreview();
            return;
        }

        e.Handled = true;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var modifiers = Keyboard.Modifiers;
        _captured = new HotkeyDefinition
        {
            VirtualKeyCode = vk,
            UseAlt   = modifiers.HasFlag(ModifierKeys.Alt),
            UseCtrl  = modifiers.HasFlag(ModifierKeys.Control),
            UseShift = modifiers.HasFlag(ModifierKeys.Shift),
            UseWin   = modifiers.HasFlag(ModifierKeys.Windows),
        };

        HotkeyPreviewText.Text = _captured.ToDisplayString();
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        if (IsModifierKey(e.Key == Key.System ? e.SystemKey : e.Key))
            UpdateModifierPreview();
    }

    private void UpdateModifierPreview()
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            HotkeyPreviewText.Text = _captured.ToDisplayString();
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        HotkeyPreviewText.Text = string.Join("+", parts) + "+";
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        CapturedHotkey = _captured;
        DialogResult = true;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _captured = new HotkeyDefinition();
        HotkeyPreviewText.Text = _captured.ToDisplayString();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static HotkeyDefinition CloneHotkey(HotkeyDefinition src) => new()
    {
        VirtualKeyCode = src.VirtualKeyCode,
        UseAlt   = src.UseAlt,
        UseCtrl  = src.UseCtrl,
        UseShift = src.UseShift,
        UseWin   = src.UseWin,
    };
}
