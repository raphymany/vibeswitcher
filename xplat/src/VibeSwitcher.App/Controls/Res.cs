using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace VibeSwitcher.App.Controls;

// WPF's SetResourceReference / FindResource for code-behind.
internal static class Res
{
    // Follows the theme (like {DynamicResource key}).
    public static void Bind(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target[!property] = new DynamicResourceExtension(key);

    public static Geometry Icon(Control anchor, string key) =>
        anchor.TryFindResource(key, out var value) && value is Geometry g
            ? g
            : throw new KeyNotFoundException($"Icon '{key}' isn't defined.");
}
