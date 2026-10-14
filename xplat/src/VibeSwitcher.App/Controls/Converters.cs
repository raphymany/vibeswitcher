using System.Globalization;
using Avalonia.Data;
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

    // A radio button of a string choice (theme, logo animation): checked while the value is the
    // ConverterParameter; checking it sets the value to the parameter (unchecking changes nothing —
    // the radio that was checked instead sets it).
    public static readonly IValueConverter IsValue = new IsValueConverter();

    private sealed class IsValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Equals(value as string, parameter as string);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? parameter : BindingOperations.DoNothing;
    }
}
