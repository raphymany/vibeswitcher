using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using WpfVisibility = System.Windows.Visibility;

namespace VibeSwitcher.App.Controls;

// Lets a view use the shared view models' WPF-style Visibility properties directly:
// c:Wpf.Visibility="{Binding PlaybackVisible}". Collapsed removes the control from the layout;
// Hidden keeps its space but draws nothing, and nothing inside it can be clicked or focused — as in
// WPF. Applied over the control's own values (which come back unchanged when it's shown again).
public static class Wpf
{
    public static readonly AttachedProperty<WpfVisibility> VisibilityProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, WpfVisibility>("Visibility");

    // The overrides currently applied to each control, undone when its visibility changes again.
    private static readonly ConditionalWeakTable<Control, List<IDisposable>> Applied = new();

    static Wpf()
    {
        VisibilityProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            var overrides = Applied.GetOrCreateValue(control);
            foreach (var o in overrides) o.Dispose();
            overrides.Clear();

            void Set<T>(StyledProperty<T> property, T value)
            {
                if (control.SetValue(property, value, BindingPriority.Animation) is { } undo) overrides.Add(undo);
            }

            switch (e.GetNewValue<WpfVisibility>())
            {
                case WpfVisibility.Collapsed:
                    Set(Visual.IsVisibleProperty, false);
                    break;
                case WpfVisibility.Hidden:
                    Set(Visual.OpacityProperty, 0.0);
                    Set(InputElement.IsHitTestVisibleProperty, false);
                    Set(InputElement.IsEnabledProperty, false); // (so nothing inside can take focus)
                    break;
            }
        });
    }

    public static WpfVisibility GetVisibility(Control control) => control.GetValue(VisibilityProperty);
    public static void SetVisibility(Control control, WpfVisibility value) => control.SetValue(VisibilityProperty, value);
}
