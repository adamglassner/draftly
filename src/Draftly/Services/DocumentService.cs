using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Draftly.Services;

public enum DocFormat { Rtf, Txt, Docx }

public static class DocumentService
{
    static DocumentService()
    {
        // Enables Windows-1252 ("ANSI") and other legacy code pages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public const string OpenFilter =
        "All supported documents (*.rtf;*.txt;*.docx)|*.rtf;*.txt;*.docx|" +
        "Rich Text Format (*.rtf)|*.rtf|" +
        "Text documents (*.txt)|*.txt|" +
        "Word documents - import (*.docx)|*.docx|" +
        "All files (*.*)|*.*";

    public const string SaveFilter =
        "Rich Text Format (*.rtf)|*.rtf|" +
        "Text document (*.txt)|*.txt|" +
        "Unicode text document (*.txt)|*.txt";

    public static DocFormat FormatFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".rtf" => DocFormat.Rtf,
            ".docx" => DocFormat.Docx,
            _ => DocFormat.Txt,
        };
    }

    // ------------------------------------------------------------------ load

    public sealed class LoadResult
    {
        public FlowDocument Document;
        public DocFormat Format;
        public Encoding TextEncoding;   // for TXT files
        public string Warning;          // shown in the info bar, if any
    }

    public static LoadResult Load(string path)
    {
        var fmt = FormatFromPath(path);

        // Sniff: a file named .txt that is actually RTF (and vice versa) should still open correctly.
        byte[] head = new byte[6];
        using (var fs = File.OpenRead(path)) fs.Read(head, 0, head.Length);
        bool looksRtf = Encoding.ASCII.GetString(head).StartsWith("{\\rtf");
        bool looksZip = head[0] == 'P' && head[1] == 'K';

        if (fmt == DocFormat.Docx || (looksZip && fmt != DocFormat.Rtf))
        {
            var doc = DocxImporter.Import(path, out var warn);
            return new LoadResult { Document = doc, Format = DocFormat.Docx, Warning = warn };
        }
        if (looksRtf)
            return new LoadResult { Document = LoadRtf(path), Format = DocFormat.Rtf };

        if (fmt == DocFormat.Rtf)
            throw new InvalidDataException("This file has an .rtf extension but does not contain Rich Text. It may be damaged.");

        var text = ReadText(path, out var enc);
        return new LoadResult { Document = FromPlainText(text), Format = DocFormat.Txt, TextEncoding = enc };
    }

    public static FlowDocument LoadRtf(string path)
    {
        var doc = NewDocument();
        using var fs = File.OpenRead(path);
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        range.Load(fs, DataFormats.Rtf);
        return doc;
    }

    public static string ReadText(string path, out Encoding encoding)
    {
        var bytes = File.ReadAllBytes(path);

        // BOMs
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        { encoding = new UTF8Encoding(true); return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3); }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { encoding = Encoding.Unicode; return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { encoding = Encoding.BigEndianUnicode; return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); }

        // Valid UTF-8 without BOM?
        try
        {
            var strict = new UTF8Encoding(false, true);
            var s = strict.GetString(bytes);
            encoding = new UTF8Encoding(false);
            return s;
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.GetEncoding(1252);
            return encoding.GetString(bytes);
        }
    }

    public static FlowDocument FromPlainText(string text)
    {
        var doc = NewDocument();
        doc.Blocks.Clear();
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            if (line.Length > 0) p.Inlines.Add(new Run(line));
            doc.Blocks.Add(p);
        }
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        return doc;
    }

    /// <summary>A blank document with the user's default font and tight paragraph spacing.</summary>
    public static FlowDocument NewDocument()
    {
        var s = SettingsService.Current;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily(s.DefaultFontFamily),
            FontSize = PointsToDips(s.DefaultFontSize),
            TextAlignment = TextAlignment.Left,
        };
        doc.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        return doc;
    }

    // ------------------------------------------------------------------ save

    /// <summary>
    /// Saves the document. In dark mode the page shows light text, but "automatic" text is always
    /// written to disk as black so the file looks right in WordPad, Word, etc.
    /// </summary>
    public static void Save(FlowDocument doc, string path, DocFormat format, Encoding textEncoding = null)
    {
        var tmp = path + ".draftly-tmp";
        try
        {
            if (format == DocFormat.Rtf)
            {
                using var fs = File.Create(tmp);
                WithBlackText(doc, () => new TextRange(doc.ContentStart, doc.ContentEnd).Save(fs, DataFormats.Rtf));
            }
            else
            {
                var enc = textEncoding ?? EncodingFor(SettingsService.Current.TextEncoding);
                File.WriteAllText(tmp, GetPlainText(doc), enc);
            }

            if (File.Exists(path) && SettingsService.Current.KeepBackupOnSave)
                File.Copy(path, path + ".bak", true);

            // Write-then-swap so a crash mid-save never destroys the original file.
            if (File.Exists(path)) File.Replace(tmp, path, null, true);
            else File.Move(tmp, path);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>Runs an action with "automatic" text temporarily black (for saving, copying and printing).</summary>
    public static void WithBlackText(FlowDocument doc, Action action)
    {
        var local = doc.ReadLocalValue(TextElement.ForegroundProperty);
        try
        {
            doc.Foreground = Brushes.Black;
            action();
        }
        finally
        {
            if (local == DependencyProperty.UnsetValue) doc.ClearValue(TextElement.ForegroundProperty);
            else doc.SetValue(TextElement.ForegroundProperty, local);
        }
    }

    public static string GetPlainText(FlowDocument doc)
    {
        var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        // TextRange always ends with a paragraph break; drop the final one.
        if (text.EndsWith("\r\n")) text = text[..^2];
        return text;
    }

    public static Encoding EncodingFor(TxtEncoding e) => e switch
    {
        TxtEncoding.Utf8Bom => new UTF8Encoding(true),
        TxtEncoding.Ansi => Encoding.GetEncoding(1252),
        TxtEncoding.Utf16 => Encoding.Unicode,
        _ => new UTF8Encoding(false),
    };

    public static string EncodingLabel(Encoding e)
    {
        if (e == null) return "";
        if (e is UTF8Encoding u) return u.GetPreamble().Length > 0 ? "UTF-8 with BOM" : "UTF-8";
        if (e.CodePage == 1200) return "UTF-16 LE";
        if (e.CodePage == 1201) return "UTF-16 BE";
        if (e.CodePage == 1252) return "ANSI";
        return e.EncodingName;
    }

    // ------------------------------------------------------------------ helpers

    public static double PointsToDips(double pt) => pt * 96.0 / 72.0;
    public static double DipsToPoints(double dip) => Math.Round(dip * 72.0 / 96.0 * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>Serialise a FlowDocument to an in-memory XAML package (keeps images) and back. Used for print and recovery.</summary>
    public static FlowDocument Clone(FlowDocument source)
    {
        var copy = new FlowDocument
        {
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
        };
        using var ms = new MemoryStream();
        new TextRange(source.ContentStart, source.ContentEnd).Save(ms, DataFormats.XamlPackage);
        ms.Position = 0;
        new TextRange(copy.ContentStart, copy.ContentEnd).Load(ms, DataFormats.XamlPackage);
        return copy;
    }

    /// <summary>
    /// After loading a file, text that is explicitly black is really "automatic" colour in most RTF
    /// files. Clearing it lets the text follow the theme, so it stays readable on a dark page.
    /// </summary>
    public static void NormaliseAutomaticColour(FlowDocument doc)
    {
        foreach (var el in Walk(doc.Blocks))
        {
            if (el.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush b &&
                b.Color.R < 0x10 && b.Color.G < 0x10 && b.Color.B < 0x10 && b.Color.A == 0xFF)
            {
                el.ClearValue(TextElement.ForegroundProperty);
            }
        }
    }

    public static IEnumerable<TextElement> Walk(IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            switch (b)
            {
                case Paragraph p:
                    foreach (var i in WalkInlines(p.Inlines)) yield return i;
                    break;
                case List l:
                    foreach (var li in l.ListItems)
                    {
                        yield return li;
                        foreach (var x in Walk(li.Blocks)) yield return x;
                    }
                    break;
                case Section s:
                    foreach (var x in Walk(s.Blocks)) yield return x;
                    break;
                case Table t:
                    foreach (var g in t.RowGroups)
                        foreach (var r in g.Rows)
                            foreach (var c in r.Cells)
                            {
                                yield return c;
                                foreach (var x in Walk(c.Blocks)) yield return x;
                            }
                    break;
            }
        }
    }

    private static IEnumerable<TextElement> WalkInlines(InlineCollection inlines)
    {
        foreach (var i in inlines)
        {
            yield return i;
            if (i is Span s)
                foreach (var x in WalkInlines(s.Inlines)) yield return x;
        }
    }
}
