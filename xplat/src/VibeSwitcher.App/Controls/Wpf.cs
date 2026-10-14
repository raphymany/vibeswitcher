using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        RereadIsCheckedProperty.Changed.AddClassHandler<ToggleButton>((toggle, e) =>
        {
            // On Click: a toggle raises it after the change has gone through — including the view
            // model's setter and any question it asked — so the re-read sees the final answer.
            toggle.RemoveHandler(Button.ClickEvent, OnToggled);
            if (e.GetNewValue<bool>()) toggle.AddHandler(Button.ClickEvent, OnToggled, RoutingStrategies.Bubble, handledEventsToo: true);
        });

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

    // WPF reads a two-way bound value back after writing it, so the shared view models can refuse a
    // change (e.g. auto-switch when its confirmation is declined) by raising PropertyChanged inside
    // their setter. Avalonia ignores that echo; c:Wpf.RereadIsChecked="True" on a toggle reads the
    // view model's value again right after each click, so a refused change flips the toggle back.
    public static readonly AttachedProperty<bool> RereadIsCheckedProperty =
        AvaloniaProperty.RegisterAttached<ToggleButton, ToggleButton, bool>("RereadIsChecked");

    public static bool GetRereadIsChecked(ToggleButton toggle) => toggle.GetValue(RereadIsCheckedProperty);
    public static void SetRereadIsChecked(ToggleButton toggle, bool value) => toggle.SetValue(RereadIsCheckedProperty, value);

    private static bool _rereading;

    private static void OnToggled(object? sender, RoutedEventArgs e)
    {
        if (_rereading || sender is not ToggleButton toggle || !ReferenceEquals(e.Source, toggle)) return;
        Dispatcher.UIThread.Post(() =>
        {
            // Briefly unhooking the toggle from its view model and hooking it back makes every
            // binding on it read the view model afresh.
            var context = toggle.DataContext;
            if (context == null) return;
            _rereading = true;
            try
            {
                toggle.DataContext = null;
                toggle.DataContext = context;
                toggle.ClearValue(StyledElement.DataContextProperty); // back to the inherited one (the same)
            }
            finally { _rereading = false; }
        });
    }
}
