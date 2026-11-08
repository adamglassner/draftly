using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using D = System.Windows.Documents;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Draftly.Services;

/// <summary>
/// Imports .docx files using Microsoft's Open XML SDK. Text, character formatting, paragraph
/// formatting, lists, tables and inline pictures are converted. Layout-only features (headers,
/// footers, text boxes, columns, comments, footnotes) are not, and the user is told so.
/// Draftly never writes .docx.
/// </summary>
public static class DocxImporter
{
    private sealed class RunFmt
    {
        public bool? Bold, Italic, Strike, Underline;
        public double? SizeHalfPts;
        public string Font, Color, Highlight, Shading, VertAlign;

        public RunFmt Merge(RunFmt over)
        {
            if (over == null) return this;
            return new RunFmt
            {
                Bold = over.Bold ?? Bold,
                Italic = over.Italic ?? Italic,
                Strike = over.Strike ?? Strike,
                Underline = over.Underline ?? Underline,
                SizeHalfPts = over.SizeHalfPts ?? SizeHalfPts,
                Font = over.Font ?? Font,
                Color = over.Color ?? Color,
                Highlight = over.Highlight ?? Highlight,
                Shading = over.Shading ?? Shading,
                VertAlign = over.VertAlign ?? VertAlign,
            };
        }
    }

    private sealed class ParaFmt
    {
        public string Align;
        public double? Left, Right, FirstLine, Hanging, Before, After, Line;
        public string LineRule;
        public int? NumId, NumLevel;

        public ParaFmt Merge(ParaFmt o)
        {
            if (o == null) return this;
            return new ParaFmt
            {
                Align = o.Align ?? Align,
                Left = o.Left ?? Left,
                Right = o.Right ?? Right,
                FirstLine = o.FirstLine ?? FirstLine,
                Hanging = o.Hanging ?? Hanging,
                Before = o.Before ?? Before,
                After = o.After ?? After,
                Line = o.Line ?? Line,
                LineRule = o.LineRule ?? LineRule,
                NumId = o.NumId ?? NumId,
                NumLevel = o.NumLevel ?? NumLevel,
            };
        }
    }

    private sealed class Ctx
    {
        public MainDocumentPart Main;
        public Dictionary<string, W.Style> Styles = new();
        public RunFmt DefaultRun = new();
        public ParaFmt DefaultPara = new();
        public string MinorFont, MajorFont;
        public HashSet<string> Skipped = new();
    }

    public static D.FlowDocument Import(string path, out string warning)
    {
        using var pkg = WordprocessingDocument.Open(path, false);
        var main = pkg.MainDocumentPart ?? throw new InvalidDataException("This Word file has no document body.");
        var body = main.Document?.Body ?? throw new InvalidDataException("This Word file has no document body.");

        var ctx = new Ctx { Main = main };
        LoadThemeFonts(ctx);
        LoadStyles(ctx);

        var doc = DocumentService.NewDocument();
        doc.Blocks.Clear();

        if (main.HeaderParts.Any() || main.FooterParts.Any()) ctx.Skipped.Add("headers and footers");
        if (main.FootnotesPart != null && main.FootnotesPart.Footnotes?.Elements<W.Footnote>().Count() > 2) ctx.Skipped.Add("footnotes");
        if (main.WordprocessingCommentsPart != null) ctx.Skipped.Add("comments");

        AddBlocks(ctx, body.ChildElements, doc.Blocks);
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new D.Paragraph { Margin = new Thickness(0) });

