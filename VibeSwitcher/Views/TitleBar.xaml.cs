using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VibeSwitcher.Views;

public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty ShowMinimizeProperty =
        DependencyProperty.Register(nameof(ShowMinimize), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(false, (d, e) =>
            {
                if (d is TitleBar tb)
                    tb.MinBtn.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }));

    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(false, (d, e) =>
            {
                if (d is TitleBar tb)
                    tb.MaxBtn.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }));

    public static readonly DependencyProperty ShowTitleProperty =
        DependencyProperty.Register(nameof(ShowTitle), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, (d, e) =>
            {
                if (d is TitleBar tb)
                    tb.TitleLabel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }));

    public static readonly DependencyProperty IconSourceProperty =
        DependencyProperty.Register(nameof(IconSource), typeof(ImageSource), typeof(TitleBar),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is TitleBar tb)
                {
                    tb.IconImage.Source = e.NewValue as ImageSource;
                    tb.IconImage.Visibility = e.NewValue != null ? Visibility.Visible : Visibility.Collapsed;
                }
            }));

    public bool ShowMinimize
    {
        get => (bool)GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    private EventHandler? _stateChangedHandler;

    public TitleBar()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window == null) return;
        _stateChangedHandler = (_, _) => UpdateMaxBtn(window);
        window.StateChanged += _stateChangedHandler;
        UpdateMaxBtn(window);

        if (IconSource == null)
            try { IconSource = Helpers.IconHelper.GetAppIconImageSource(); } catch { }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_stateChangedHandler == null) return;
        var window = Window.GetWindow(this);
        if (window != null) window.StateChanged -= _stateChangedHandler;
        _stateChangedHandler = null;
    }

    private void UpdateMaxBtn(Window window) { }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        Window.GetWindow(this)?.Close();

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w != null) w.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w == null) return;
        w.WindowState = w.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
