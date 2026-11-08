using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Draftly.Dialogs;
using Draftly.Services;

namespace Draftly;

public partial class MainWindow
{
    private static readonly double[] StandardSizes = { 8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };

    private Color? _textColor = Color.FromRgb(0xC0, 0x00, 0x00);
    private Color? _highlightColor = Color.FromRgb(0xFF, 0xF2, 0x00);

    // ================================================================== font lists

    private void PopulateFontLists()
    {
        FontFamilyBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var s in StandardSizes) FontSizeBox.Items.Add(s.ToString(CultureInfo.CurrentCulture));
    }

    private void FontFamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || FontFamilyBox.SelectedItem is not FontFamily ff) return;
        // Only apply when the user picked from the list (not while typing).
        if (!FontFamilyBox.IsDropDownOpen && FontFamilyBox.IsKeyboardFocusWithin && !Keyboard.IsKeyDown(Key.Enter)) return;
        ApplyFontFamily(ff.Source);
    }

    private void FontFamilyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyFontFamily(FontFamilyBox.Text); Editor.Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { UpdateFormattingUi(); Editor.Focus(); e.Handled = true; }
    }

    private void FontFamilyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FontFamilyBox.IsDropDownOpen) return;
        if (e.NewFocus == Editor) UpdateFormattingUi();
    }

    private void ApplyFontFamily(string name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var match = Fonts.SystemFontFamilies.FirstOrDefault(f => string.Equals(f.Source, name, StringComparison.CurrentCultureIgnoreCase));
        if (match == null)
        {
            MessageDialog.Show(this, "Font", $"The font \"{name}\" isn't installed on this PC.", MessageDialog.Kind.Warning);
            UpdateFormattingUi();
            return;
        }
        ApplyToSelection(TextElement.FontFamilyProperty, match);
        Editor.Focus();
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || FontSizeBox.SelectedItem is not string s) return;
        if (!FontSizeBox.IsDropDownOpen && FontSizeBox.IsKeyboardFocusWithin) return;
        ApplyFontSizeText(s);
    }

    private void FontSizeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyFontSizeText(FontSizeBox.Text); e.Handled = true; }
        else if (e.Key == Key.Escape) { UpdateFormattingUi(); Editor.Focus(); e.Handled = true; }
    }

    private void FontSizeBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!FontSizeBox.IsDropDownOpen && e.NewFocus == Editor) UpdateFormattingUi();
    }

    private void ApplyFontSizeText(string text)
    {
        if (!double.TryParse(text?.Replace("pt", "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var pt) || pt < 1 || pt > 1638)
        {
            MessageDialog.Show(this, "Font size", "Enter a size between 1 and 1638 points.", MessageDialog.Kind.Warning);
            UpdateFormattingUi();
            return;
        }
        pt = Math.Round(pt * 2) / 2;
        ApplyToSelection(TextElement.FontSizeProperty, DocumentService.PointsToDips(pt));
        Editor.Focus();
    }

    private double CurrentPointSize()
    {
        var v = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        if (v is double d) return DocumentService.DipsToPoints(d);
        var start = Editor.Selection.Start.Parent as TextElement;
        return start != null ? DocumentService.DipsToPoints(start.FontSize) : S.DefaultFontSize;
    }

    private void GrowFont_Click(object sender, RoutedEventArgs e) => StepFont(+1);
    private void ShrinkFont_Click(object sender, RoutedEventArgs e) => StepFont(-1);

    private void StepFont(int dir)
    {
        double cur = CurrentPointSize();
        double next = dir > 0
            ? StandardSizes.FirstOrDefault(s => s > cur + 0.01, Math.Min(1638, Math.Ceiling(cur / 10 + 1) * 10))
            : StandardSizes.LastOrDefault(s => s < cur - 0.01, Math.Max(1, cur - 1));
        if (cur > 72 && dir < 0) next = Math.Max(72, Math.Floor(cur / 10 - 1) * 10);
        ApplyToSelection(TextElement.FontSizeProperty, DocumentService.PointsToDips(next));
    }

    private void ClearFormatting_Click(object sender, RoutedEventArgs e) => ClearFormatting();

    private void ClearFormatting()
    {
        Editor.BeginChange();
        try
        {
            Editor.Selection.ClearAllProperties();
        }
        finally { Editor.EndChange(); }
        UpdateFormattingUi();
        Editor.Focus();
    }

    /// <summary>Applies a character property; with an empty selection it sets the format for the next typed text.</summary>
    private void ApplyToSelection(DependencyProperty prop, object value)
    {
        Editor.Selection.ApplyPropertyValue(prop, value);
        UpdateFormattingUi();
        Editor.Focus();
    }

    // ================================================================== B / I / U / S / sub / super

    private void Bold_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Editor);
    private void Italic_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Editor);
    private void Underline_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Underline);
    private void Strike_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Strikethrough);

    private bool HasDecoration(TextDecorationLocation loc)
    {
        var v = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        if (v is TextDecorationCollection c && c.Any(d => d.Location == loc)) return true;
        // Also look at what encloses the text (pasted RTF can carry it on a Span or the paragraph).
        var sel = Editor.Selection;
        var pos = sel.IsEmpty ? sel.Start : (sel.Start.GetNextInsertionPosition(LogicalDirection.Forward) ?? sel.Start);
        for (DependencyObject d = pos.Parent; d is TextElement t; d = t.Parent)
        {
            var deco = t switch { Inline i => i.TextDecorations, Paragraph p => p.TextDecorations, _ => null };
            if (deco != null && deco.Any(x => x.Location == loc)) return true;
            if (t is Block) break;
        }
        return false;
    }

    /// <summary>Toggles underline or strikethrough without removing the other.</summary>
    private void ToggleDecoration(TextDecorationLocation loc)
    {
        bool u = HasDecoration(TextDecorationLocation.Underline);
        bool st = HasDecoration(TextDecorationLocation.Strikethrough);
        if (loc == TextDecorationLocation.Underline) u = !u; else st = !st;
        ApplyToSelection(Inline.TextDecorationsProperty, DocumentService.Decorations(u, st));
    }

    private void Subscript_Click(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Subscript);
    private void Superscript_Click(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Superscript);

    /// <summary>Sub/superscript: shifts the baseline and shrinks the text to about two-thirds, like Word.</summary>
    private void ToggleBaseline(BaselineAlignment which)
    {
        var current = Editor.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
        bool on = current is BaselineAlignment b && b == which;
        bool otherOn = current is BaselineAlignment o && (o == BaselineAlignment.Subscript || o == BaselineAlignment.Superscript) && o != which;
        double size = CurrentPointSize();
        Editor.BeginChange();
        try
        {
            if (on)
            {
                Editor.Selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
                Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, DocumentService.PointsToDips(Math.Round(size * 1.5 * 2) / 2));
            }
            else
            {
                Editor.Selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, which);
                if (!otherOn)
                    Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, DocumentService.PointsToDips(Math.Max(1, Math.Round(size / 1.5 * 2) / 2)));
            }
        }
        finally { Editor.EndChange(); }
        UpdateFormattingUi();
        Editor.Focus();
    }

    // ================================================================== colours

    private static readonly Color[] TextPalette = BuildTextPalette();

    private static Color[] BuildTextPalette()
    {
        string[] hex =
        {
            "#000000", "#404040", "#7F7F7F", "#BFBFBF", "#FFFFFF", "#C00000", "#FF0000", "#FFC000", "#FFFF00", "#92D050",
            "#00B050", "#00B0F0", "#0070C0", "#002060", "#7030A0", "#E36C09", "#943634", "#4F6228", "#1F497D", "#5F497A",
            "#F2DCDB", "#FDE9D9", "#EBF1DD", "#DBEEF3", "#E5E0EC", "#D99694", "#FAC08F", "#C3D69B", "#93CDDD", "#B2A2C7",
        };
        return hex.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();
    }

    private static readonly Color[] HighlightPalette =
    {
        Color.FromRgb(0xFF, 0xF2, 0x00), Color.FromRgb(0x00, 0xFF, 0x00), Color.FromRgb(0x00, 0xFF, 0xFF), Color.FromRgb(0xFF, 0x00, 0xFF),
        Color.FromRgb(0x00, 0x00, 0xFF), Color.FromRgb(0xFF, 0x00, 0x00), Color.FromRgb(0x00, 0x00, 0x80), Color.FromRgb(0x00, 0x80, 0x80),
        Color.FromRgb(0x00, 0x80, 0x00), Color.FromRgb(0x80, 0x00, 0x80), Color.FromRgb(0x80, 0x00, 0x00), Color.FromRgb(0x80, 0x80, 0x00),
        Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0xC0, 0xC0, 0xC0), Color.FromRgb(0xFF, 0xD8, 0xA8), Color.FromRgb(0xC9, 0xE7, 0xFF),
    };

    private void TextColor_Click(object sender, RoutedEventArgs e) => ApplyTextColor(_textColor);
    private void Highlight_Click(object sender, RoutedEventArgs e) => ApplyHighlight(_highlightColor);

    private void ApplyTextColor(Color? c)
    {
        if (c.HasValue) Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(c.Value));
        else MakeSelectionAutomaticColour();
        Editor.Focus();
    }

    private void ApplyHighlight(Color? c)
    {
        Editor.BeginChange();
        try
        {
            if (c == null) Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
            else Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(c.Value));
            DocumentService.FixHighlightContrast(DocumentService.Walk(Editor.Document.Blocks));
        }
        finally { Editor.EndChange(); }
        Editor.Focus();
    }

    /// <summary>"Automatic" colour: remove explicit colours so text follows the theme (and saves as black).</summary>
    private void MakeSelectionAutomaticColour()
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;
        Editor.BeginChange();
        try
        {
            // Applying a value first splits runs exactly at the selection edges,
            // so clearing afterwards never touches text outside the selection.
            sel.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Black);
            foreach (var el in DocumentService.Walk(Editor.Document.Blocks))
            {
                if (el is not Inline inl) continue;
                if (inl.ContentEnd.CompareTo(sel.Start) <= 0 || inl.ContentStart.CompareTo(sel.End) >= 0) continue;
                inl.ClearValue(TextElement.ForegroundProperty);
            }
        }
        finally { Editor.EndChange(); }
    }

    private void TextColorDrop_Click(object sender, RoutedEventArgs e) =>
        ShowPalette(TextColorDrop, TextPalette, "Automatic", 10, c =>
        {
            _textColor = c;
            TextColorSwatch.Fill = c.HasValue ? new SolidColorBrush(c.Value) : (Brush)FindResource("PageText");
            ApplyTextColor(c);
        }, allowCustom: true);

    private void HighlightDrop_Click(object sender, RoutedEventArgs e) =>
        ShowPalette(HighlightDrop, HighlightPalette, "No colour", 8, c =>
        {
            _highlightColor = c;
            HighlightSwatch.Fill = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
            ApplyHighlight(c);
        }, allowCustom: false);

    private void ShowPalette(FrameworkElement anchor, Color[] colors, string noneLabel, int perRow, Action<Color?> pick, bool allowCustom)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        var panel = new StackPanel { Margin = new Thickness(8) };
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = panel, Margin = new Thickness(0, 4, 10, 10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.25 } };
        card.SetResourceReference(Border.BackgroundProperty, "MenuBg");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
        popup.Child = card;

        var none = new Button { Style = (Style)FindResource("RibbonSmall"), Tag = "", Content = noneLabel, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
        none.Click += (_, _) => { popup.IsOpen = false; pick(null); };
        panel.Children.Add(none);

        var grid = new UniformGrid { Columns = perRow };
        foreach (var c in colors)
        {
            var color = c;
            var b = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                Width = 22, Height = 22, Margin = new Thickness(3),
                Background = new SolidColorBrush(color), ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}", Focusable = false,
            };
            b.SetResourceReference(BorderBrushProperty, "ControlBorder");
            b.Click += (_, _) => { popup.IsOpen = false; pick(color); };
            grid.Children.Add(b);
        }
        panel.Children.Add(grid);

        if (allowCustom)
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var go = new Button { Content = "Use", MinWidth = 56, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(go, Dock.Right);
            var hex = new TextBox { Text = "#", ToolTip = "Custom colour, e.g. #1E90FF" };
            void Apply()
            {
                try
                {
                    var t = hex.Text.Trim();
                    if (!t.StartsWith("#")) t = "#" + t;
                    var c = (Color)ColorConverter.ConvertFromString(t);
                    popup.IsOpen = false;
                    pick(Color.FromRgb(c.R, c.G, c.B));
                }
                catch { hex.SelectAll(); }
            }
            go.Click += (_, _) => Apply();
            hex.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) { Apply(); ke.Handled = true; } };
            row.Children.Add(go);
            row.Children.Add(hex);
            panel.Children.Add(row);
        }
        popup.Closed += (_, _) => Editor.Focus();
        popup.IsOpen = true;
    }

    // ================================================================== paragraphs

    /// <summary>All paragraphs touched by the selection (including the caret's paragraph).</summary>
    private List<Paragraph> SelectedParagraphs()
    {
        var sel = Editor.Selection;
        var list = new List<Paragraph>();
        foreach (var el in DocumentService.Walk(Editor.Document.Blocks))
        {
            if (el is not Paragraph p) continue;
            if (p.ContentEnd.CompareTo(sel.Start) < 0) continue;
            if (p.ContentStart.CompareTo(sel.End) > 0) break;
            list.Add(p);
        }
        if (list.Count == 0 && sel.Start.Paragraph != null) list.Add(sel.Start.Paragraph);
        return list;
    }

    private void Align_Click(object sender, RoutedEventArgs e)
    {
        var align = Enum.Parse<TextAlignment>((string)((FrameworkElement)sender).Tag);
        SetAlignment(align);
    }

    private void SetAlignment(TextAlignment align)
    {
        Editor.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, align);
        UpdateFormattingUi();
        Editor.Focus();
    }

    private void IndentMore_Click(object sender, RoutedEventArgs e) => ChangeIndent(+1);
    private void IndentLess_Click(object sender, RoutedEventArgs e) => ChangeIndent(-1);

    /// <summary>Like WordPad: moves the left indent by half an inch. Inside a list, nests or un-nests the item.</summary>
    private void ChangeIndent(int dir)
    {
        if (CurrentList() != null)
        {
            (dir > 0 ? EditingCommands.IncreaseIndentation : EditingCommands.DecreaseIndentation).Execute(null, Editor);
            Editor.Focus();
            return;
        }
        const double step = 48; // 0.5 inch
        Editor.BeginChange();
        try
        {
            foreach (var p in SelectedParagraphs())
            {
                var m = p.Margin;
                double left = Math.Max(0, Math.Round((Safe(m.Left) + dir * step) / step) * step);
                left = Math.Min(left, Math.Max(0, _textWidth - Safe(m.Right) - 96));
                if (left + Safe(p.TextIndent) < 0) p.TextIndent = -left;
                p.Margin = new Thickness(left, Safe(m.Top), Safe(m.Right), Safe(m.Bottom));
            }
        }
        finally { Editor.EndChange(); }
        UpdateRuler();
        Editor.Focus();
    }

    private List CurrentList() => (Editor.Selection.Start.Paragraph?.Parent as ListItem)?.List;

    private static bool IsNumbered(TextMarkerStyle s) => s is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or
        TextMarkerStyle.UpperLatin or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;

    private void Bullets_Click(object sender, RoutedEventArgs e) => ToggleList(TextMarkerStyle.Disc);
    private void Numbering_Click(object sender, RoutedEventArgs e) => ToggleList(TextMarkerStyle.Decimal);

    /// <summary>Turns a bullet/number list on or off, or switches an existing list to the given style.</summary>
    private void ToggleList(TextMarkerStyle style)
    {
        var list = CurrentList();
        Editor.BeginChange();
        try
        {
            if (list != null && IsNumbered(list.MarkerStyle) != IsNumbered(style))
                list.MarkerStyle = style;                                   // switch type
            else if (list != null)
            {
                if (IsNumbered(style)) EditingCommands.ToggleNumbering.Execute(null, Editor);
                else EditingCommands.ToggleBullets.Execute(null, Editor);  // remove
            }
            else
            {
                if (IsNumbered(style)) EditingCommands.ToggleNumbering.Execute(null, Editor);
                else EditingCommands.ToggleBullets.Execute(null, Editor);
                var created = CurrentList();
                if (created != null) created.MarkerStyle = style;
            }
        }
        finally { Editor.EndChange(); }
        UpdateFormattingUi();
        Editor.Focus();
    }

    private void SetListStyle(TextMarkerStyle style)
    {
        var list = CurrentList();
        if (list == null) { ToggleList(style); return; }
        list.MarkerStyle = style;
        UpdateFormattingUi();
        Editor.Focus();
    }

    private void BulletsDrop_Click(object sender, RoutedEventArgs e) => ShowListMenu((FrameworkElement)sender, new[]
    {
        ("●  Solid circle", TextMarkerStyle.Disc), ("○  Hollow circle", TextMarkerStyle.Circle),
        ("■  Solid square", TextMarkerStyle.Square), ("□  Hollow square", TextMarkerStyle.Box),
    });

    private void NumberingDrop_Click(object sender, RoutedEventArgs e) => ShowListMenu((FrameworkElement)sender, new[]
    {
        ("1, 2, 3", TextMarkerStyle.Decimal), ("a, b, c", TextMarkerStyle.LowerLatin), ("A, B, C", TextMarkerStyle.UpperLatin),
        ("i, ii, iii", TextMarkerStyle.LowerRoman), ("I, II, III", TextMarkerStyle.UpperRoman),
    });

    private void ShowListMenu(FrameworkElement anchor, (string, TextMarkerStyle)[] items)
    {
        var current = CurrentList()?.MarkerStyle;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (label, style) in items)
        {
            var mi = new MenuItem { Header = label, IsChecked = current == style };
            mi.Click += (_, _) => SetListStyle(style);
            menu.Items.Add(mi);
        }
        if (current != null)
        {
            menu.Items.Add(new Separator());
            var off = new MenuItem { Header = "None (remove list)" };
            off.Click += (_, _) => ToggleList(current.Value);
            menu.Items.Add(off);
        }
        menu.Closed += (_, _) => Editor.Focus();
        menu.IsOpen = true;
    }

    // ---- line spacing ----

    private double ParagraphFontSize(Paragraph p)
    {
        // Use the largest run in the paragraph so big headings don't get clipped.
        double max = p.FontSize;
        foreach (var i in p.Inlines) if (i.FontSize > max) max = i.FontSize;
        return max;
    }

    private double? GetLineSpacing(Paragraph p)
    {
        if (double.IsNaN(p.LineHeight)) return 1.0;
        double single = ParagraphFontSize(p) * 1.17;
        return Math.Round(p.LineHeight / single * 100) / 100;
    }

    private void SetLineSpacing(double factor)
    {
        Editor.BeginChange();
        try
        {
            foreach (var p in SelectedParagraphs())
            {
                if (Math.Abs(factor - 1.0) < 0.01)
                {
                    p.ClearValue(Block.LineHeightProperty);
                    p.ClearValue(Block.LineStackingStrategyProperty);
                }
                else
                {
                    p.LineStackingStrategy = LineStackingStrategy.MaxHeight;
                    p.LineHeight = ParagraphFontSize(p) * 1.17 * factor;
                }
            }
        }
        finally { Editor.EndChange(); }
        Editor.Focus();
    }

    private void SetSpaceAfter(double points)
    {
        Editor.BeginChange();
        try
        {
            foreach (var p in SelectedParagraphs())
            {
                var m = p.Margin;
                p.Margin = new Thickness(Safe(m.Left), Safe(m.Top), Safe(m.Right), DocumentService.PointsToDips(points));
            }
        }
        finally { Editor.EndChange(); }
        Editor.Focus();
    }

    private void LineSpacing_Click(object sender, RoutedEventArgs e)
    {
        var p = Editor.Selection.Start.Paragraph;
        double? cur = p != null ? GetLineSpacing(p) : null;
        bool spaceAfter = p != null && Safe(p.Margin.Bottom) > 1;
        var menu = new ContextMenu { PlacementTarget = LineSpacingBtn, Placement = PlacementMode.Bottom };
        foreach (var f in ParagraphDialog.Spacings)
        {
            var factor = f;
            var mi = new MenuItem { Header = f.ToString("0.0#", CultureInfo.CurrentCulture), IsChecked = cur.HasValue && Math.Abs(cur.Value - f) < 0.05 };
            mi.Click += (_, _) => SetLineSpacing(factor);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var after = new MenuItem { Header = "Add 10pt space after paragraphs", IsChecked = spaceAfter };
        after.Click += (_, _) => SetSpaceAfter(spaceAfter ? 0 : 10);
        menu.Items.Add(after);
        var more = new MenuItem { Header = "Paragraph settings…" };
        more.Click += (_, _) => ShowParagraphDialog();
        menu.Items.Add(more);
        menu.Closed += (_, _) => Editor.Focus();
        menu.IsOpen = true;
    }

    private void ParagraphDialog_Click(object sender, RoutedEventArgs e) => ShowParagraphDialog();

    private void ShowParagraphDialog()
    {
        var paras = SelectedParagraphs();
        if (paras.Count == 0) return;

        T? Same<T>(Func<Paragraph, T> get) where T : struct
        {
            var first = get(paras[0]);
            return paras.All(p => EqualityComparer<T>.Default.Equals(get(p), first)) ? first : null;
        }
        double R(double d) => Math.Round(Safe(d), 2);

        var v = new ParagraphValues
        {
            Left = Same(p => R(p.Margin.Left)),
            Right = Same(p => R(p.Margin.Right)),
            FirstLine = Same(p => R(p.TextIndent)),
            Before = Same(p => DocumentService.DipsToPoints(Safe(p.Margin.Top))),
            After = Same(p => DocumentService.DipsToPoints(Safe(p.Margin.Bottom))),
            LineSpacing = Same(p => GetLineSpacing(p) ?? 1.0),
            Alignment = Same(p => p.TextAlignment),
            KeepTogether = Same(p => p.KeepTogether),
            PageBreakBefore = Same(p => p.BreakPageBefore),
        };

        var d = new ParagraphDialog(v, S.Units);
        if (d.ShowDialog() != true || d.Result is not ParagraphValues r) { Editor.Focus(); return; }

        Editor.BeginChange();
        try
        {
            foreach (var p in paras)
            {
                var m = p.Margin;
                p.Margin = new Thickness(
                    r.Left ?? Safe(m.Left),
                    r.Before.HasValue ? DocumentService.PointsToDips(r.Before.Value) : Safe(m.Top),
                    r.Right ?? Safe(m.Right),
                    r.After.HasValue ? DocumentService.PointsToDips(r.After.Value) : Safe(m.Bottom));
                if (r.FirstLine.HasValue) p.TextIndent = r.FirstLine.Value;
                if (r.Alignment.HasValue) p.TextAlignment = r.Alignment.Value;
                if (r.KeepTogether.HasValue) p.KeepTogether = r.KeepTogether.Value;
                if (r.PageBreakBefore.HasValue) p.BreakPageBefore = r.PageBreakBefore.Value;
            }
        }
        finally { Editor.EndChange(); }
        if (r.LineSpacing.HasValue) SetLineSpacing(r.LineSpacing.Value);
        UpdateFormattingUi();
        UpdateRuler();
        Editor.Focus();
    }

    // ================================================================== reflect selection in the toolbar

    private void UpdateFormattingUi()
    {
        if (Editor?.Document == null) return;
        _updatingUi = true;
        try
        {
            var sel = Editor.Selection;

            var ff = sel.GetPropertyValue(TextElement.FontFamilyProperty);
            if (!FontFamilyBox.IsKeyboardFocusWithin)
            {
                FontFamilyBox.SelectedItem = null;
                FontFamilyBox.Text = ff is FontFamily f ? f.Source : "";
            }
            if (!FontSizeBox.IsKeyboardFocusWithin)
            {
                FontSizeBox.SelectedItem = null;
                var fs = sel.GetPropertyValue(TextElement.FontSizeProperty);
                FontSizeBox.Text = fs is double d ? DocumentService.DipsToPoints(d).ToString(CultureInfo.CurrentCulture) : "";
            }

            BoldBtn.IsChecked = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 600;
            ItalicBtn.IsChecked = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle st && st != FontStyles.Normal;
            UnderlineBtn.IsChecked = HasDecoration(TextDecorationLocation.Underline);
            StrikeBtn.IsChecked = HasDecoration(TextDecorationLocation.Strikethrough);
            var bl = sel.GetPropertyValue(Inline.BaselineAlignmentProperty);
            SubBtn.IsChecked = bl is BaselineAlignment b1 && b1 == BaselineAlignment.Subscript;
            SuperBtn.IsChecked = bl is BaselineAlignment b2 && b2 == BaselineAlignment.Superscript;

            var align = sel.GetPropertyValue(Paragraph.TextAlignmentProperty);
            AlignLeftBtn.IsChecked = align is TextAlignment a1 && a1 == TextAlignment.Left;
            AlignCenterBtn.IsChecked = align is TextAlignment a2 && a2 == TextAlignment.Center;
            AlignRightBtn.IsChecked = align is TextAlignment a3 && a3 == TextAlignment.Right;
            AlignJustifyBtn.IsChecked = align is TextAlignment a4 && a4 == TextAlignment.Justify;

            var list = CurrentList();
            BulletsBtn.IsChecked = list != null && !IsNumbered(list.MarkerStyle);
            NumberingBtn.IsChecked = list != null && IsNumbered(list.MarkerStyle);

            QuickUndo.IsEnabled = Editor.CanUndo;
            QuickRedo.IsEnabled = Editor.CanRedo;
        }
        finally { _updatingUi = false; }
    }
}