        warning = "Imported from a Word document. Draftly saves as RTF or plain text, so your original .docx won't be changed.";
        if (ctx.Skipped.Count > 0)
            warning += " Not imported: " + string.Join(", ", ctx.Skipped) + ".";
        return doc;
    }

    // ------------------------------------------------------------------ styles

    private static void LoadThemeFonts(Ctx ctx)
    {
        try
        {
            var fs = ctx.Main.ThemePart?.Theme?.ThemeElements?.FontScheme;
            ctx.MinorFont = fs?.MinorFont?.LatinFont?.Typeface?.Value;
            ctx.MajorFont = fs?.MajorFont?.LatinFont?.Typeface?.Value;
        }
        catch { }
    }

    private static void LoadStyles(Ctx ctx)
    {
        var styles = ctx.Main.StyleDefinitionsPart?.Styles;
        if (styles == null) { ctx.DefaultRun.Font = ctx.MinorFont ?? "Calibri"; ctx.DefaultRun.SizeHalfPts = 22; return; }

        var defaults = styles.DocDefaults;
        ctx.DefaultRun = ReadRun(ctx, defaults?.RunPropertiesDefault?.RunPropertiesBaseStyle) ?? new RunFmt();
        ctx.DefaultRun.Font ??= ctx.MinorFont ?? "Calibri";
        ctx.DefaultRun.SizeHalfPts ??= 20;
        ctx.DefaultPara = ReadPara(defaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle) ?? new ParaFmt();

        foreach (var s in styles.Elements<W.Style>())
            if (s.StyleId?.Value is string id) ctx.Styles[id] = s;

        // The "Normal" paragraph style applies when a paragraph names no style.
        if (ctx.Styles.ContainsKey("Normal"))
        {
            ctx.DefaultRun = ctx.DefaultRun.Merge(StyleRun(ctx, "Normal", 0));
            ctx.DefaultPara = ctx.DefaultPara.Merge(StylePara(ctx, "Normal", 0));
        }
    }

    private static RunFmt StyleRun(Ctx ctx, string id, int depth)
    {
        if (id == null || depth > 10 || !ctx.Styles.TryGetValue(id, out var s)) return null;
        var parent = StyleRun(ctx, s.BasedOn?.Val?.Value, depth + 1) ?? new RunFmt();
        return parent.Merge(ReadRun(ctx, s.StyleRunProperties));
    }

    private static ParaFmt StylePara(Ctx ctx, string id, int depth)
    {
        if (id == null || depth > 10 || !ctx.Styles.TryGetValue(id, out var s)) return null;
        var parent = StylePara(ctx, s.BasedOn?.Val?.Value, depth + 1) ?? new ParaFmt();
        return parent.Merge(ReadPara(s.StyleParagraphProperties));
    }

    private static bool? OnOff(OpenXmlElement el)
    {
        if (el == null) return null;
        var val = el.GetAttributes().FirstOrDefault(a => a.LocalName == "val").Value;
        if (string.IsNullOrEmpty(val)) return true;
        return !(val == "0" || val == "false" || val == "off" || val == "none");
    }

    private static RunFmt ReadRun(Ctx ctx, OpenXmlCompositeElement rp)
    {
        if (rp == null) return null;
        var f = new RunFmt
        {
            Bold = OnOff(rp.GetFirstChild<W.Bold>()),
            Italic = OnOff(rp.GetFirstChild<W.Italic>()),
            Strike = OnOff(rp.GetFirstChild<W.Strike>()) ?? OnOff(rp.GetFirstChild<W.DoubleStrike>()),
        };
        var u = rp.GetFirstChild<W.Underline>();
        if (u != null) f.Underline = u.Val == null || u.Val.InnerText != "none";
        var sz = rp.GetFirstChild<W.FontSize>();
        if (sz?.Val?.Value is string szs && double.TryParse(szs, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var half)) f.SizeHalfPts = half;
        var fonts = rp.GetFirstChild<W.RunFonts>();
        if (fonts != null)
        {
            f.Font = fonts.Ascii?.Value ?? fonts.HighAnsi?.Value;
            if (f.Font == null && fonts.AsciiTheme != null)
                f.Font = fonts.AsciiTheme.InnerText.StartsWith("major") ? ctx.MajorFont : ctx.MinorFont;
        }
        var color = rp.GetFirstChild<W.Color>();
        if (color?.Val?.Value is string c) f.Color = c;
        var hl = rp.GetFirstChild<W.Highlight>();
        if (hl?.Val != null) f.Highlight = hl.Val.InnerText;
        var shd = rp.GetFirstChild<W.Shading>();
        if (shd?.Fill?.Value is string fill && fill != "auto") f.Shading = fill;
        var va = rp.GetFirstChild<W.VerticalTextAlignment>();
        if (va?.Val != null) f.VertAlign = va.Val.InnerText;
        return f;
    }

    private static double? Twips(StringValue v)
    {
        if (v?.Value == null) return null;
        return double.TryParse(v.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    private static ParaFmt ReadPara(OpenXmlCompositeElement pp)
    {
        if (pp == null) return null;
        var f = new ParaFmt();
        var jc = pp.GetFirstChild<W.Justification>();
        if (jc?.Val != null) f.Align = jc.Val.InnerText;
        var ind = pp.GetFirstChild<W.Indentation>();
        if (ind != null)
        {
            f.Left = Twips(ind.Left) ?? Twips(ind.Start);
            f.Right = Twips(ind.Right) ?? Twips(ind.End);
            f.FirstLine = Twips(ind.FirstLine);
            f.Hanging = Twips(ind.Hanging);
        }
        var sp = pp.GetFirstChild<W.SpacingBetweenLines>();
        if (sp != null)
        {
            f.Before = Twips(sp.Before);
            f.After = Twips(sp.After);
            f.Line = Twips(sp.Line);
            f.LineRule = sp.LineRule?.InnerText;
        }
        var num = pp.GetFirstChild<W.NumberingProperties>();
        if (num != null)
        {
            if (num.NumberingId?.Val?.Value is int id) f.NumId = id;
            if (num.NumberingLevelReference?.Val?.Value is int lvl) f.NumLevel = lvl;
        }
        return f;
    }

    // ------------------------------------------------------------------ blocks

    private static void AddBlocks(Ctx ctx, IEnumerable<OpenXmlElement> elements, D.BlockCollection target)
    {
        D.List currentList = null;
        int currentNumId = -1, currentLevel = -1;

        foreach (var el in elements)
        {
            switch (el)
            {
                case W.Paragraph p:
                {
                    var (para, pf) = ConvertParagraph(ctx, p);
                    int numId = pf.NumId ?? 0;
                    if (numId > 0)
                    {
                        int lvl = pf.NumLevel ?? 0;
                        if (currentList == null || numId != currentNumId || lvl != currentLevel)
                        {
                            currentList = new D.List
                            {
                                MarkerStyle = MarkerFor(ctx, numId, lvl),
                                Margin = new Thickness(24 * lvl, 0, 0, 0),
                                Padding = new Thickness(24, 0, 0, 0),
                            };
                            target.Add(currentList);
                            currentNumId = numId; currentLevel = lvl;
                        }
                        para.TextIndent = 0;
                        para.ClearValue(D.Block.MarginProperty);
                        para.Margin = new Thickness(0, para.Margin.Top, 0, para.Margin.Bottom);
                        currentList.ListItems.Add(new D.ListItem(para));
                    }
                    else
                    {
                        currentList = null; currentNumId = -1;
                        target.Add(para);
                    }
                    break;
                }
                case W.Table t:
                    currentList = null; currentNumId = -1;
                    target.Add(ConvertTable(ctx, t));
                    break;
                case W.SdtBlock sdt:
                    var content = sdt.SdtContentBlock;
                    if (content != null) AddBlocks(ctx, content.ChildElements, target);
                    break;
                case W.SectionProperties sp:
                    var cols = sp.GetFirstChild<W.Columns>();
                    if (cols?.ColumnCount?.Value > 1) ctx.Skipped.Add("multiple columns");
                    break;
            }
        }
    }

    private static TextMarkerStyle MarkerFor(Ctx ctx, int numId, int level)
    {
        try
        {
            var numbering = ctx.Main.NumberingDefinitionsPart?.Numbering;
            var inst = numbering?.Elements<W.NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var absId = inst?.AbstractNumId?.Val?.Value;
            var abs = numbering?.Elements<W.AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == absId);
            var lvl = abs?.Elements<W.Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
            var fmt = lvl?.NumberingFormat?.Val?.InnerText;
            return fmt switch
            {
                "decimal" or "decimalZero" => TextMarkerStyle.Decimal,
                "lowerLetter" => TextMarkerStyle.LowerLatin,
                "upperLetter" => TextMarkerStyle.UpperLatin,
                "lowerRoman" => TextMarkerStyle.LowerRoman,
                "upperRoman" => TextMarkerStyle.UpperRoman,
                _ => level % 3 == 1 ? TextMarkerStyle.Circle : level % 3 == 2 ? TextMarkerStyle.Square : TextMarkerStyle.Disc,
            };
        }
        catch { return TextMarkerStyle.Disc; }
    }

    private static (D.Paragraph, ParaFmt) ConvertParagraph(Ctx ctx, W.Paragraph p)
    {
        var pPr = p.ParagraphProperties;
        string styleId = pPr?.ParagraphStyleId?.Val?.Value;

        var pf = ctx.DefaultPara.Merge(StylePara(ctx, styleId, 0)).Merge(ReadPara(pPr));
        var baseRun = ctx.DefaultRun.Merge(StyleRun(ctx, styleId, 0));

        var para = new D.Paragraph();
        ApplyPara(para, pf, baseRun);

        if (p.Descendants<W.TextBoxContent>().Any()) ctx.Skipped.Add("text boxes");

        AddInlines(ctx, p.ChildElements, para.Inlines, baseRun);
        return (para, pf);
    }

    private static void ApplyPara(D.Paragraph para, ParaFmt pf, RunFmt baseRun)
    {
        const double tw = 96.0 / 1440.0; // twips → DIPs
        para.TextAlignment = pf.Align switch
        {
            "center" => TextAlignment.Center,
            "right" or "end" => TextAlignment.Right,
            "both" or "distribute" => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
        double left = (pf.Left ?? 0) * tw, right = (pf.Right ?? 0) * tw;
        double before = (pf.Before ?? 0) * tw, after = (pf.After ?? 0) * tw;
        para.Margin = new Thickness(Math.Max(0, left), before, Math.Max(0, right), after);
        if (pf.Hanging is double h) para.TextIndent = -h * tw;
        else if (pf.FirstLine is double fl) para.TextIndent = fl * tw;

        if (pf.Line is double line && line > 0)
        {
            double fontDips = (baseRun.SizeHalfPts ?? 22) / 2.0 * 96.0 / 72.0;
            if (pf.LineRule == "exact" || pf.LineRule == "atLeast")
            {
                para.LineHeight = line * tw;
                para.LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight;
            }
            else if (Math.Abs(line - 240) > 1)
            {
                para.LineHeight = fontDips * 1.17 * (line / 240.0);
                para.LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight;
            }
        }
        ApplyRun(para, baseRun);
    }

    private static void AddInlines(Ctx ctx, IEnumerable<OpenXmlElement> elements, D.InlineCollection target, RunFmt baseRun)
    {
        foreach (var el in elements)
        {
            switch (el)
            {
                case W.Run r:
                    AddRun(ctx, r, target, baseRun);
                    break;
                case W.Hyperlink hl:
                    AddInlines(ctx, hl.ChildElements, target, baseRun);
                    break;
                case W.SimpleField sf:
                    AddInlines(ctx, sf.ChildElements, target, baseRun);
                    break;
                case W.InsertedRun ins:
                    AddInlines(ctx, ins.ChildElements, target, baseRun);
                    break;
                case W.SdtRun sdt:
                    if (sdt.SdtContentRun != null) AddInlines(ctx, sdt.SdtContentRun.ChildElements, target, baseRun);
                    break;
                case W.SmartTagRun st:
                    AddInlines(ctx, st.ChildElements, target, baseRun);
                    break;
            }
        }
    }

    private static void AddRun(Ctx ctx, W.Run r, D.InlineCollection target, RunFmt baseRun)
    {
        var rPr = r.RunProperties;
        string rStyle = rPr?.RunStyle?.Val?.Value;
        var fmt = baseRun.Merge(StyleRun(ctx, rStyle, 0)).Merge(ReadRun(ctx, rPr));

        var sb = new System.Text.StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            var run = new D.Run(sb.ToString());
            ApplyRun(run, fmt);
            target.Add(run);
            sb.Clear();
        }

        foreach (var child in r.ChildElements)
        {
            switch (child)
            {
                case W.Text t: sb.Append(t.Text); break;
                case W.TabChar: sb.Append('\t'); break;
                case W.NoBreakHyphen: sb.Append('‑'); break;
                case W.SoftHyphen: sb.Append('­'); break;
                case W.CarriageReturn:
                case W.Break:
                    Flush();
                    target.Add(new D.LineBreak());
                    break;
                case W.Drawing drawing:
                    Flush();
                    var img = ConvertDrawing(ctx, drawing);
                    if (img != null) target.Add(img);
                    break;
                case W.Picture:
                    ctx.Skipped.Add("legacy (VML) pictures");
                    break;
                case W.FieldCode:
                case W.DeletedText:
                    break;
            }
        }
        Flush();
    }

    private static D.InlineUIContainer ConvertDrawing(Ctx ctx, W.Drawing drawing)
    {
        try
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            var embed = blip?.Embed?.Value;
            if (embed == null) { ctx.Skipped.Add("charts and shapes"); return null; }
            if (ctx.Main.GetPartById(embed) is not ImagePart part) return null;

            var ms = new MemoryStream();
            using (var s = part.GetStream()) s.CopyTo(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
            double w = extent?.Cx?.Value is long cx ? cx / 9525.0 : bmp.Width;
            double h = extent?.Cy?.Value is long cy ? cy / 9525.0 : bmp.Height;

            if (drawing.Anchor != null) ctx.Skipped.Add("text wrapping around pictures (pictures were placed inline)");

            var image = new System.Windows.Controls.Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill };
            return new D.InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Bottom };
        }
        catch { return null; }
    }

    private static D.Table ConvertTable(Ctx ctx, W.Table t)
    {
        var table = new D.Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 8) };
        var rows = t.Elements<W.TableRow>().ToList();
        int cols = rows.Count == 0 ? 0 : rows.Max(r => r.Elements<W.TableCell>().Sum(c =>
            c.TableCellProperties?.GridSpan?.Val?.Value ?? 1));
        for (int i = 0; i < cols; i++) table.Columns.Add(new D.TableColumn());

        var group = new D.TableRowGroup();
        table.RowGroups.Add(group);
        var border = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        border.Freeze();

        foreach (var r in rows)
        {
            var row = new D.TableRow();
            foreach (var c in r.Elements<W.TableCell>())
            {
                var cell = new D.TableCell
                {
                    BorderBrush = border,
                    BorderThickness = new Thickness(0.75),
                    Padding = new Thickness(6, 3, 6, 3),
                    ColumnSpan = c.TableCellProperties?.GridSpan?.Val?.Value ?? 1,
                };
                AddBlocks(ctx, c.ChildElements, cell.Blocks);
                if (cell.Blocks.Count == 0) cell.Blocks.Add(new D.Paragraph());
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        return table;
    }

    // ------------------------------------------------------------------ run formatting

    private static void ApplyRun(D.TextElement el, RunFmt f)
    {
        if (f.Font != null) el.FontFamily = new FontFamily(f.Font);
        if (f.SizeHalfPts is double hp) el.FontSize = hp / 2.0 * 96.0 / 72.0;
        if (f.Bold == true) el.FontWeight = FontWeights.Bold;
        if (f.Italic == true) el.FontStyle = FontStyles.Italic;

        if (el is D.Inline inl)
        {
            var deco = new TextDecorationCollection();
            if (f.Underline == true) deco.Add(TextDecorations.Underline[0]);
            if (f.Strike == true) deco.Add(TextDecorations.Strikethrough[0]);
            if (deco.Count > 0) inl.TextDecorations = deco;

            if (f.VertAlign == "superscript") { inl.BaselineAlignment = BaselineAlignment.Superscript; el.FontSize *= 0.7; }
            else if (f.VertAlign == "subscript") { inl.BaselineAlignment = BaselineAlignment.Subscript; el.FontSize *= 0.7; }
        }

        if (f.Color != null && f.Color != "auto" && TryHex(f.Color, out var c) && !(c.R < 16 && c.G < 16 && c.B < 16))
            el.Foreground = new SolidColorBrush(c);

        if (el is D.Inline)
        {
            if (f.Highlight != null && f.Highlight != "none" && HighlightColor(f.Highlight) is Color hc)
                el.Background = new SolidColorBrush(hc);
            else if (f.Shading != null && TryHex(f.Shading, out var sc))
                el.Background = new SolidColorBrush(sc);
        }
    }

    private static bool TryHex(string hex, out Color c)
    {
        c = Colors.Black;
        if (hex == null || hex.Length != 6) return false;
        try { c = (Color)ColorConverter.ConvertFromString("#" + hex); return true; }
        catch { return false; }
    }

    private static Color? HighlightColor(string name) => name switch
    {
        "yellow" => Color.FromRgb(255, 255, 0),
        "green" => Color.FromRgb(0, 255, 0),
        "cyan" => Color.FromRgb(0, 255, 255),
        "magenta" => Color.FromRgb(255, 0, 255),
        "blue" => Color.FromRgb(0, 0, 255),
        "red" => Color.FromRgb(255, 0, 0),
        "darkBlue" => Color.FromRgb(0, 0, 128),
        "darkCyan" => Color.FromRgb(0, 128, 128),
        "darkGreen" => Color.FromRgb(0, 128, 0),
        "darkMagenta" => Color.FromRgb(128, 0, 128),
        "darkRed" => Color.FromRgb(128, 0, 0),
        "darkYellow" => Color.FromRgb(128, 128, 0),
        "darkGray" => Color.FromRgb(128, 128, 128),
        "lightGray" => Color.FromRgb(192, 192, 192),
        "black" => Color.FromRgb(0, 0, 0),
        "white" => Color.FromRgb(255, 255, 255),
        _ => null,
    };
}
