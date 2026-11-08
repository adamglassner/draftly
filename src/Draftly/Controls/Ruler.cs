using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Draftly.Services;

namespace Draftly.Controls;

/// <summary>
/// Horizontal ruler. Draws ticks in the user's units, shades the page margins and shows the
/// current paragraph's first-line, left and right indents, which can be dragged.
/// </summary>
public sealed class Ruler : FrameworkElement
{
    // Geometry supplied by the main window (all in DIPs, already scaled by zoom where noted)
    public double PageLeft;        // x of the page's left edge on this control (screen, zoomed)
    public double PageWidth;       // unzoomed page width
    public double MarginLeft;      // unzoomed
    public double TextWidth;       // unzoomed width of the text column
    public double Zoom = 1;
    public MeasureUnit Unit = MeasureUnit.Inches;

    // Current paragraph indents (unzoomed)
    public double LeftIndent, FirstLineIndent, RightIndent;
    public bool IndentsEnabled = true;

    /// <summary>Raised when the user finishes dragging a marker: (left, firstLine (relative), right).</summary>
    public event Action<double, double, double> IndentsChanged;

    private enum Drag { None, First, Hanging, Left, Right }
    private Drag _drag;
    private double _dragStartX, _l0, _f0, _r0;

    public Ruler()
    {
        Cursor = Cursors.Arrow;
        SnapsToDevicePixels = true;
    }

    public void Refresh() => InvalidateVisual();

    private double ZeroX => PageLeft + MarginLeft * Zoom;
    private double FirstX => ZeroX + (LeftIndent + FirstLineIndent) * Zoom;
    private double LeftX => ZeroX + LeftIndent * Zoom;
    private double RightX => ZeroX + (TextWidth - RightIndent) * Zoom;

    protected override void OnRender(DrawingContext dc)
    {
        var bg = (Brush)FindResource("RulerBg");
        var margin = (Brush)FindResource("RulerMargin");
        var text = (Brush)FindResource("RulerText");
        var accent = (Brush)FindResource("Accent");
        double h = ActualHeight, w = ActualWidth;

        dc.DrawRectangle(margin, null, new Rect(0, 0, w, h));
        if (PageWidth <= 0) return;

        // Page area, with the text column brighter
        var pageRect = new Rect(PageLeft, 3, PageWidth * Zoom, h - 6);
        dc.DrawRectangle(margin, null, pageRect);
        dc.DrawRectangle(bg, null, new Rect(ZeroX, 3, TextWidth * Zoom, h - 6));

        // Ticks
        double unit = Units.PerUnit(Unit);
        double labelEvery; int subdivisions;
        switch (Unit)
        {
            case MeasureUnit.Centimeters: labelEvery = 1; subdivisions = 4; break;
            case MeasureUnit.Points: labelEvery = 72; subdivisions = 8; break;
            case MeasureUnit.Picas: labelEvery = 6; subdivisions = 6; break;
            default: labelEvery = 1; subdivisions = 8; break;
        }
        double major = labelEvery * unit * Zoom;
        if (major < 28) { labelEvery *= 2; major *= 2; subdivisions = Math.Max(2, subdivisions / 2); }
        double minor = major / subdivisions;

        var tickPen = new Pen(text, 1);
        tickPen.Freeze();
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double startX = PageLeft, endX = PageLeft + PageWidth * Zoom;
        int firstIndex = (int)Math.Floor((startX - ZeroX) / minor);
        int lastIndex = (int)Math.Ceiling((endX - ZeroX) / minor);
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            double x = Math.Round(ZeroX + i * minor) + 0.5;
            if (x < startX || x > endX) continue;
            int mod = ((i % subdivisions) + subdivisions) % subdivisions;
            if (mod == 0)
            {
                if (i == 0) continue;
                var label = Math.Abs(i / subdivisions * labelEvery).ToString(CultureInfo.CurrentCulture);
                var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 10.5, text, dpi);
                dc.DrawText(ft, new Point(x - ft.Width / 2, (h - ft.Height) / 2));
            }
            else
            {
                double len = (subdivisions % 2 == 0 && mod == subdivisions / 2) ? 7 : 3;
                dc.DrawLine(tickPen, new Point(x, (h - len) / 2), new Point(x, (h + len) / 2));
            }
        }

        if (!IndentsEnabled) return;

        // Indent markers
        var fill = bg;
        var stroke = new Pen(accent, 1.2);
        stroke.Freeze();
        // First line: down-pointing triangle at the top
        dc.DrawGeometry(fill, stroke, Triangle(FirstX, 3, down: true));
        // Hanging (left) indent: up-pointing triangle at the bottom + box
        dc.DrawGeometry(fill, stroke, Triangle(LeftX, h - 3, down: false));
        // Right indent
        dc.DrawGeometry(fill, stroke, Triangle(RightX, h - 3, down: false));
    }

    private static Geometry Triangle(double x, double y, bool down)
    {
        double s = 5.5, d = down ? 7 : -7;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x - s, y), true, true);
            ctx.LineTo(new Point(x + s, y), true, false);
            ctx.LineTo(new Point(x, y + d), true, false);
        }
        g.Freeze();
        return g;
    }

    // ------------------------------------------------------------------ dragging

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_drag == Drag.None)
        {
            Cursor = HitTest(p) != Drag.None ? Cursors.SizeWE : Cursors.Arrow;
            ToolTip = HitTest(p) switch
            {
                Drag.First => "First line indent",
                Drag.Hanging => "Hanging indent",
                Drag.Left => "Left indent",
                Drag.Right => "Right indent",
                _ => null,
            };
            return;
        }

        double delta = (p.X - _dragStartX) / Zoom;
        double snap = Units.PerUnit(Unit) / (Unit == MeasureUnit.Inches ? 16 : 4);
        if (!Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt)) delta = Math.Round(delta / snap) * snap;

        switch (_drag)
        {
            case Drag.First:
                FirstLineIndent = Math.Clamp(_f0 + delta, -_l0, TextWidth - _l0 - _r0 - 20);
                break;
            case Drag.Hanging:
                // Moves the left indent but keeps the first line where it is
                LeftIndent = Math.Clamp(_l0 + delta, 0, TextWidth - _r0 - 20);
                FirstLineIndent = (_l0 + _f0) - LeftIndent;
                break;
            case Drag.Left:
                LeftIndent = Math.Clamp(_l0 + delta, Math.Max(0, -_f0), TextWidth - _r0 - 20);
                break;
            case Drag.Right:
                RightIndent = Math.Clamp(_r0 - delta, 0, TextWidth - _l0 - 20);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IndentsEnabled) return;
        var p = e.GetPosition(this);
        _drag = HitTest(p);
        if (_drag == Drag.Hanging && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _drag = Drag.Left;
        if (_drag == Drag.None) return;
        _dragStartX = p.X; _l0 = LeftIndent; _f0 = FirstLineIndent; _r0 = RightIndent;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        ReleaseMouseCapture();
        IndentsChanged?.Invoke(LeftIndent, FirstLineIndent, RightIndent);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _drag = Drag.None;
    }

    private Drag HitTest(Point p)
    {
        if (!IndentsEnabled) return Drag.None;
        double h = ActualHeight;
        bool top = p.Y < h / 2;
        if (top && Math.Abs(p.X - FirstX) <= 7) return Drag.First;
        if (!top && Math.Abs(p.X - LeftX) <= 7) return Drag.Hanging;
        if (!top && Math.Abs(p.X - RightX) <= 7) return Drag.Right;
        return Drag.None;
    }
}
