using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Draftly.Controls;

/// <summary>
/// Line numbers in the page's left margin (on screen only — they aren't printed or saved). Lines are
/// counted the same way as "Line N" in the status bar, and only the lines on screen are measured.
/// </summary>
public sealed class LineNumberAdorner : Adorner
{
    private readonly RichTextBox _box;
    private readonly List<(double Bottom, int Number, bool Current)> _lines = new();
    private readonly DispatcherTimer _timer;
    public Func<Rect> VisibleRegion; // in editor coordinates

    private bool _show;
    public bool Show
    {
        get => _show;
        set { _show = value; Visibility = value ? Visibility.Visible : Visibility.Collapsed; Schedule(); }
    }

    /// <summary>Lines numbered last time (for the self-test).</summary>
    public IReadOnlyList<int> Numbers => _lines.Select(l => l.Number).ToList();

    public LineNumberAdorner(RichTextBox box) : base(box)
    {
        _box = box;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => { _timer.Stop(); Collect(); InvalidateVisual(); };
    }

    public void Schedule()
    {
        if (!_show) return;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Measures straight away (used by the self-test).</summary>
    public void CollectNow() { Collect(); InvalidateVisual(); }

    private void Collect()
    {
        _lines.Clear();
        if (!_show || _box.Document == null || _box.ActualHeight <= 0) return;
        try
        {
            Rect view = VisibleRegion?.Invoke() ?? new Rect(0, 0, _box.ActualWidth, _box.ActualHeight);
            view.Inflate(0, 30);
            var pad = _box.Document.PagePadding;
            double x = Math.Max(pad.Left + 4, 1);
            double y = Math.Clamp(view.Top, pad.Top, Math.Max(pad.Top, _box.ActualHeight - 2));

            var pos = _box.GetPositionFromPoint(new Point(x, y), true) ?? _box.Document.ContentStart;
            var line = pos.GetLineStartPosition(0) ?? pos;
            line.GetLineStartPosition(-int.MaxValue, out int moved);
            int number = -moved + 1;

            var caretLine = _box.CaretPosition.GetLineStartPosition(0);
            for (int guard = 0; line != null && guard < 2000; guard++)
            {
                var r = line.GetCharacterRect(LogicalDirection.Forward);
                if (!r.IsEmpty)
                {
                    if (r.Top > view.Bottom) break;
                    if (r.Bottom >= view.Top)
                        _lines.Add((r.Bottom, number, caretLine != null && caretLine.CompareTo(line) == 0));
                }
                var next = line.GetLineStartPosition(1, out int got);
                if (got == 0 || next == null) break;
                line = next;
                number++;
            }
        }
        catch { _lines.Clear(); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_show || _lines.Count == 0) return;
        var dim = (Brush)TryFindResource("TextSecondary") ?? Brushes.Gray;
        var cur = (Brush)TryFindResource("PageText") ?? Brushes.Black;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double right = Math.Max(_box.Document.PagePadding.Left - 12, 20);
        foreach (var (bottom, n, current) in _lines)
        {
            var ft = new FormattedText(n.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                tf, 11, current ? cur : dim, dpi);
            dc.DrawText(ft, new Point(right - ft.Width, bottom - ft.Height - 1));
        }
    }
}
