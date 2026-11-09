using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Draftly.Services;

public enum ParaStyle { Normal, Title, Heading1, Heading2, Heading3, Quote, Code }

/// <summary>
/// Paragraph styles (headings, quote, code). They're stored as ordinary formatting (size, bold,
/// spacing, a left border), so they save in RTF and look right in WordPad and Word. Draftly
/// recognises them again when a file is reopened.
/// </summary>
public static class ParagraphStyles
{
    public static readonly (ParaStyle Style, string Name, string Shortcut)[] All =
    {
        (ParaStyle.Normal, "Normal", "Ctrl+Alt+0"),
        (ParaStyle.Title, "Title", "Ctrl+Alt+T"),
        (ParaStyle.Heading1, "Heading 1", "Ctrl+Alt+1"),
        (ParaStyle.Heading2, "Heading 2", "Ctrl+Alt+2"),
        (ParaStyle.Heading3, "Heading 3", "Ctrl+Alt+3"),
        (ParaStyle.Quote, "Quote", ""),
        (ParaStyle.Code, "Code", ""),
    };

    // Point sizes; headings are bold.
    private const double TitlePt = 26, H1Pt = 20, H2Pt = 16, H3Pt = 13;
    public static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, Courier New");
    private static readonly SolidColorBrush RuleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public static string NameOf(ParaStyle s) => All.First(a => a.Style == s).Name;

    /// <summary>Applies a style to a paragraph. Character formatting other than size/weight (bold inside a heading, colour, italic) is kept.</summary>
    public static void Apply(Paragraph p, ParaStyle style)
    {
        // Start from Normal: clear what the styles set.
        foreach (var dp in new DependencyProperty[]
                 {
                     TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.FontStyleProperty,
                     TextElement.FontFamilyProperty, Block.BorderThicknessProperty, Block.BorderBrushProperty, Block.PaddingProperty,
                 })
            p.ClearValue(dp);
        foreach (var inline in Inlines(p))
        {
            inline.ClearValue(TextElement.FontSizeProperty);
            if (style is ParaStyle.Title or ParaStyle.Heading1 or ParaStyle.Heading2 or ParaStyle.Heading3)
                inline.ClearValue(TextElement.FontWeightProperty);
            if (style == ParaStyle.Code) inline.ClearValue(TextElement.FontFamilyProperty);
        }
        var m = p.Margin;
        double left = double.IsNaN(m.Left) ? 0 : m.Left, right = double.IsNaN(m.Right) ? 0 : m.Right;

        double Dip(double pt) => pt * 96.0 / 72.0;
        switch (style)
        {
            case ParaStyle.Title:
                Heading(p, TitlePt);
                p.Margin = new Thickness(left, 0, right, Dip(10));
                break;
            case ParaStyle.Heading1:
                Heading(p, H1Pt);
                p.Margin = new Thickness(left, Dip(12), right, Dip(6));
                break;
            case ParaStyle.Heading2:
                Heading(p, H2Pt);
                p.Margin = new Thickness(left, Dip(10), right, Dip(4));
                break;
            case ParaStyle.Heading3:
                Heading(p, H3Pt);
                p.Margin = new Thickness(left, Dip(8), right, Dip(4));
                break;
            case ParaStyle.Quote:
                p.FontStyle = FontStyles.Italic;
                p.BorderBrush = RuleBrush;
                p.BorderThickness = new Thickness(3, 0, 0, 0);
                p.Padding = new Thickness(12, 2, 0, 2);
                p.Margin = new Thickness(left, Dip(4), right, Dip(4));
                break;
            case ParaStyle.Code:
                p.FontFamily = CodeFont;
                p.FontSize = Dip(10);
                p.BorderBrush = RuleBrush;
                p.BorderThickness = new Thickness(3, 0, 0, 0);
                p.Padding = new Thickness(12, 2, 0, 2);
                p.Margin = new Thickness(left, Dip(2), right, Dip(2));
                break;
            default:
                p.Margin = new Thickness(left, 0, right, 0);
                break;
        }
    }

    private static void Heading(Paragraph p, double pt)
    {
        p.FontSize = pt * 96.0 / 72.0;
        p.FontWeight = FontWeights.Bold;
    }

    /// <summary>Works out a paragraph's style from its formatting (also for files made elsewhere).</summary>
    public static ParaStyle Detect(Paragraph p)
    {
        var first = Inlines(p).OfType<Run>().FirstOrDefault(r => r.Text.Trim().Length > 0);
        double size = first?.FontSize ?? p.FontSize;
        var weight = first?.FontWeight ?? p.FontWeight;
        var family = first?.FontFamily ?? p.FontFamily;
        bool leftRule = p.BorderThickness.Left >= 2 && p.BorderThickness.Top == 0 && p.BorderThickness.Bottom == 0;

        if (leftRule && IsCodeFont(family)) return ParaStyle.Code;
        if (leftRule && (first?.FontStyle ?? p.FontStyle) == FontStyles.Italic) return ParaStyle.Quote;
        if (weight.ToOpenTypeWeight() >= 600)
        {
            double pt = size * 72.0 / 96.0;
            if (Math.Abs(pt - TitlePt) < 0.6) return ParaStyle.Title;
            if (Math.Abs(pt - H1Pt) < 0.6) return ParaStyle.Heading1;
            if (Math.Abs(pt - H2Pt) < 0.6) return ParaStyle.Heading2;
            if (Math.Abs(pt - H3Pt) < 0.6) return ParaStyle.Heading3;
        }
        return ParaStyle.Normal;
    }

    public static bool IsCodeFont(FontFamily f)
    {
        var s = f?.Source ?? "";
        return s.Contains("Consolas", StringComparison.OrdinalIgnoreCase) || s.Contains("Cascadia", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("Courier", StringComparison.OrdinalIgnoreCase) || s.Contains("Mono", StringComparison.OrdinalIgnoreCase);
    }

    public static int HeadingLevel(ParaStyle s) => s switch
    {
        ParaStyle.Title or ParaStyle.Heading1 => 1,
        ParaStyle.Heading2 => 2,
        ParaStyle.Heading3 => 3,
        _ => 0,
    };

    private static IEnumerable<Inline> Inlines(Paragraph p)
    {
        foreach (var i in p.Inlines)
        {
            yield return i;
            if (i is Span s)
                foreach (var x in Descendants(s)) yield return x;
        }
    }

    private static IEnumerable<Inline> Descendants(Span s)
    {
        foreach (var i in s.Inlines)
        {
            yield return i;
            if (i is Span c) foreach (var x in Descendants(c)) yield return x;
        }
    }
}
