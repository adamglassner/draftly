using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace Draftly.Services;

/// <summary>
/// WPF's RTF writer and reader ignore subscript and superscript (RTF \sub, \super, \dn, \up), and
/// WPF's reader ignores paragraph line spacing (\sl, \slmult).
/// This fills the gap: on save, sub/superscript runs are tagged in a scratch copy and the tags are
/// turned into \sub / \super; on open or paste, those RTF codes are turned into tags that are then
/// applied as BaselineAlignment. The result reads and writes the same as WordPad and Word.
/// </summary>
public static class RtfBaseline
{
    private const string SubStart = "@@DSUB<@@", SubEnd = "@@DSUB>@@", SupStart = "@@DSUP<@@", SupEnd = "@@DSUP>@@";
    private const string MarkerPrefix = "@@DSU";
    private const string SpacingPrefix = "@@DSL";   // @@DSL<twips>,<mult>@@ : paragraph line spacing (\\sl, \\slmult)
    private const int MarkerLength = 9;

    // ------------------------------------------------------------------ save

    /// <summary>RTF for a range, including sub/superscript. Call inside DocumentService.WithBlackText.</summary>
    public static string ToRtf(TextRange range, FlowDocument source)
    {
        if (!HasBaselineText(source))
            return SaveRange(range);

        // Work on a scratch copy so the user's document and undo history are untouched.
        var temp = new FlowDocument
        {
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            Foreground = source.Foreground,
            FlowDirection = source.FlowDirection,
            Language = source.Language,
        };
        using (var pkg = new MemoryStream())
        {
            range.Save(pkg, DataFormats.XamlPackage);
            pkg.Position = 0;
            new TextRange(temp.ContentStart, temp.ContentEnd).Load(pkg, DataFormats.XamlPackage);
        }

        foreach (var run in DocumentService.Walk(temp.Blocks).OfType<Run>().ToList())
        {
            if (run.Text.Length == 0) continue;
            switch (EffectiveBaseline(run))
            {
                case BaselineAlignment.Subscript: run.Text = SubStart + run.Text + SubEnd; break;
                case BaselineAlignment.Superscript: run.Text = SupStart + run.Text + SupEnd; break;
            }
        }

        return SaveRange(new TextRange(temp.ContentStart, temp.ContentEnd))
            .Replace(SubStart, "\\sub ").Replace(SupStart, "\\super ")
            .Replace(SubEnd, "\\nosupersub ").Replace(SupEnd, "\\nosupersub ");
    }

    private static string SaveRange(TextRange range)
    {
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        // RTF is 7-bit text; Latin-1 maps every byte to one char and back unchanged.
        return Encoding.Latin1.GetString(ms.ToArray());
    }

    private static bool HasBaselineText(FlowDocument doc) =>
        DocumentService.Walk(doc.Blocks).OfType<Inline>().Any(i =>
            i.BaselineAlignment is BaselineAlignment.Subscript or BaselineAlignment.Superscript);

    private static BaselineAlignment EffectiveBaseline(Inline inline)
    {
        for (DependencyObject d = inline; d is Inline i; d = i.Parent)
            if (i.BaselineAlignment is BaselineAlignment.Subscript or BaselineAlignment.Superscript)
                return i.BaselineAlignment;
        return BaselineAlignment.Baseline;
    }

    // ------------------------------------------------------------------ open / paste

    /// <summary>Loads RTF text into a range, keeping sub/superscript.</summary>
    public static void Load(TextRange target, string rtf, FlowDocument document)
    {
        var processed = PrepareForLoad(rtf);
        using (var ms = new MemoryStream(Encoding.Latin1.GetBytes(processed)))
            target.Load(ms, DataFormats.Rtf);
        if (!ReferenceEquals(processed, rtf)) ApplyMarkers(document);
    }

