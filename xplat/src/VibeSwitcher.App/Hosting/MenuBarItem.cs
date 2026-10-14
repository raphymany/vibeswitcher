using VibeSwitcher.Platform.MacOS.OS;

namespace VibeSwitcher.App.Hosting;

// The macOS menu-bar item the app draws its styled menu from (tests stand in for it).
internal interface IMenuBarItem : IDisposable
{
    // A press on the item (UI thread).
    event Action? Clicked;
    void SetImage(byte[] png);
    void SetToolTip(string text);
    void SetVisible(bool visible);
    // Where the menu opens: under the item, in points from the main display's top-left; null when
    // unknown.
    (double X, double Y)? MenuAnchor();
    void PerformClick();
}

internal sealed class MacMenuBarItem(MacStatusItem item) : IMenuBarItem
{
    public event Action? Clicked
    {
        add => item.Clicked += value;
        remove => item.Clicked -= value;
    }

    public void SetImage(byte[] png) => item.SetImage(png);
    public void SetToolTip(string text) => item.SetToolTip(text);
    public void SetVisible(bool visible) => item.SetVisible(visible);
    // (Under the item; else where the pointer is — it just clicked the item.)
    public (double X, double Y)? MenuAnchor() => item.BelowItem() ?? MacPointer.Location();
    public void PerformClick() => item.PerformClick();
    public void Dispose() => item.Dispose();
}
