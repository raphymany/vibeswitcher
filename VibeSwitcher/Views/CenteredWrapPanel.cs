namespace VibeSwitcher.Views;

public class CenteredWrapPanel : System.Windows.Controls.Panel
{
    protected override System.Windows.Size MeasureOverride(System.Windows.Size available)
    {
        var lineW = 0.0; var lineH = 0.0;
        var totalH = 0.0; var maxW = 0.0;

        foreach (System.Windows.UIElement child in InternalChildren)
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
        return new System.Windows.Size(maxW, totalH);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size final)
    {
        var maxW = double.IsPositiveInfinity(final.Width) ? double.MaxValue : final.Width;

        // Build rows
        var rows   = new List<(List<System.Windows.UIElement> Items, double H)>();
        var line   = new List<System.Windows.UIElement>();
        var lineW  = 0.0; var lineH = 0.0;

        foreach (System.Windows.UIElement child in InternalChildren)
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

        var y = 0.0;
        foreach (var (items, h) in rows)
        {
            var rowW = items.Sum(c => c.DesiredSize.Width);
            var x    = Math.Max(0, (final.Width - rowW) / 2.0);
            foreach (var child in items)
            {
                child.Arrange(new System.Windows.Rect(x, y, child.DesiredSize.Width, h));
                x += child.DesiredSize.Width;
            }
            y += h;
        }

        return final;
    }
}
