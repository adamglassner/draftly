using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Draftly.Services;

namespace Draftly.Dialogs;

public sealed class PageSetupDialog : DialogBase
{
    public PageLayout Result { get; private set; }
    public bool MakeDefault { get; private set; }

    private readonly PageLayout _work;
    private readonly MeasureUnit _unit;
    private readonly ComboBox _paper;
    private readonly RadioButton _portrait, _landscape;
    private readonly TextBox _l, _r, _t, _b;
    private readonly CheckBox _numbers, _default;
    private readonly Canvas _preview = new() { Width = 170, Height = 200 };

    public PageSetupDialog(PageLayout current, MeasureUnit unit) : base("Page setup", 560)
    {
        _work = current.Clone();
        _unit = unit;

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Body.Children.Add(columns);

        var left = new StackPanel();
        columns.Children.Add(left);

        left.Children.Add(Header("Paper"));
        _paper = Combo(PageLayout.PaperSizes.Select(p => (object)$"{p.Name}  ({p.WidthIn:0.##} × {p.HeightIn:0.##} in)"),
            Math.Max(0, Array.FindIndex(PageLayout.PaperSizes, p => p.Name == _work.PaperName)), 230);
        left.Children.Add(Row("Size", _paper, 90));

        var orient = new StackPanel { Orientation = Orientation.Horizontal };
        _portrait = new RadioButton { Content = "Portrait", GroupName = "orient", IsChecked = !_work.Landscape, Margin = new Thickness(0, 0, 18, 0) };
        _landscape = new RadioButton { Content = "Landscape", GroupName = "orient", IsChecked = _work.Landscape };
        orient.Children.Add(_portrait);
        orient.Children.Add(_landscape);
        left.Children.Add(Row("Orientation", orient, 90));

        left.Children.Add(Header($"Margins ({Units.Name(unit).ToLowerInvariant()})"));
        left.Children.Add(Row("Left", _l = Field(Units.Format(_work.MarginLeft, unit)), 90));
        left.Children.Add(Row("Right", _r = Field(Units.Format(_work.MarginRight, unit)), 90));
        left.Children.Add(Row("Top", _t = Field(Units.Format(_work.MarginTop, unit)), 90));
        left.Children.Add(Row("Bottom", _b = Field(Units.Format(_work.MarginBottom, unit)), 90));

        left.Children.Add(Header("Printing"));
        _numbers = new CheckBox { Content = "Print page numbers", IsChecked = _work.PageNumbers };
        _default = new CheckBox { Content = "Use these settings for new documents" };
        left.Children.Add(_numbers);
        left.Children.Add(_default);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);
        right.Children.Add(Header("Preview"));
        right.Children.Add(_preview);

        _paper.SelectionChanged += (_, _) => Update();
        _portrait.Checked += (_, _) => Update();
        _landscape.Checked += (_, _) => Update();
        foreach (var tb in new[] { _l, _r, _t, _b }) tb.TextChanged += (_, _) => Update();
        Update();

        AddButton("OK", primary: true, onClick: Ok);
        AddButton("Cancel", cancel: true);
    }

    private bool Read(bool show)
    {
        _work.PaperName = PageLayout.PaperSizes[Math.Max(0, _paper.SelectedIndex)].Name;
        _work.Landscape = _landscape.IsChecked == true;
        foreach (var (box, set) in new (TextBox, Action<double>)[]
                 {
                     (_l, v => _work.MarginLeft = v), (_r, v => _work.MarginRight = v),
                     (_t, v => _work.MarginTop = v), (_b, v => _work.MarginBottom = v),
                 })
        {
            if (!Units.TryParse(box.Text, _unit, out var v) || v < 0 || v > 4 * 96)
            {
                if (show)
                {
                    MessageDialog.Show(this, "Page setup", "Margins must be between 0 and 4 inches (10.16 cm).", MessageDialog.Kind.Warning);
                    box.Focus(); box.SelectAll();
                }
                return false;
            }
            set(v);
        }
        if (_work.PaperWidth - _work.MarginLeft - _work.MarginRight < 96 || _work.PaperHeight - _work.MarginTop - _work.MarginBottom < 96)
        {
            if (show) MessageDialog.Show(this, "Page setup", "The margins leave less than one inch for text. Make them smaller.", MessageDialog.Kind.Warning);
            return false;
        }
        return true;
    }

    private void Update()
    {
        if (_preview == null || _l == null || _b == null) return;
        Read(false);
        _preview.Children.Clear();
        double scale = Math.Min(150 / _work.PaperWidth, 190 / _work.PaperHeight);
        double w = _work.PaperWidth * scale, h = _work.PaperHeight * scale;
        double x = (170 - w) / 2, y = (200 - h) / 2;

        var page = new Rectangle { Width = w, Height = h, RadiusX = 2, RadiusY = 2, StrokeThickness = 1 };
        page.Fill = Brushes.White;
        page.SetResourceReference(Shape.StrokeProperty, "ControlBorder");
        Canvas.SetLeft(page, x); Canvas.SetTop(page, y);
        _preview.Children.Add(page);

        double tx = x + _work.MarginLeft * scale, ty = y + _work.MarginTop * scale;
        double tw = Math.Max(4, w - (_work.MarginLeft + _work.MarginRight) * scale);
        double th = Math.Max(4, h - (_work.MarginTop + _work.MarginBottom) * scale);
        for (double ly = ty + 2; ly < ty + th - 2; ly += 6)
        {
            var line = new Rectangle { Width = (ly - ty) % 36 > 30 ? tw * 0.6 : tw, Height = 2, Fill = new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC8)) };
            Canvas.SetLeft(line, tx); Canvas.SetTop(line, ly);
            _preview.Children.Add(line);
        }
        var box = new Rectangle { Width = tw, Height = th, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 } };
        box.SetResourceReference(Shape.StrokeProperty, "Accent");
        Canvas.SetLeft(box, tx); Canvas.SetTop(box, ty);
        _preview.Children.Add(box);
    }

    private void Ok()
    {
        if (!Read(true)) return;
        _work.PageNumbers = _numbers.IsChecked == true;
        Result = _work;
        MakeDefault = _default.IsChecked == true;
        DialogResult = true;
    }
}
