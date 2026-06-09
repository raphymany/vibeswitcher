using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VibeSwitcher.Views;

public partial class ProfileCardView : UserControl
{
    public static readonly RoutedEvent CardExpandedEvent =
        EventManager.RegisterRoutedEvent(
            "CardExpanded", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ProfileCardView));

    public event RoutedEventHandler CardExpanded
    {
        add => AddHandler(CardExpandedEvent, value);
        remove => RemoveHandler(CardExpandedEvent, value);
    }

    public ProfileCardView()
    {
        InitializeComponent();
        MouseEnter += (_, _) => AnimateActions(1);
        MouseLeave += (_, _) => AnimateActions(0);
    }

    private void AnimateActions(double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 150 : 200));
        anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        ActionStrip.BeginAnimation(OpacityProperty, anim);
        ActionStrip.IsHitTestVisible = to > 0;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        // Don't expand if the click was on an action button
        if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is Button)
            return;
        if (e.Source is Button)
            return;

        // Press animation on CardBorder
        var kf = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(220) };
        kf.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(0)));
        kf.KeyFrames.Add(new EasingDoubleKeyFrame(0.93, KeyTime.FromPercent(0.4))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        kf.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(1.0))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        CardBorder.RenderTransform = new ScaleTransform(1, 1);
        ((ScaleTransform)CardBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, kf);
        ((ScaleTransform)CardBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, kf);

        Dispatcher.BeginInvoke(
            () => RaiseEvent(new RoutedEventArgs(CardExpandedEvent, this)),
            System.Windows.Threading.DispatcherPriority.Background);
    }
}
