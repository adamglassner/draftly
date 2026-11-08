using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Draftly.Services;

namespace Draftly.Controls;

/// <summary>
/// Draws paragraph marks (¶), spaces (·) and tabs (→) over the editor. Positions are gathered
/// after layout for the paragraphs currently on screen only, so large documents stay fast.
/// </summary>
public sealed class FormattingMarksAdorner : Adorner
{
    private readonly RichTextBox _box;
    private readonly List<(Point Pos, string Glyph, double Size)> _marks = new();
    private readonly DispatcherTimer _timer;
    public Func<Rect> VisibleRegion; // in editor coordinates

    /// <summary>¶ · → marks. Page-break lines are always drawn, since they're otherwise invisible on screen.</summary>
    public bool ShowMarks { get; set; }
    private readonly List<double> _pageBreaks = new();

    public FormattingMarksAdorner(RichTextBox box) : base(box)
    {
        _box = box;
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => { _timer.Stop(); Collect(); InvalidateVisual(); };
    }

    public void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    private void Collect()
    {
        _marks.Clear();
        _pageBreaks.Clear();
        var doc = _box.Document;
        if (doc == null) return;
        Rect view = VisibleRegion?.Invoke() ?? new Rect(0, 0, _box.ActualWidth, _box.ActualHeight);
        view.Inflate(0, 40);
        int budget = 20000;

        foreach (var el in DocumentService.Walk(doc.Blocks))
        {
            if (el is not Paragraph p) continue;
            Rect start, end;
            try
            {
                start = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                end = p.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            }
            catch { continue; }
            if (start.IsEmpty || end.IsEmpty) continue;
            if (end.Bottom < view.Top) continue;
            if (start.Top > view.Bottom) break;

            if (p.BreakPageBefore) _pageBreaks.Add(start.Top - 2);
            if (!ShowMarks) continue;

            // Size the mark from the text, not the line (a line with a picture is much taller).
            double size = Math.Clamp(Math.Min(end.Height * 0.8, p.FontSize * 1.05), 9, 72);
            _marks.Add((new Point(end.Right + 1, end.Bottom - size * 1.3), "¶", size));

            foreach (var inline in Inlines(p.Inlines))
            {
                if (inline is not Run run) continue;
                var text = run.Text;
                for (int i = 0; i < text.Length && budget > 0; i++)
                {
                    char ch = text[i];
                    if (ch != ' ' && ch != '\t' && ch != ' ') continue;
                    budget--;
                    var a = run.ContentStart.GetPositionAtOffset(i);
                    var b = run.ContentStart.GetPositionAtOffset(i + 1);
                    if (a == null || b == null) continue;
                    var ra = a.GetCharacterRect(LogicalDirection.Forward);
                    var rb = b.GetCharacterRect(LogicalDirection.Backward);
                    if (ra.IsEmpty || ra.Bottom < view.Top || ra.Top > view.Bottom) continue;
                    double sz = Math.Max(8, ra.Height * 0.7);
                    if (ch == '\t')
                        _marks.Add((new Point(ra.Left + 1, ra.Top + (ra.Height - sz * 1.25) / 2), "→", sz));
                    else
                    {
                        double mid = (Math.Abs(rb.Top - ra.Top) < 1 && rb.Left > ra.Left) ? (ra.Left + rb.Left) / 2 : ra.Left + 2;
                        _marks.Add((new Point(mid - sz * 0.15, ra.Top + (ra.Height - sz * 1.25) / 2), ch == ' ' ? "·" : "°", sz));
                    }
                }
            }
        }
    }

    private static IEnumerable<Inline> Inlines(InlineCollection inlines)
    {
        foreach (var i in inlines)
        {
            yield return i;
            if (i is Span s) foreach (var x in Inlines(s.Inlines)) yield return x;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var brush = (Brush)FindResource("Accent");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tf = new Typeface("Segoe UI");
        if (_pageBreaks.Count > 0)
        {
            var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            var pad = _box.Document.PagePadding;
            foreach (var y in _pageBreaks)
            {
                var label = new FormattedText("Page break", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, 11, brush, dpi);
                double x0 = pad.Left, x1 = _box.ActualWidth - pad.Right;
                double mid = (x0 + x1) / 2;
                dc.DrawLine(pen, new Point(x0, y), new Point(mid - label.Width / 2 - 6, y));
                dc.DrawLine(pen, new Point(mid + label.Width / 2 + 6, y), new Point(x1, y));
                dc.DrawText(label, new Point(mid - label.Width / 2, y - label.Height / 2));
            }
        }
        if (_marks.Count == 0) return;
        foreach (var (pos, glyph, size) in _marks)
        {
            var ft = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush, dpi);
            dc.DrawText(ft, pos);
        }
    }
}
