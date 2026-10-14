using Avalonia.Data.Converters;
using WpfVisibility = System.Windows.Visibility;

namespace VibeSwitcher.App.Controls;

// The few value tests the Windows XAML does with DataTriggers.
public static class Converters
{
    // A hotkey text other than "Not set" (the view models' text for no hotkey).
    public static readonly IValueConverter IsNotNotSet =
        new FuncValueConverter<string?, bool>(s => s != "Not set");

    // A view model's WPF visibility is Collapsed.
    public static readonly IValueConverter IsCollapsed =
        new FuncValueConverter<WpfVisibility, bool>(v => v == WpfVisibility.Collapsed);
}
