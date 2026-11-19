using System;
using System.Windows;
using System.Windows.Media;

namespace MacDock;

// The unelapsed sector is clipped to the tile, including wide and square blocks.
internal sealed class CooldownOverlay : FrameworkElement
{
    static readonly Brush Shade = DockWindow.Brush("#92101925");
    static readonly Pen SweepEdge = new(DockWindow.Brush("#AAA4F7EA"), 1.5);
    double remaining;
    internal double Remaining => remaining;

    internal void SetRemaining(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (remaining == fraction) return;
        remaining = fraction;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (remaining <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var bounds = new Rect(RenderSize);
        var clip = new RectangleGeometry(bounds, 13, 13);
        if (remaining >= 1) { dc.DrawGeometry(Shade, null, clip); return; }

        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        double radius = Math.Sqrt(center.X * center.X + center.Y * center.Y) + 1;
        double angle = (1 - remaining) * 2 * Math.PI;
        var tip = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var sector = new StreamGeometry();
        using (var path = sector.Open()) {
            path.BeginFigure(center, true, true);
            path.LineTo(tip, true, false);
            path.ArcTo(new Point(center.X, center.Y - radius), new Size(radius, radius),
                0, remaining > .5, SweepDirection.Clockwise, true, false);
        }
        dc.PushClip(clip);
        dc.DrawGeometry(Shade, null, sector);
        dc.DrawLine(SweepEdge, center, tip);
        dc.Pop();
    }
}
