using System.Text;
using System.Windows;
using System.Windows.Media;
using Markdig;
using D = System.Windows.Documents;
using MD = Markdig.Syntax;
using MDI = Markdig.Syntax.Inlines;
using MDT = Markdig.Extensions.Tables;

namespace Draftly.Services;

/// <summary>
/// Markdown (.md) support, like Windows 11 Notepad: headings, bold, italic, strikethrough, links,
/// bullet and numbered lists, quotes, code, horizontal lines and tables. Reading uses Markdig
/// (MIT licence); writing is Draftly's own. Things Markdown can't store (colours, fonts, sizes,
/// alignment, pictures) are dropped when saving, and Draftly warns first.
/// </summary>
public static class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()   // ~~strike~~, ~sub~, ^sup^, ++inserted++, ==marked==
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    private static readonly SolidColorBrush TableBorder = Frozen(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly SolidColorBrush MarkBrush = Frozen(Color.FromRgb(0xFF, 0xF2, 0x00));
    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static double Dip(double pt) => pt * 96.0 / 72.0;

    // ================================================================== read

    public static D.FlowDocument ToDocument(string markdown)
    {
        var doc = DocumentService.NewDocument();
        doc.Blocks.Clear();
        var md = Markdown.Parse(markdown ?? "", Pipeline);
        AddBlocks(md, doc.Blocks);
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new D.Paragraph { Margin = new Thickness(0) });
        return doc;
    }

    private record struct Fmt(bool Bold, bool Italic, bool Strike, bool Code, bool Sub, bool Sup, bool Underline, bool Mark);

    private static void AddBlocks(MD.ContainerBlock container, D.BlockCollection target)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case MD.HeadingBlock h:
                {
                    var p = Para(h.Inline);
                    ParagraphStyles.Apply(p, h.Level <= 1 ? ParaStyle.Heading1 : h.Level == 2 ? ParaStyle.Heading2 : ParaStyle.Heading3);
                    target.Add(p);
                    break;
                }
                case MD.ParagraphBlock pb:
                    target.Add(Para(pb.Inline, spaceAfter: true));
                    break;
                case MD.ListBlock lb:
                {
                    var list = new D.List
                    {
                        MarkerStyle = lb.IsOrdered ? System.Windows.TextMarkerStyle.Decimal : System.Windows.TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, Dip(6)),
                        Padding = new Thickness(24, 0, 0, 0),
                    };
                    if (lb.IsOrdered && int.TryParse(lb.OrderedStart, out var start) && start > 1) list.StartIndex = start;
                    foreach (var child in lb)
                    {
                        var item = new D.ListItem();
                        if (child is MD.ContainerBlock li) AddBlocks(li, item.Blocks);
                        foreach (var b in item.Blocks) if (b is D.Paragraph ip) ip.Margin = new Thickness(0);
                        if (item.Blocks.Count == 0) item.Blocks.Add(new D.Paragraph { Margin = new Thickness(0) });
                        list.ListItems.Add(item);
                    }
                    target.Add(list);
                    break;
                }
                case MD.QuoteBlock q:
                {
                    var before = target.LastBlock;
                    AddBlocks(q, target);
                    for (var b = before == null ? target.FirstBlock : before.NextBlock; b != null; b = b.NextBlock)
                        if (b is D.Paragraph qp) ParagraphStyles.Apply(qp, ParaStyle.Quote);
                    break;
                }
                case MD.CodeBlock cb:   // fenced or indented
                {
                    var p = new D.Paragraph();
                    var lines = cb.Lines.ToString().Replace("\r", "").Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) p.Inlines.Add(new D.LineBreak());
                        if (lines[i].Length > 0) p.Inlines.Add(new D.Run(lines[i]));
                    }
                    ParagraphStyles.Apply(p, ParaStyle.Code);
                    target.Add(p);
                    break;
                }
                case MD.ThematicBreakBlock:
                    target.Add(HorizontalLine());
                    break;
                case MD.HtmlBlock html:
                    target.Add(new D.Paragraph(new D.Run(html.Lines.ToString())) { Margin = new Thickness(0, 0, 0, Dip(6)) });
                    break;
                case MDT.Table t:
                    target.Add(TableFrom(t));
                    break;
                case MD.LinkReferenceDefinitionGroup:
                    break;
                case MD.LeafBlock leaf when leaf.Inline != null:
                    target.Add(Para(leaf.Inline, spaceAfter: true));
                    break;
                case MD.ContainerBlock inner:
                    AddBlocks(inner, target);
                    break;
            }
        }
    }

    public static D.Paragraph HorizontalLine() => new()
    {
        Margin = new Thickness(0, 6, 0, 10),
        BorderBrush = TableBorder,
        BorderThickness = new Thickness(0, 0, 0, 1),
        FontSize = 4,
    };

    private static D.Paragraph Para(MDI.ContainerInline inlines, bool spaceAfter = false)
    {
        var p = new D.Paragraph { Margin = new Thickness(0, 0, 0, spaceAfter ? Dip(6) : 0) };
        if (inlines != null) AddInlines(inlines, p.Inlines, default);
        return p;
    }

    private static D.Table TableFrom(MDT.Table t)
    {
        var table = new D.Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, Dip(6)) };
        int cols = 0;
        foreach (var r in t.OfType<MDT.TableRow>()) cols = Math.Max(cols, r.Count);
        for (int i = 0; i < Math.Max(1, cols); i++) table.Columns.Add(new D.TableColumn());
        var group = new D.TableRowGroup();
        table.RowGroups.Add(group);
        foreach (var r in t.OfType<MDT.TableRow>())
        {
            var row = new D.TableRow();
            foreach (var c in r.OfType<MDT.TableCell>())
            {
                var cell = new D.TableCell { BorderBrush = TableBorder, BorderThickness = new Thickness(0.75), Padding = new Thickness(6, 3, 6, 3) };
                AddBlocks(c, cell.Blocks);
                foreach (var b in cell.Blocks)
                    if (b is D.Paragraph cp) { cp.Margin = new Thickness(0); if (r.IsHeader) cp.FontWeight = FontWeights.Bold; }
                if (cell.Blocks.Count == 0) cell.Blocks.Add(new D.Paragraph { Margin = new Thickness(0) });
                row.Cells.Add(cell);
            }
            while (row.Cells.Count < cols)
                row.Cells.Add(new D.TableCell(new D.Paragraph { Margin = new Thickness(0) }) { BorderBrush = TableBorder, BorderThickness = new Thickness(0.75), Padding = new Thickness(6, 3, 6, 3) });
            group.Rows.Add(row);
        }
        return table;
    }

    private static void AddInlines(MDI.ContainerInline container, D.InlineCollection target, Fmt f)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case MDI.LiteralInline lit:
                    AddRun(target, lit.Content.ToString(), f);
                    break;
                case MDI.EmphasisInline em:
                {
                    var g = f;
                    switch (em.DelimiterChar)
                    {
                        case '*' or '_':
                            if (em.DelimiterCount >= 2) g.Bold = true; else g.Italic = true;
                            if (em.DelimiterCount >= 3) g.Italic = true;
                            break;
                        case '~': if (em.DelimiterCount >= 2) g.Strike = true; else g.Sub = true; break;
                        case '^': g.Sup = true; break;
                        case '+': g.Underline = true; break;
                        case '=': g.Mark = true; break;
                    }
                    AddInlines(em, target, g);
                    break;
                }
                case MDI.CodeInline code:
                    AddRun(target, code.Content, f with { Code = true });
                    break;
                case MDI.LinkInline link when link.IsImage:
                    AddRun(target, "[" + PlainText(link) + "]", f with { Italic = true });
                    break;
                case MDI.LinkInline link:
                {
                    var h = new D.Hyperlink();
                    if (TryUri(link.Url, out var uri)) h.NavigateUri = uri;
                    AddInlines(link, h.Inlines, f);
                    if (h.Inlines.Count == 0) h.Inlines.Add(new D.Run(link.Url ?? ""));
                    target.Add(h);
                    break;
                }
                case MDI.AutolinkInline auto:
                {
                    var h = new D.Hyperlink(new D.Run(auto.Url));
                    if (TryUri(auto.IsEmail ? "mailto:" + auto.Url : auto.Url, out var uri)) h.NavigateUri = uri;
                    target.Add(h);
                    break;
                }
                case MDI.LineBreakInline br:
                    if (br.IsHard) target.Add(new D.LineBreak());
                    else AddRun(target, " ", f);
                    break;
                case MDI.HtmlEntityInline ent:
                    AddRun(target, ent.Transcoded.ToString(), f);
                    break;
                case MDI.HtmlInline html:
                    if (html.Tag is "<br>" or "<br/>" or "<br />") target.Add(new D.LineBreak());
                    else AddRun(target, html.Tag, f);
                    break;
                case MDI.ContainerInline c:
                    AddInlines(c, target, f);
                    break;
            }
        }
    }

    private static void AddRun(D.InlineCollection target, string text, Fmt f)
    {
        if (string.IsNullOrEmpty(text)) return;
        var r = new D.Run(text);
        if (f.Bold) r.FontWeight = FontWeights.Bold;
        if (f.Italic) r.FontStyle = FontStyles.Italic;
        if (f.Strike || f.Underline) r.TextDecorations = DocumentService.Decorations(f.Underline, f.Strike);
        if (f.Code) r.FontFamily = ParagraphStyles.CodeFont;
        if (f.Sub) { r.BaselineAlignment = BaselineAlignment.Subscript; r.FontSize = Dip(8); }
        if (f.Sup) { r.BaselineAlignment = BaselineAlignment.Superscript; r.FontSize = Dip(8); }
        if (f.Mark) r.Background = MarkBrush;
        target.Add(r);
    }

    private static string PlainText(MDI.ContainerInline c)
    {
        var sb = new StringBuilder();
        foreach (var i in c)
        {
            if (i is MDI.LiteralInline l) sb.Append(l.Content.ToString());
            else if (i is MDI.CodeInline code) sb.Append(code.Content);
            else if (i is MDI.ContainerInline inner) sb.Append(PlainText(inner));
        }
        return sb.ToString();
    }

    public static bool TryUri(string url, out Uri uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) || Uri.TryCreate(url.Trim(), UriKind.Relative, out uri);
    }

    // ================================================================== write

    /// <summary>Markdown text for a document. Lines are joined with <paramref name="newLine"/>.</summary>
    public static string FromDocument(D.FlowDocument doc, string newLine = "\r\n")
    {
        var lines = new List<string>();
        WriteBlocks(doc.Blocks, lines, "");
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join(newLine, lines) + (lines.Count > 0 ? newLine : "");
    }

    private static void Blank(List<string> lines)
    {
        if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
    }

    private static void WriteBlocks(IEnumerable<D.Block> blocks, List<string> lines, string indent)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case D.Paragraph p:
                    WriteParagraph(p, lines, indent);
                    if (indent.Length == 0) Blank(lines);
                    break;
                case D.List list:
                    WriteList(list, lines, indent);
                    if (indent.Length == 0) Blank(lines);
                    break;
                case D.Table table:
                    WriteTable(table, lines, indent);
                    Blank(lines);
                    break;
                case D.Section section:
                    WriteBlocks(section.Blocks, lines, indent);
                    break;
                // BlockUIContainer (pictures): Markdown text can't hold them.
            }
        }
    }

    private static void WriteParagraph(D.Paragraph p, List<string> lines, string indent)
    {
        var plain = new D.TextRange(p.ContentStart, p.ContentEnd).Text;
        if (plain.Trim().Length == 0 && p.BorderThickness.Bottom > 0 && !p.Inlines.OfType<D.InlineUIContainer>().Any())
        {
            lines.Add(indent + "---");
            return;
        }
        var style = ParagraphStyles.Detect(p);
        switch (style)
        {
            case ParaStyle.Code:
            {
                lines.Add(indent + "```");
                foreach (var l in CodeText(p).Split('\n')) lines.Add(indent + l);
                lines.Add(indent + "```");
                return;
            }
            case ParaStyle.Title or ParaStyle.Heading1 or ParaStyle.Heading2 or ParaStyle.Heading3:
            {
                var text = InlineMarkdown(p.Inlines, ignoreBold: true).Replace("\n", " ");
                lines.Add(indent + new string('#', ParagraphStyles.HeadingLevel(style)) + " " + text);
                return;
            }
            case ParaStyle.Quote:
            {
                var text = InlineMarkdown(p.Inlines, ignoreItalic: true);
                foreach (var l in text.Split('\n')) lines.Add(indent + "> " + l);
                return;
            }
            default:
            {
                var text = InlineMarkdown(p.Inlines);
                var parts = text.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                    lines.Add(indent + (i == 0 ? EscapeLineStart(parts[i]) : parts[i]) + (i < parts.Length - 1 ? "\\" : ""));
                return;
            }
        }
    }

    private static string CodeText(D.Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var i in p.Inlines)
        {
            if (i is D.LineBreak) sb.Append('\n');
            else sb.Append(new D.TextRange(i.ContentStart, i.ContentEnd).Text);
        }
        return sb.ToString();
    }

    private static void WriteList(D.List list, List<string> lines, string indent)
    {
        bool ordered = list.MarkerStyle is System.Windows.TextMarkerStyle.Decimal or System.Windows.TextMarkerStyle.LowerLatin or
            System.Windows.TextMarkerStyle.UpperLatin or System.Windows.TextMarkerStyle.LowerRoman or System.Windows.TextMarkerStyle.UpperRoman;
        int n = Math.Max(1, list.StartIndex);
        foreach (var item in list.ListItems)
        {
            string marker = ordered ? $"{n++}." : "-";
            string childIndent = indent + new string(' ', marker.Length + 1);
            bool first = true;
            foreach (var b in item.Blocks)
            {
                if (first && b is D.Paragraph p)
                {
                    var tmp = new List<string>();
                    WriteParagraph(p, tmp, "");
                    for (int i = 0; i < tmp.Count; i++)
                        lines.Add(i == 0 ? indent + marker + " " + tmp[i] : childIndent + tmp[i]);
                }
                else if (b is D.List sub) WriteList(sub, lines, childIndent);
                else if (b is D.Paragraph more) WriteParagraph(more, lines, childIndent);
                first = false;
            }
            if (first) lines.Add(indent + marker);
        }
    }

    private static void WriteTable(D.Table table, List<string> lines, string indent)
    {
        var rows = table.RowGroups.SelectMany(g => g.Rows).ToList();
        if (rows.Count == 0) return;
        int cols = Math.Max(1, rows.Max(r => r.Cells.Count));
        string Cell(D.TableCell c) => string.Join("<br>", c.Blocks.OfType<D.Paragraph>()
            .Select(p => InlineMarkdown(p.Inlines, ignoreBold: rows.IndexOf((D.TableRow)c.Parent) == 0).Replace("\n", "<br>").Replace("|", "\\|")));
        string Row(D.TableRow r) => indent + "| " + string.Join(" | ", Enumerable.Range(0, cols).Select(i => i < r.Cells.Count ? Cell(r.Cells[i]) : "")) + " |";
        lines.Add(Row(rows[0]));
        lines.Add(indent + "|" + string.Concat(Enumerable.Repeat(" --- |", cols)));
        foreach (var r in rows.Skip(1)) lines.Add(Row(r));
    }

    // ---- inline formatting

    private sealed record Seg(string Text, bool Bold, bool Italic, bool Strike, bool Code, bool Sub, bool Sup, bool Underline, bool Mark, string Url)
    {
        public bool SameFormat(Seg o) => Bold == o.Bold && Italic == o.Italic && Strike == o.Strike && Code == o.Code &&
                                         Sub == o.Sub && Sup == o.Sup && Underline == o.Underline && Mark == o.Mark;
    }

    private static string InlineMarkdown(D.InlineCollection inlines, bool ignoreBold = false, bool ignoreItalic = false)
    {
        var segs = new List<Seg>();
        Collect(inlines, null, segs, ignoreBold, ignoreItalic);
        var sb = new StringBuilder();
        int i = 0;
        while (i < segs.Count)
        {
            // Consecutive segments with the same link become one [text](url)
            var url = segs[i].Url;
            int j = i;
            while (j < segs.Count && segs[j].Url == url && segs[j].Text != "\n") j++;
            if (j == i) { sb.Append('\n'); i++; continue; }
            var inner = Formatted(segs.GetRange(i, j - i));
            sb.Append(url != null ? $"[{inner}]({url.Replace(" ", "%20").Replace(")", "%29")})" : inner);
            i = j;
        }
        return sb.ToString();
    }

    private static string Formatted(List<Seg> segs)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < segs.Count)
        {
            int j = i + 1;
            while (j < segs.Count && segs[j].SameFormat(segs[i])) j++;
            var s = segs[i];
            var text = string.Concat(segs.GetRange(i, j - i).Select(x => x.Text));
            i = j;
            if (s.Code) { sb.Append(text.Contains('`') ? "`` " + text + " ``" : "`" + text + "`"); continue; }
            text = Escape(text);
            // Markdown emphasis can't start or end with a space, so keep spaces outside the markers.
            string lead = text[..(text.Length - text.TrimStart().Length)];
            string trail = text[text.TrimEnd().Length..];
            string core = text.Trim();
            if (core.Length == 0) { sb.Append(text); continue; }
            string open = "", close = "";
            void Wrap(bool on, string m) { if (on) { open += m; close = m + close; } }
            Wrap(s.Mark, "==");
            Wrap(s.Underline, "++");
            Wrap(s.Strike, "~~");
            Wrap(s.Sub, "~");
            Wrap(s.Sup, "^");
            Wrap(s.Bold, "**");
            Wrap(s.Italic, "*");
            sb.Append(lead).Append(open).Append(core).Append(close).Append(trail);
        }
        return sb.ToString();
    }

    private static void Collect(D.InlineCollection inlines, string url, List<Seg> segs, bool ignoreBold, bool ignoreItalic)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case D.Run r when r.Text.Length > 0:
                    segs.Add(new Seg(r.Text,
                        Bold: !ignoreBold && r.FontWeight.ToOpenTypeWeight() >= 600,
                        Italic: !ignoreItalic && r.FontStyle != FontStyles.Normal,
                        Strike: HasDecoration(r, TextDecorationLocation.Strikethrough),
                        Code: ParagraphStyles.IsCodeFont(r.FontFamily),
                        Sub: Baseline(r) == BaselineAlignment.Subscript,
                        Sup: Baseline(r) == BaselineAlignment.Superscript,
                        Underline: url == null && HasDecoration(r, TextDecorationLocation.Underline),
                        Mark: r.Background is SolidColorBrush,
                        Url: url));
                    break;
                case D.LineBreak:
                    segs.Add(new Seg("\n", false, false, false, false, false, false, false, false, url));
                    break;
                case D.Hyperlink h:
                    Collect(h.Inlines, h.NavigateUri?.OriginalString ?? url, segs, ignoreBold, ignoreItalic);
                    break;
                case D.Span span:
                    Collect(span.Inlines, url, segs, ignoreBold, ignoreItalic);
                    break;
            }
        }
    }

    private static bool HasDecoration(D.Inline i, TextDecorationLocation loc)
    {
        for (DependencyObject d = i; d is D.Inline x; d = x.Parent)
            if (x.TextDecorations?.Any(t => t.Location == loc) == true) return true;
        return false;
    }

    private static BaselineAlignment Baseline(D.Inline i)
    {
        for (DependencyObject d = i; d is D.Inline x; d = x.Parent)
            if (x.BaselineAlignment is BaselineAlignment.Subscript or BaselineAlignment.Superscript) return x.BaselineAlignment;
        return BaselineAlignment.Baseline;
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (NeedsEscape(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool NeedsEscape(char c) => c is '\\' or '*' or '_' or '`' or '[' or ']' or '~' or '^';

    /// <summary>Stops a normal paragraph that starts like "# " or "- " or "1. " from turning into a heading or list.</summary>
    private static string EscapeLineStart(string line)
    {
        if (line.Length == 0) return line;
        if (line[0] is '#' or '>' or '|') return "\\" + line;
        if ((line.StartsWith("- ") || line.StartsWith("+ ") || line.StartsWith("---"))) return "\\" + line;
        int k = 0;
        while (k < line.Length && char.IsDigit(line[k])) k++;
        if (k > 0 && k < line.Length && (line[k] == '.' || line[k] == ')') && (k + 1 == line.Length || line[k + 1] == ' '))
            return line[..k] + "\\" + line[k..];
        return line;
    }

    /// <summary>True if saving as Markdown would drop something the user can see.</summary>
    public static bool WouldLoseFormatting(D.FlowDocument doc)
    {
        foreach (var el in DocumentService.Walk(doc.Blocks))
        {
            if (el is D.InlineUIContainer or D.BlockUIContainer) return true;
            if (el is D.Paragraph p && p.TextAlignment is TextAlignment.Center or TextAlignment.Right) return true;
            if (el is D.Run r && r.Text.Trim().Length > 0)
            {
                bool inLink = false;
                for (DependencyObject d = r; d is D.Inline x; d = x.Parent) if (x is D.Hyperlink) inLink = true;
                if (!inLink && r.Foreground is SolidColorBrush fg && r.ReadLocalValue(D.TextElement.ForegroundProperty) != DependencyProperty.UnsetValue &&
                    !(fg.Color.R < 0x10 && fg.Color.G < 0x10 && fg.Color.B < 0x10)) return true;
            }
        }
        return false;
    }
}
