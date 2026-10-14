using Avalonia;
using Avalonia.Controls;

namespace VibeSwitcher.App.Controls;

// Port of Views/CenteredWrapPanel.cs: lays children out left to right, wrapping to a new row when
// one is full, with every row centred (the profile cards, the filter chips).
public class CenteredWrapPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double lineW = 0, lineH = 0, totalH = 0, maxW = 0;

        foreach (var child in Children)
        {
            child.Measure(available);
            var cw = child.DesiredSize.Width;
            var ch = child.DesiredSize.Height;

            if (lineW + cw > available.Width && lineW > 0)
            {
                totalH += lineH;
                maxW = Math.Max(maxW, lineW);
                lineW = 0; lineH = 0;
            }
            lineW += cw;
            lineH = Math.Max(lineH, ch);
        }

        totalH += lineH;
        maxW = Math.Max(maxW, lineW);
        return new Size(maxW, totalH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var maxW = double.IsPositiveInfinity(final.Width) ? double.MaxValue : final.Width;

        // Build rows
        var rows = new List<(List<Control> Items, double H)>();
        var line = new List<Control>();
        double lineW = 0, lineH = 0;

        foreach (var child in Children)
        {
            var cw = child.DesiredSize.Width;
            var ch = child.DesiredSize.Height;

            if (lineW + cw > maxW && line.Count > 0)
            {
                rows.Add((line, lineH));
                line = []; lineW = 0; lineH = 0;
            }
            line.Add(child);
            lineW += cw;
            lineH = Math.Max(lineH, ch);
        }
        if (line.Count > 0) rows.Add((line, lineH));

        double y = 0;
        foreach (var (items, h) in rows)
        {
            var rowW = items.Sum(c => c.DesiredSize.Width);
            var x = Math.Max(0, (final.Width - rowW) / 2.0);
            foreach (var child in items)
            {
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, h));
                x += child.DesiredSize.Width;
            }
            y += h;
        }

        return final;
    }
}
