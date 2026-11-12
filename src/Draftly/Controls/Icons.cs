using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Draftly.Controls;

/// <summary>Draftly's own line icons (drawn as vectors so they're sharp at any size and follow the theme's text colour).</summary>
public static class Icons
{
    /// <summary>Print preview: a page of text with a magnifying glass over it.</summary>
    public static FrameworkElement PrintPreview(double size = 16)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        // Page outline with a folded corner; the right edge stops where the lens covers it.
        Add(canvas, "M 13.5,2.5 H 5 V 21.5 H 16", 1.5);
        Add(canvas, "M 13.5,2.5 L 18,7 V 9.4", 1.5);
        Add(canvas, "M 13.5,2.5 V 7 H 18", 1.5);
        // Lines of text, stopping short of the lens.
        Add(canvas, "M 7.5,6 H 11.2", 1.3);
        Add(canvas, "M 7.5,8.6 H 13.8", 1.3);
        Add(canvas, "M 7.5,11.1 H 10.6", 1.3);
        Add(canvas, "M 7.5,13.6 H 9.8", 1.3);
        Add(canvas, "M 7.5,16.1 H 10.3", 1.3);
        Add(canvas, "M 7.5,18.7 H 13.2", 1.3);
        // Magnifying glass: lens, glint and handle.
        Add(canvas, "M 19.4,13.8 A 4.2,4.2 0 1 1 11,13.8 A 4.2,4.2 0 1 1 19.4,13.8 Z", 1.5);
        Add(canvas, "M 13.2,12.2 A 2.4,2.4 0 0 1 15.6,11.3", 1.1);
        Add(canvas, "M 18.4,17 L 21.4,20.6", 2.6);
        return new Viewbox { Child = canvas, Width = size, Height = size, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
    }

    private static void Add(Canvas c, string data, double thickness)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        // Same colour as the text around it (menu, button, light or dark theme).
        p.SetBinding(Shape.StrokeProperty, new Binding("(TextElement.Foreground)")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.Self),
        });
        c.Children.Add(p);
    }
}
