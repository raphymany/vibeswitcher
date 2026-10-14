using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using Path = Avalonia.Controls.Shapes.Path;

namespace VibeSwitcher.App.Controls;

// Port of Views/TitleBar.xaml.cs: ShowMinimize / ShowMaximize (off by default), ShowTitle (on), the
// title taken from the window, and the maximize symbol switching to "restore" while maximized. Also
// what the system frame would otherwise do: dragging the window by its title bar (Linux), and
// making room for the macOS buttons.
public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<bool> ShowMinimizeProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMinimize));
    public static readonly StyledProperty<bool> ShowMaximizeProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMaximize));
    public static readonly StyledProperty<bool> ShowTitleProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowTitle), true);

    public bool ShowMinimize { get => GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }
    public bool ShowMaximize { get => GetValue(ShowMaximizeProperty); set => SetValue(ShowMaximizeProperty, value); }
    public bool ShowTitle { get => GetValue(ShowTitleProperty); set => SetValue(ShowTitleProperty, value); }

    // Two overlapping squares: the "restore" symbol.
    private static readonly Geometry RestoreGlyph = StreamGeometry.Parse("M2.5,2.5 H9.5 V9.5 H2.5 Z M2.5,2.5 V0.5 H11.5 V7.5 H9.5");
    private static readonly Geometry MaximizeGlyph = StreamGeometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");
    private Window? _window;
    private IDisposable? _subscriptions;

    public TitleBar()
    {
        AvaloniaXamlLoader.Load(this);
        bool mac = WindowChrome.Style == ChromeStyle.Mac;
        this.FindControl<StackPanel>("Buttons")!.IsVisible = !mac;
        if (mac) this.FindControl<StackPanel>("TitlePanel")!.Margin = new Thickness(WindowChrome.MacButtonsWidth, 0, 0, 0);
        this.FindControl<Grid>("Bar")!.PointerPressed += Bar_PointerPressed;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowMinimizeProperty) this.FindControl<Button>("MinBtn")!.IsVisible = ShowMinimize;
        else if (change.Property == ShowMaximizeProperty) this.FindControl<Button>("MaxBtn")!.IsVisible = ShowMaximize;
        else if (change.Property == ShowTitleProperty) this.FindControl<TextBlock>("TitleLabel")!.IsVisible = ShowTitle;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window == null) return;
        var title = this.FindControl<TextBlock>("TitleLabel")!;
        var glyph = this.FindControl<Path>("MaxGlyph")!;
        _subscriptions = new CompositeSubscription(
            _window.GetObservable(Window.TitleProperty).Subscribe(new Observer<string?>(t => title.Text = t)),
            _window.GetObservable(Window.WindowStateProperty).Subscribe(new Observer<WindowState>(s =>
                glyph.Data = s == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph)));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _subscriptions?.Dispose();
        _subscriptions = null;
        _window = null;
    }

    // The title bar is where the window is dragged from: without a system frame (Linux) nothing else
    // would move it, and on macOS the system only drags from a title bar with nothing drawn on it,
    // which this one has. A double click maximizes when the window can be (macOS handles its own).
    private void Bar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            if (ShowMaximize && WindowChrome.Style != ChromeStyle.Mac) ToggleMaximize();
            return;
        }
        _window.BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => _window?.Close();

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (_window != null) _window.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        if (_window == null) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private sealed class CompositeSubscription(params IDisposable[] parts) : IDisposable
    {
        public void Dispose() { foreach (var p in parts) p.Dispose(); }
    }
}
