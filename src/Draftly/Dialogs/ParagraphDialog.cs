using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Draftly.Services;

namespace Draftly.Dialogs;

public sealed class ParagraphValues
{
    public double? Left, Right, FirstLine;     // DIPs
    public double? Before, After;              // points
    public double? LineSpacing;                // multiple of single (1.0, 1.15 ...)
    public TextAlignment? Alignment;
    public bool? KeepTogether, PageBreakBefore;
}

public sealed class ParagraphDialog : DialogBase
{
    public static readonly double[] Spacings = { 1.0, 1.15, 1.5, 2.0, 2.5, 3.0 };
    private static readonly string[] AlignNames = { "Left", "Center", "Right", "Justify" };

    public ParagraphValues Result { get; private set; }

    private readonly TextBox _left, _right, _first, _before, _after;
    private readonly ComboBox _line, _align;
    private readonly CheckBox _keep, _pageBreak;
    private readonly MeasureUnit _unit;

    public ParagraphDialog(ParagraphValues v, MeasureUnit unit) : base("Paragraph", 460)
    {
        _unit = unit;
        string F(double? d) => d.HasValue ? Units.Format(d.Value, unit) : "";
        string P(double? d) => d.HasValue ? d.Value.ToString("0.#", CultureInfo.CurrentCulture) + " pt" : "";

        Body.Children.Add(Header("Indentation"));
        Body.Children.Add(Row("Left", _left = Field(F(v.Left))));
        Body.Children.Add(Row("Right", _right = Field(F(v.Right))));
        Body.Children.Add(Row("First line", _first = Field(F(v.FirstLine))));
        Body.Children.Add(Hint("Use a negative first-line value for a hanging indent. Leave a box empty to keep mixed values."));

        Body.Children.Add(Header("Spacing"));
        Body.Children.Add(Row("Before paragraph", _before = Field(P(v.Before))));
        Body.Children.Add(Row("After paragraph", _after = Field(P(v.After))));
        _line = Combo(new object[] { "Mixed", "1.0", "1.15", "1.5", "2.0", "2.5", "3.0" }, 0, 140);
        if (v.LineSpacing is double ls)
        {
            int idx = Array.FindIndex(Spacings, s => Math.Abs(s - ls) < 0.05);
            _line.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        Body.Children.Add(Row("Line spacing", _line));

        Body.Children.Add(Header("Alignment and flow"));
        _align = Combo(new object[] { "Mixed", "Left", "Center", "Right", "Justify" }, 0, 140);
        if (v.Alignment is TextAlignment a) _align.SelectedIndex = Array.IndexOf(AlignNames, a.ToString()) + 1;
        Body.Children.Add(Row("Alignment", _align));
        _keep = new CheckBox { Content = "Keep lines together when printing", IsChecked = v.KeepTogether, IsThreeState = v.KeepTogether == null };
        _pageBreak = new CheckBox { Content = "Start on a new page", IsChecked = v.PageBreakBefore, IsThreeState = v.PageBreakBefore == null };
        Body.Children.Add(_keep);
        Body.Children.Add(_pageBreak);

        AddButton("OK", primary: true, onClick: Ok);
        AddButton("Cancel", cancel: true);
    }

    private void Ok()
    {
        var r = new ParagraphValues();
        if (!ParseLen(_left, out r.Left) || !ParseLen(_right, out r.Right) || !ParseLen(_first, out r.FirstLine, allowNegative: true) ||
            !ParsePt(_before, out r.Before) || !ParsePt(_after, out r.After))
            return;
        if (_line.SelectedIndex > 0) r.LineSpacing = Spacings[_line.SelectedIndex - 1];
        if (_align.SelectedIndex > 0) r.Alignment = Enum.Parse<TextAlignment>(AlignNames[_align.SelectedIndex - 1]);
        r.KeepTogether = _keep.IsChecked;
        r.PageBreakBefore = _pageBreak.IsChecked;
        Result = r;
        DialogResult = true;
    }

    private bool ParseLen(TextBox box, out double? value, bool allowNegative = false)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(box.Text)) return true;
        if (Units.TryParse(box.Text, _unit, out var d) && (allowNegative || d >= 0) && Math.Abs(d) < 22 * 96)
        { value = d; return true; }
        Invalid(box, "Enter a measurement such as 0.5\" or 1.2 cm.");
        return false;
    }

    private bool ParsePt(TextBox box, out double? value)
    {
        value = null;
        var t = box.Text.Replace("pt", "").Trim();
        if (t.Length == 0) return true;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) && d >= 0 && d <= 1584)
        { value = d; return true; }
        Invalid(box, "Enter spacing in points, between 0 and 1584.");
        return false;
    }

    private void Invalid(TextBox box, string msg)
    {
        MessageDialog.Show(this, "Paragraph", msg, MessageDialog.Kind.Warning);
        box.Focus();
        box.SelectAll();
    }
}
