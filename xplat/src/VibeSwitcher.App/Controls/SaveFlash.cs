using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;

namespace VibeSwitcher.App.Controls;

// The profile card's save flash (the Windows card template's DataTrigger on SaveFlash): when the
// view model's SaveFlash turns on, the overlay fades up to 0.7 in 0.15 s; when it turns off, it
// fades back out in 0.28 s. c:SaveFlash.IsOn="{Binding SaveFlash}" on the overlay.
public static class SaveFlash
{
    public static readonly AttachedProperty<bool> IsOnProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("IsOn");

    public static bool GetIsOn(Control c) => c.GetValue(IsOnProperty);
    public static void SetIsOn(Control c, bool value) => c.SetValue(IsOnProperty, value);

    private static readonly AttachedProperty<CancellationTokenSource?> RunningProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, CancellationTokenSource?>("Running");

    static SaveFlash()
    {
        IsOnProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            bool on = e.GetNewValue<bool>();
            c.GetValue(RunningProperty)?.Cancel();
            var cts = new CancellationTokenSource();
            c.SetValue(RunningProperty, cts);
            double from = c.Opacity, to = on ? 0.7 : 0;
            c.Opacity = to; // where it rests; the fade runs over it
            _ = new Animation
            {
                Duration = TimeSpan.FromSeconds(on ? 0.15 : 0.28),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, from) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, to) } },
                },
            }.RunAsync(c, cts.Token);
        });
    }
}
