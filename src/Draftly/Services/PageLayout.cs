namespace Draftly.Services;

/// <summary>Page setup for the open document. All lengths are in WPF device-independent pixels (96 per inch).</summary>
public sealed class PageLayout
{
    public const double Dpi = 96.0;

    public static readonly (string Name, double WidthIn, double HeightIn)[] PaperSizes =
    {
        ("Letter", 8.5, 11),
        ("Legal", 8.5, 14),
        ("A4", 8.27, 11.69),
        ("A5", 5.83, 8.27),
        ("Executive", 7.25, 10.5),
        ("Tabloid", 11, 17),
    };

    public string PaperName { get; set; } = "Letter";
    public bool Landscape { get; set; }
    public double MarginLeft { get; set; } = 1.25 * Dpi;
    public double MarginRight { get; set; } = 1.25 * Dpi;
    public double MarginTop { get; set; } = 1.0 * Dpi;
    public double MarginBottom { get; set; } = 1.0 * Dpi;
    public bool PageNumbers { get; set; } = true;

    public double PaperWidth
    {
        get
        {
            var p = Find(PaperName);
            return (Landscape ? p.HeightIn : p.WidthIn) * Dpi;
        }
    }

    public double PaperHeight
    {
        get
        {
            var p = Find(PaperName);
            return (Landscape ? p.WidthIn : p.HeightIn) * Dpi;
        }
    }

    public double TextWidth => Math.Max(96, PaperWidth - MarginLeft - MarginRight);
    public double TextHeight => Math.Max(96, PaperHeight - MarginTop - MarginBottom);

    private static (string Name, double WidthIn, double HeightIn) Find(string name)
    {
        foreach (var p in PaperSizes)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return PaperSizes[0];
    }

    public static PageLayout FromSettings(AppSettings s) => new()
    {
        PaperName = s.PaperSize,
        Landscape = s.Landscape,
        MarginLeft = s.MarginLeftIn * Dpi,
        MarginRight = s.MarginRightIn * Dpi,
        MarginTop = s.MarginTopIn * Dpi,
        MarginBottom = s.MarginBottomIn * Dpi,
        PageNumbers = s.PrintPageNumbers,
    };

    public PageLayout Clone() => (PageLayout)MemberwiseClone();
}

/// <summary>Converts between DIPs and the user's chosen measurement unit.</summary>
public static class Units
{
    public static double PerUnit(MeasureUnit u) => u switch
    {
        MeasureUnit.Inches => 96.0,
        MeasureUnit.Centimeters => 96.0 / 2.54,
        MeasureUnit.Points => 96.0 / 72.0,
        MeasureUnit.Picas => 96.0 / 6.0,
        _ => 96.0,
    };

    public static string Suffix(MeasureUnit u) => u switch
    {
        MeasureUnit.Inches => "\"",
        MeasureUnit.Centimeters => " cm",
        MeasureUnit.Points => " pt",
        MeasureUnit.Picas => " pi",
        _ => "",
    };

    public static string Name(MeasureUnit u) => u switch
    {
        MeasureUnit.Inches => "Inches",
        MeasureUnit.Centimeters => "Centimeters",
        MeasureUnit.Points => "Points",
        MeasureUnit.Picas => "Picas",
        _ => u.ToString(),
    };

    public static string Format(double dips, MeasureUnit u)
    {
        double v = dips / PerUnit(u);
        string num = u is MeasureUnit.Points ? v.ToString("0.#") : v.ToString("0.##");
        return num + Suffix(u);
    }

    /// <summary>Parses "1.5", "1.5\"", "3 cm", "12pt" etc. Values without a unit use <paramref name="u"/>.</summary>
    public static bool TryParse(string text, MeasureUnit u, out double dips)
    {
        dips = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant();
        var unit = u;
        if (t.EndsWith("\"") || t.EndsWith("in")) { unit = MeasureUnit.Inches; t = t.TrimEnd('"').Replace("in", ""); }
        else if (t.EndsWith("cm")) { unit = MeasureUnit.Centimeters; t = t[..^2]; }
        else if (t.EndsWith("pt")) { unit = MeasureUnit.Points; t = t[..^2]; }
        else if (t.EndsWith("pi")) { unit = MeasureUnit.Picas; t = t[..^2]; }
        if (!double.TryParse(t.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out var v) &&
            !double.TryParse(t.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v))
            return false;
        dips = v * PerUnit(unit);
        return true;
    }
}