    private static readonly HashSet<string> Destinations = new(StringComparer.Ordinal)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "shppict", "nonshppict", "pntext", "pntxta", "pntxtb", "pn",
        "listtable", "listoverridetable", "rsidtbl", "generator", "header", "footer", "headerl", "headerr", "headerf",
        "footerl", "footerr", "footerf", "footnote", "fldinst", "themedata", "colorschememapping", "latentstyles",
        "datastore", "xmlnstbl", "mmathPr", "object", "objdata", "listtext", "author", "title", "operator",
    };

    /// <summary>Replaces \sub, \super, \dn, \up and \nosupersub in body text with text tags.</summary>
    public static string PrepareForLoad(string rtf)
    {
        if (rtf.IndexOf("\\sub", StringComparison.Ordinal) < 0 && rtf.IndexOf("\\super", StringComparison.Ordinal) < 0 &&
            rtf.IndexOf("\\dn", StringComparison.Ordinal) < 0 && rtf.IndexOf("\\up", StringComparison.Ordinal) < 0 &&
            rtf.IndexOf("\\sl", StringComparison.Ordinal) < 0)
            return rtf;

        var sb = new StringBuilder(rtf.Length + 256);
        var stack = new Stack<(int State, bool Dest, string Spacing)>();
        int state = 0;          // 0 normal, 1 subscript, 2 superscript
        string spacing = null;  // current paragraph line spacing as "twips,mult", or null for single
        bool carry = false;     // a new paragraph started; tag it with the spacing once its text begins

        void Flush()
        {
            if (!carry || dest) return;
            carry = false;
            if (spacing != null) sb.Append(SpacingPrefix).Append(spacing).Append("@@");
        }
        bool dest = false;      // inside a group that isn't body text
        bool groupStart = false;
        int i = 0, n = rtf.Length;

        void Open(int s) { if (s == 1) sb.Append(SubStart); else if (s == 2) sb.Append(SupStart); }
        void Close(int s) { if (s == 1) sb.Append(SubEnd); else if (s == 2) sb.Append(SupEnd); }
        void SetState(int s)
        {
            if (s == state) return;
            Close(state);
            Open(s);
            state = s;
        }

        while (i < n)
        {
            char c = rtf[i];
            if (c == '\\' && i + 1 < n)
            {
                if (char.IsLetter(rtf[i + 1]))
                {
                    int j = i + 1;
                    while (j < n && char.IsLetter(rtf[j])) j++;
                    string word = rtf.Substring(i + 1, j - i - 1);
                    int k = j;
                    if (k < n && (rtf[k] == '-' || char.IsDigit(rtf[k]))) { k++; while (k < n && char.IsDigit(rtf[k])) k++; }
                    string param = rtf.Substring(j, k - j);
                    int end = k < n && rtf[k] == ' ' ? k + 1 : k;

                    if (groupStart) { groupStart = false; if (Destinations.Contains(word)) dest = true; }

                    if (word == "bin" && int.TryParse(param, out var bytes) && bytes > 0)
                    {
                        int take = Math.Min(bytes, n - end);
                        sb.Append(rtf, i, end - i).Append(rtf, end, take);
                        i = end + take;
                        continue;
                    }
                    if (!dest && word == "sl")
                    {
                        // \slN is usually followed by \slmultM
                        string mult = "0";
                        if (string.CompareOrdinal(rtf, end, "\\slmult", 0, 7) == 0)
                        {
                            int q = end + 7, q0 = q;
                            while (q < n && char.IsDigit(rtf[q])) q++;
                            mult = q > q0 ? rtf.Substring(q0, q - q0) : "1";
                            end = q < n && rtf[q] == ' ' ? q + 1 : q;
                        }
                        spacing = param.Length == 0 || param == "0" ? null : param + "," + mult;
                        carry = false;
                        if (spacing != null) sb.Append(SpacingPrefix).Append(spacing).Append("@@");
                        i = end;
                        continue;
                    }
                    if (!dest && word == "pard") { spacing = null; carry = false; }
                    if (!dest && word == "par")
                    {
                        sb.Append(rtf, i, end - i);
                        i = end;
                        // Spacing carries on to the next paragraph unless \pard resets it first.
                        carry = spacing != null;
                        continue;
                    }
                    if (!dest && (word == "u" || word == "tab" || word == "emdash" || word == "endash" || word == "bullet" ||
                                  word == "lquote" || word == "rquote" || word == "ldblquote" || word == "rdblquote")) Flush();
                    if (!dest)
                    {
                        int? next = word switch
                        {
                            "sub" => 1,
                            "super" => 2,
                            "nosupersub" => 0,
                            "dn" => param == "0" ? 0 : 1,
                            "up" => param == "0" ? 0 : 2,
                            _ => null,
                        };
                        if (next.HasValue) { SetState(next.Value); i = end; continue; }
                        if (word == "plain") SetState(0);
                    }
                    sb.Append(rtf, i, end - i);
                    i = end;
                    continue;
                }
                // Control symbols: \*, \'hh, \{, \}, \\ and friends
                if (rtf[i + 1] == '*' && groupStart) dest = true;
                if (rtf[i + 1] == '\'' || rtf[i + 1] == '{' || rtf[i + 1] == '}' || rtf[i + 1] == '\\' || rtf[i + 1] == '~') Flush();
                groupStart = false;
                int len = rtf[i + 1] == '\'' ? 4 : 2;
                len = Math.Min(len, n - i);
                sb.Append(rtf, i, len);
                i += len;
                continue;
            }
            if (c == '{')
            {
                stack.Push((state, dest, spacing));
                sb.Append('{');
                groupStart = true;
                i++;
                continue;
            }
            if (c == '}')
            {
                var (parentState, parentDest, parentSpacing) = stack.Count > 0 ? stack.Pop() : (0, false, null);
                if (!dest && state != parentState) Close(state);
                sb.Append('}');
                if (!parentDest && state != parentState) Open(parentState);
                state = parentState;
                dest = parentDest;
                spacing = parentSpacing;
                groupStart = false;
                i++;
                continue;
            }
            if (c != '\r' && c != '\n') { groupStart = false; Flush(); }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Turns the text tags left by PrepareForLoad into real formatting, and removes them.</summary>
    public static void ApplyMarkers(FlowDocument doc)
    {
        // Kind: 1/-1 subscript start/end, 2/-2 superscript start/end, 3 line spacing.
        var hits = new List<(TextPointer Start, TextPointer End, int Kind, Paragraph Para, int Twips, int Mult)>();
        foreach (var run in DocumentService.Walk(doc.Blocks).OfType<Run>().ToList())
        {
            var t = run.Text;
            int idx = t.IndexOf("@@DS", StringComparison.Ordinal);
            while (idx >= 0)
            {
                int len = 0, kind = 0, twips = 0, mult = 0;
                if (string.CompareOrdinal(t, idx, MarkerPrefix, 0, MarkerPrefix.Length) == 0 && idx + MarkerLength <= t.Length)
                {
                    var m = t.Substring(idx, MarkerLength);
                    kind = m == SubStart ? 1 : m == SubEnd ? -1 : m == SupStart ? 2 : m == SupEnd ? -2 : 0;
                    len = MarkerLength;
                }
                else if (string.CompareOrdinal(t, idx, SpacingPrefix, 0, SpacingPrefix.Length) == 0)
                {
                    int close = t.IndexOf("@@", idx + SpacingPrefix.Length, StringComparison.Ordinal);
                    var parts = close > 0 ? t.Substring(idx + SpacingPrefix.Length, close - idx - SpacingPrefix.Length).Split(',') : Array.Empty<string>();
                    if (parts.Length == 2 && int.TryParse(parts[0], out twips) && int.TryParse(parts[1], out mult))
                    {
                        kind = 3;
                        len = close + 2 - idx;
                    }
                }
                if (kind != 0)
                {
                    hits.Add((run.ContentStart.GetPositionAtOffset(idx, LogicalDirection.Backward),
                              run.ContentStart.GetPositionAtOffset(idx + len, LogicalDirection.Forward), kind,
                              run.ContentStart.Paragraph, twips, mult));
                    idx = t.IndexOf("@@DS", idx + len, StringComparison.Ordinal);
                }
                else idx = t.IndexOf("@@DS", idx + 4, StringComparison.Ordinal);
            }
        }
        if (hits.Count == 0) return;

        var spans = new List<(TextPointer From, TextPointer To, BaselineAlignment Value)>();
        for (int a = 0; a < hits.Count; a++)
        {
            if (hits[a].Kind is not (1 or 2)) continue;
            int b = hits.FindIndex(a + 1, h => h.Kind == -hits[a].Kind);
            if (b < 0) continue;
            spans.Add((hits[a].End, hits[b].Start, hits[a].Kind == 1 ? BaselineAlignment.Subscript : BaselineAlignment.Superscript));
        }

        // Remove the tags (last first), then apply the formatting they stood for.
        for (int a = hits.Count - 1; a >= 0; a--)
            new TextRange(hits[a].Start, hits[a].End).Text = "";
        foreach (var (from, to, value) in spans)
            if (from.CompareTo(to) < 0)
                new TextRange(from, to).ApplyPropertyValue(Inline.BaselineAlignmentProperty, value);
        foreach (var h in hits.Where(h => h.Kind == 3 && h.Para != null))
            ApplySpacing(h.Para, h.Twips, h.Mult);
    }

    /// <summary>Applies RTF \slN\slmultM to a paragraph, the same way Draftly's own line spacing works.</summary>
    private static void ApplySpacing(Paragraph p, int twips, int mult)
    {
        if (mult == 1)
        {
            double factor = twips / 240.0;
            if (Math.Abs(factor - 1) < 0.01 || factor <= 0) { p.ClearValue(Block.LineHeightProperty); return; }
            double size = p.FontSize;
            foreach (var i in p.Inlines) if (i.FontSize > size) size = i.FontSize;
            p.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            p.LineHeight = size * 1.17 * factor;
        }
        else if (twips > 0)
        {
            p.LineStackingStrategy = LineStackingStrategy.MaxHeight;      // "at least"
            p.LineHeight = twips / 15.0;
        }
        else if (twips < 0)
        {
            p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; // "exactly"
            p.LineHeight = -twips / 15.0;
        }
    }
}
