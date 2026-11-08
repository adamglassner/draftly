using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace Draftly.Services;

/// <summary>
/// WPF's RTF writer and reader ignore subscript and superscript (RTF \sub, \super, \dn, \up).
/// This fills the gap: on save, sub/superscript runs are tagged in a scratch copy and the tags are
/// turned into \sub / \super; on open or paste, those RTF codes are turned into tags that are then
/// applied as BaselineAlignment. The result reads and writes the same as WordPad and Word.
/// </summary>
public static class RtfBaseline
{
    private const string SubStart = "@@DSUB<@@", SubEnd = "@@DSUB>@@", SupStart = "@@DSUP<@@", SupEnd = "@@DSUP>@@";
    private const string MarkerPrefix = "@@DSU";
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
            rtf.IndexOf("\\dn", StringComparison.Ordinal) < 0 && rtf.IndexOf("\\up", StringComparison.Ordinal) < 0)
            return rtf;

        var sb = new StringBuilder(rtf.Length + 256);
        var stack = new Stack<(int State, bool Dest)>();
        int state = 0;          // 0 normal, 1 subscript, 2 superscript
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
                groupStart = false;
                int len = rtf[i + 1] == '\'' ? 4 : 2;
                len = Math.Min(len, n - i);
                sb.Append(rtf, i, len);
                i += len;
                continue;
            }
            if (c == '{')
            {
                stack.Push((state, dest));
                sb.Append('{');
                groupStart = true;
                i++;
                continue;
            }
            if (c == '}')
            {
                var (parentState, parentDest) = stack.Count > 0 ? stack.Pop() : (0, false);
                if (!dest && state != parentState) Close(state);
                sb.Append('}');
                if (!parentDest && state != parentState) Open(parentState);
                state = parentState;
                dest = parentDest;
                groupStart = false;
                i++;
                continue;
            }
            if (c != '\r' && c != '\n') groupStart = false;
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Turns the text tags left by PrepareForLoad into real sub/superscript, and removes them.</summary>
    public static void ApplyMarkers(FlowDocument doc)
    {
        var hits = new List<(TextPointer Start, TextPointer End, int Kind)>();
        foreach (var run in DocumentService.Walk(doc.Blocks).OfType<Run>().ToList())
        {
            var t = run.Text;
            int idx = t.IndexOf(MarkerPrefix, StringComparison.Ordinal);
            while (idx >= 0 && idx + MarkerLength <= t.Length)
            {
                var m = t.Substring(idx, MarkerLength);
                int kind = m == SubStart ? 1 : m == SubEnd ? -1 : m == SupStart ? 2 : m == SupEnd ? -2 : 0;
                if (kind != 0)
                    hits.Add((run.ContentStart.GetPositionAtOffset(idx, LogicalDirection.Backward),
                              run.ContentStart.GetPositionAtOffset(idx + MarkerLength, LogicalDirection.Forward), kind));
                idx = t.IndexOf(MarkerPrefix, idx + MarkerLength, StringComparison.Ordinal);
            }
        }
        if (hits.Count == 0) return;

        var spans = new List<(TextPointer From, TextPointer To, BaselineAlignment Value)>();
        for (int a = 0; a < hits.Count; a++)
        {
            if (hits[a].Kind <= 0) continue;
            int b = hits.FindIndex(a + 1, h => h.Kind == -hits[a].Kind);
            if (b < 0) continue;
            spans.Add((hits[a].End, hits[b].Start, hits[a].Kind == 1 ? BaselineAlignment.Subscript : BaselineAlignment.Superscript));
        }

        // Remove the tags (last first), then apply the formatting between them.
        for (int a = hits.Count - 1; a >= 0; a--)
            new TextRange(hits[a].Start, hits[a].End).Text = "";
        foreach (var (from, to, value) in spans)
            if (from.CompareTo(to) < 0)
                new TextRange(from, to).ApplyPropertyValue(Inline.BaselineAlignmentProperty, value);
    }
}
