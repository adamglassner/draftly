using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Draftly.Dialogs;
using Draftly.Services;
using Microsoft.Win32;

namespace Draftly;

public partial class MainWindow : FindReplaceWindow.IHost
{
    private FindReplaceWindow _findWindow;

    // ================================================================== undo / redo / select all

    private void Undo_Click(object sender, RoutedEventArgs e) { Editor.Undo(); Editor.Focus(); UpdateFormattingUi(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { Editor.Redo(); Editor.Focus(); UpdateFormattingUi(); }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { Editor.SelectAll(); Editor.Focus(); }

    // ================================================================== clipboard

    private void Cut_Click(object sender, RoutedEventArgs e) => CutSelection();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelection();
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteSmart();
    private void PasteSpecial_Click(object sender, RoutedEventArgs e) => PasteSpecial();

    // Copy/cut run with "automatic" text set to black so other apps (Word, email) get normal black text,
    // even when Draftly is showing light text on a dark page.
    private void CopySelection()
    {
        if (Editor.Selection.IsEmpty) return;
        try { DocumentService.WithBlackText(Editor.Document, () => Editor.Copy()); }
        catch (Exception ex) { ClipboardBusy(ex); }
        Editor.Focus();
    }

    private void CutSelection()
    {
        if (Editor.Selection.IsEmpty) return;
        try { DocumentService.WithBlackText(Editor.Document, () => Editor.Cut()); }
        catch (Exception ex) { ClipboardBusy(ex); }
        Editor.Focus();
    }

    private void ClipboardBusy(Exception ex) =>
        MessageDialog.Show(this, "Clipboard", "The clipboard is being used by another program. Try again in a moment.\n\n" + ex.Message, MessageDialog.Kind.Warning);

    /// <summary>Paste, then turn pasted black text into "automatic" colour so it stays readable in dark mode.</summary>
    private void PasteSmart()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null) return;
            bool hasText = data.GetDataPresent(DataFormats.Rtf) || data.GetDataPresent(DataFormats.UnicodeText) ||
                           data.GetDataPresent(DataFormats.Text) || data.GetDataPresent(DataFormats.XamlPackage);
            if (!hasText && Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null) InsertImage(ToPng(img));
                return;
            }
            PasteWith(() => Editor.Paste());
        }
        catch (Exception ex) { ClipboardBusy(ex); }
        Editor.Focus();
    }

    private void PasteWith(Action paste)
    {
        var start = Editor.Selection.Start.GetPositionAtOffset(0, LogicalDirection.Backward);
        var end = Editor.Selection.End.GetPositionAtOffset(0, LogicalDirection.Forward);
        Editor.BeginChange();
        try
        {
            paste();
            if (start != null && end != null) NormaliseRange(start, end);
        }
        finally { Editor.EndChange(); }
    }

    private void NormaliseRange(TextPointer start, TextPointer end)
    {
        foreach (var el in DocumentService.Walk(Editor.Document.Blocks))
        {
            if (el.ContentEnd.CompareTo(start) < 0) continue;
            if (el.ContentStart.CompareTo(end) > 0) continue;
            if (el.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush b &&
                b.Color.R < 0x10 && b.Color.G < 0x10 && b.Color.B < 0x10 && b.Color.A == 0xFF)
                el.ClearValue(TextElement.ForegroundProperty);
        }
    }

    private void PasteSpecial()
    {
        var d = new PasteSpecialDialog();
        if (d.ShowDialog() != true) { Editor.Focus(); return; }
        try
        {
            switch (d.Result)
            {
                case PasteKind.PlainText:
                    var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    Editor.BeginChange();
                    try
                    {
                        Editor.Selection.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                        Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
                    }
                    finally { Editor.EndChange(); }
                    break;
                case PasteKind.Picture:
                    var img = Clipboard.GetImage();
                    if (img != null) InsertImage(ToPng(img));
                    break;
                default:
                    var rtf = Clipboard.GetData(DataFormats.Rtf);
                    var bytes = rtf switch
                    {
                        string s => Encoding.Latin1.GetBytes(s),
                        MemoryStream ms => ms.ToArray(),
                        _ => null,
                    };
                    if (bytes == null) { PasteSmart(); break; }
                    PasteWith(() =>
                    {
                        using var stream = new MemoryStream(bytes);
                        Editor.Selection.Load(stream, DataFormats.Rtf);
                        Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
                    });
                    break;
            }
        }
        catch (Exception ex) { ClipboardBusy(ex); }
        Editor.Focus();
    }

    // ================================================================== pictures

    private void InsertPicture_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.ico)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.ico|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) { Editor.Focus(); return; }
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            InsertImage(bmp);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "Insert picture", $"\"{Path.GetFileName(dlg.FileName)}\" couldn't be read as a picture.\n\n{ex.Message}", MessageDialog.Kind.Error);
        }
        Editor.Focus();
    }

    private static BitmapImage ToPng(BitmapSource src)
    {
        // Clipboard images can be odd DIB formats; re-encoding as PNG keeps them savable in RTF.
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void InsertImage(BitmapSource bmp)
    {
        double w = bmp.Width, h = bmp.Height;
        double max = Math.Max(48, _textWidth - 2);
        if (w > max) { h = h * max / w; w = max; }
        var image = new Image { Source = bmp, Width = Math.Round(w), Height = Math.Round(h), Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        Editor.BeginChange();
        try
        {
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var pos = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var container = new InlineUIContainer(image, pos) { BaselineAlignment = BaselineAlignment.Bottom };
            Editor.CaretPosition = container.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
        }
        finally { Editor.EndChange(); }
    }

    /// <summary>The picture inside the selection, if the selection is (mostly) one picture.</summary>
    private Image SelectedImage()
    {
        var sel = Editor.Selection;
        for (var p = sel.Start; p != null && p.CompareTo(sel.End) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer c && c.Child is Image img) return img;
        if (sel.IsEmpty)
        {
            if (sel.Start.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer f && f.Child is Image a) return a;
            if (sel.Start.GetAdjacentElement(LogicalDirection.Backward) is InlineUIContainer b && b.Child is Image c2) return c2;
        }
        return null;
    }

    private void ResizePicture(Image img)
    {
        var src = img.Source as BitmapSource;
        if (src == null) return;
        var d = new PictureSizeDialog(img.Width, img.Height, src.Width, src.Height);
        if (d.ShowDialog() == true)
        {
            img.Width = Math.Round(d.NewWidth);
            img.Height = Math.Round(d.NewHeight);
            SetDirty(true);
        }
        Editor.Focus();
    }

    // ================================================================== date/time & objects

    private void InsertDateTime_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTimeDialog();
        if (d.ShowDialog() == true && d.Result != null) InsertText(d.Result);
        Editor.Focus();
    }

    private void InsertText(string text)
    {
        Editor.BeginChange();
        try
        {
            Editor.Selection.Text = text;
            Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
        }
        finally { Editor.EndChange(); }
    }

    private void InsertObject_Click(object sender, RoutedEventArgs e)
    {
        var d = new InsertObjectDialog();
        if (d.ShowDialog() != true) { Editor.Focus(); return; }
        switch (d.Kind)
        {
            case ObjectKind.Table: InsertTable(d.Rows, d.Columns); break;
            case ObjectKind.Symbol: InsertText(d.Symbol); break;
            case ObjectKind.HorizontalLine: InsertHorizontalLine(); break;
            case ObjectKind.PageBreak: InsertPageBreak(); break;
            case ObjectKind.FileContents: InsertFileContents(); break;
        }
        Editor.Focus();
    }

    /// <summary>Inserts a block after the caret's paragraph and returns the empty paragraph that follows it.</summary>
    private Paragraph InsertBlockAfterCaret(Block block)
    {
        var para = Editor.CaretPosition.Paragraph;
        var after = new Paragraph { Margin = new Thickness(0) };
        if (para != null && para.Parent is not TableCell)
        {
            bool empty = new TextRange(para.ContentStart, para.ContentEnd).Text.Length == 0 && !para.Inlines.OfType<InlineUIContainer>().Any();
            para.SiblingBlocks.InsertAfter(para, block);
            if (empty) para.SiblingBlocks.Remove(para);
            block.SiblingBlocks.InsertAfter(block, after);
        }
        else
        {
            Editor.Document.Blocks.Add(block);
            Editor.Document.Blocks.Add(after);
        }
        return after;
    }

    private void InsertTable(int rows, int cols)
    {
        var border = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        border.Freeze();
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 4) };
        for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow();
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell(new Paragraph { Margin = new Thickness(0) })
                {
                    BorderBrush = border, BorderThickness = new Thickness(0.75), Padding = new Thickness(6, 3, 6, 3),
                });
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);

        Editor.BeginChange();
        try
        {
            InsertBlockAfterCaret(table);
            Editor.CaretPosition = table.RowGroups[0].Rows[0].Cells[0].ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        }
        finally { Editor.EndChange(); }
    }

    private void InsertHorizontalLine()
    {
        var line = new Paragraph
        {
            Margin = new Thickness(0, 6, 0, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            FontSize = 4,
        };
        Editor.BeginChange();
        try
        {
            var after = InsertBlockAfterCaret(line);
            Editor.CaretPosition = after.ContentStart;
        }
        finally { Editor.EndChange(); }
    }

    private void InsertPageBreak()
    {
        Editor.BeginChange();
        try
        {
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var pos = Editor.CaretPosition.InsertParagraphBreak();
            Editor.CaretPosition = pos;
            if (pos.Paragraph != null) pos.Paragraph.BreakPageBefore = true;
        }
        finally { Editor.EndChange(); }
        RefreshOverlays();
    }

    private void InsertFileContents()
    {
        var dlg = new OpenFileDialog { Filter = DocumentService.OpenFilter };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var loaded = DocumentService.Load(dlg.FileName).Document;
            using var ms = new MemoryStream();
            DocumentService.WithBlackText(loaded, () =>
                new TextRange(loaded.ContentStart, loaded.ContentEnd).Save(ms, DataFormats.XamlPackage));
            ms.Position = 0;
            PasteWith(() =>
            {
                Editor.Selection.Load(ms, DataFormats.XamlPackage);
                Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
            });
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "Insert file", $"\"{Path.GetFileName(dlg.FileName)}\" couldn't be inserted.\n\n{ex.Message}", MessageDialog.Kind.Error);
        }
    }

    // ================================================================== find & replace

    private void Find_Click(object sender, RoutedEventArgs e) => OpenFind(false);
    private void Replace_Click(object sender, RoutedEventArgs e) => OpenFind(true);

    private void OpenFind(bool replace)
    {
        string initial = Editor.Selection.IsEmpty ? null : Editor.Selection.Text;
        if (initial != null && (initial.Contains('\n') || initial.Length > 100)) initial = null;
        if (_findWindow == null || !_findWindow.IsLoaded)
        {
            _findWindow = new FindReplaceWindow(this, this, replace, initial);
            _findWindow.Closed += (_, _) => { _findWindow = null; Editor.Focus(); };
            _findWindow.Show();
        }
        else
        {
            _findWindow.SetMode(replace, initial);
            _findWindow.Activate();
        }
    }

    private sealed class TextMap
    {
        public readonly StringBuilder Text = new();
        public readonly List<(int Start, int Length, TextPointer Ptr)> Segments = new();
    }

    private TextMap BuildTextMap()
    {
        var map = new TextMap();
        var doc = Editor.Document;
        var p = doc.ContentStart;
        while (p != null && p.CompareTo(doc.ContentEnd) < 0)
        {
            var ctx = p.GetPointerContext(LogicalDirection.Forward);
            if (ctx == TextPointerContext.Text)
            {
                var t = p.GetTextInRun(LogicalDirection.Forward);
                map.Segments.Add((map.Text.Length, t.Length, p));
                map.Text.Append(t);
                p = p.GetPositionAtOffset(t.Length, LogicalDirection.Forward);
                continue;
            }
            if (ctx == TextPointerContext.ElementEnd && p.Parent is Paragraph) map.Text.Append('\n');
            else if (ctx == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak) map.Text.Append('\n');
            else if (ctx == TextPointerContext.EmbeddedElement) map.Text.Append('￼');
            p = p.GetNextContextPosition(LogicalDirection.Forward);
        }
        return map;
    }

    private static TextPointer PointerAt(TextMap map, int index, bool isEnd)
    {
        foreach (var (start, len, ptr) in map.Segments)
        {
            if (index >= start && (isEnd ? index <= start + len : index < start + len))
                return ptr.GetPositionAtOffset(index - start, LogicalDirection.Forward);
        }
        return null;
    }

    private static int IndexOf(TextMap map, TextPointer pos)
    {
        foreach (var (start, len, ptr) in map.Segments)
        {
            if (ptr.CompareTo(pos) > 0) return start;     // pos is before this run
            var segEnd = ptr.GetPositionAtOffset(len, LogicalDirection.Forward);
            if (segEnd.CompareTo(pos) >= 0) return start + ptr.GetOffsetToPosition(pos);
        }
        return map.Text.Length;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int Search(string text, FindOptions o, int from, bool up)
    {
        var cmp = o.MatchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        int idx = up ? from : from;
        while (true)
        {
            if (up)
            {
                if (idx <= 0) return -1;
                idx = text.LastIndexOf(o.Text, Math.Min(idx - 1, text.Length - 1), cmp);
            }
            else
            {
                if (idx > text.Length) return -1;
                idx = text.IndexOf(o.Text, idx, cmp);
            }
            if (idx < 0) return -1;
            if (!o.WholeWord) return idx;
            bool before = idx > 0 && IsWordChar(text[idx - 1]);
            int endI = idx + o.Text.Length;
            bool after = endI < text.Length && IsWordChar(text[endI]);
            if (!before && !after) return idx;
            idx = up ? idx : idx + 1;
        }
    }

    public bool FindNext(FindOptions o, bool quiet = false)
    {
        if (string.IsNullOrEmpty(o.Text)) return false;
        var map = BuildTextMap();
        var text = map.Text.ToString();
        int from = o.Up ? IndexOf(map, Editor.Selection.Start) : IndexOf(map, Editor.Selection.End);
        int idx = Search(text, o, from, o.Up);
        if (idx < 0 && o.WrapAround) idx = Search(text, o, o.Up ? text.Length : 0, o.Up);
        if (idx < 0)
        {
            if (!quiet) MessageDialog.Show(this, "Find", $"Draftly can't find \"{o.Text}\".");
            return false;
        }
        var s = PointerAt(map, idx, false);
        var e = PointerAt(map, idx + o.Text.Length, true);
        if (s == null || e == null) return false;
        Editor.Selection.Select(s, e);
        var r = s.GetCharacterRect(LogicalDirection.Forward);
        if (!r.IsEmpty) { r.Inflate(20, 60); Editor.BringIntoView(r); }
        return true;
    }

    public bool ReplaceCurrent(FindOptions o, string replacement)
    {
        var cmp = o.MatchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        if (Editor.Selection.IsEmpty || !string.Equals(Editor.Selection.Text, o.Text, cmp)) return false;
        Editor.BeginChange();
        try
        {
            Editor.Selection.Text = replacement ?? "";
            Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
        }
        finally { Editor.EndChange(); }
        return true;
    }

    public int ReplaceAll(FindOptions o, string replacement)
    {
        if (string.IsNullOrEmpty(o.Text)) return 0;
        var map = BuildTextMap();
        var text = map.Text.ToString();
        var ranges = new List<(TextPointer, TextPointer)>();
        int idx = 0;
        var forward = new FindOptions { Text = o.Text, MatchCase = o.MatchCase, WholeWord = o.WholeWord };
        while ((idx = Search(text, forward, idx, false)) >= 0)
        {
            var s = PointerAt(map, idx, false);
            var e = PointerAt(map, idx + o.Text.Length, true);
            if (s != null && e != null) ranges.Add((s, e));
            idx += o.Text.Length;
        }
        if (ranges.Count == 0) return 0;
        Editor.BeginChange();
        try
        {
            // Back to front, so earlier positions stay valid.
            for (int i = ranges.Count - 1; i >= 0; i--)
                new TextRange(ranges[i].Item1, ranges[i].Item2).Text = replacement ?? "";
        }
        finally { Editor.EndChange(); }
        return ranges.Count;
    }

    public void ShowStatus(string text) => ShowInfo(text);

    // ================================================================== context menu

    private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        EditorMenu.Items.Clear();
        MenuItem Item(string header, string glyph, string gesture, Action a, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            if (glyph != null) mi.Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 15 };
            mi.Click += (_, _) => a();
            return mi;
        }

        // Spelling suggestions
        var err = Editor.SpellCheck.IsEnabled ? Editor.GetSpellingError(Editor.CaretPosition) : null;
        if (err != null)
        {
            int n = 0;
            foreach (var s in err.Suggestions)
            {
                var word = s;
                var mi = Item(word, null, "", () => { err.Correct(word); SetDirty(true); });
                mi.FontWeight = FontWeights.SemiBold;
                EditorMenu.Items.Add(mi);
                if (++n == 6) break;
            }
            if (n == 0) EditorMenu.Items.Add(Item("No spelling suggestions", null, "", () => { }, false));
            EditorMenu.Items.Add(Item("Ignore all", null, "", () => err.IgnoreAll()));
            EditorMenu.Items.Add(new Separator());
        }

        bool hasSel = !Editor.Selection.IsEmpty;
        EditorMenu.Items.Add(Item("Cut", "", "Ctrl+X", CutSelection, hasSel));
        EditorMenu.Items.Add(Item("Copy", "", "Ctrl+C", CopySelection, hasSel));
        EditorMenu.Items.Add(Item("Paste", "", "Ctrl+V", PasteSmart, Clipboard.ContainsText() || Clipboard.ContainsImage() || Clipboard.ContainsData(DataFormats.Rtf)));
        EditorMenu.Items.Add(Item("Paste special…", null, "Ctrl+Alt+V", PasteSpecial));
        EditorMenu.Items.Add(new Separator());

        var img = SelectedImage();
        if (img != null)
        {
            EditorMenu.Items.Add(Item("Resize picture…", "", "", () => ResizePicture(img)));
            EditorMenu.Items.Add(new Separator());
        }
        EditorMenu.Items.Add(Item("Paragraph…", null, "", ShowParagraphDialog));
        EditorMenu.Items.Add(Item("Clear formatting", null, "Ctrl+Space", ClearFormatting, hasSel));
        EditorMenu.Items.Add(new Separator());
        EditorMenu.Items.Add(Item("Select all", "", "Ctrl+A", () => Editor.SelectAll()));
    }

    // ================================================================== keyboard

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleWindowShortcut(key, Keyboard.Modifiers)) e.Handled = true;
    }

    /// <summary>Window-wide shortcuts. Returns true if the key was handled.</summary>
    internal bool HandleWindowShortcut(Key key, ModifierKeys mods)
    {
        bool ctrl = mods == ModifierKeys.Control;
        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);

        if (ctrl && key == Key.N) NewDocument();
        else if (ctrl && key == Key.O) OpenDocument();
        else if (ctrl && key == Key.S) Save();
        else if ((ctrlShift && key == Key.S) || (mods == ModifierKeys.None && key == Key.F12)) SaveAs(null);
        else if (ctrl && key == Key.P) Print();
        else if (ctrl && key == Key.F2) PrintPreview();
        else if (ctrl && key == Key.F) OpenFind(false);
        else if (ctrl && key == Key.H) OpenFind(true);
        else if (key == Key.F3 && (mods == ModifierKeys.None || mods == ModifierKeys.Shift))
        {
            var o = FindReplaceWindow.LastOptions;
            if (o != null) { o.Up = mods == ModifierKeys.Shift; FindNext(o); }
            else OpenFind(false);
        }
        else if (ctrl && key == Key.OemComma) OpenSettings();
        // Ctrl+= is subscript while typing, so the main-keyboard "=" only zooms outside the page.
        else if (ctrl && (key == Key.Add || (key == Key.OemPlus && !Editor.IsKeyboardFocusWithin))) ZoomStep(+1);
        else if (ctrl && (key == Key.Subtract || key == Key.OemMinus)) ZoomStep(-1);
        else if (ctrl && (key == Key.D0 || key == Key.NumPad0)) SetZoom(1.0);
        else if (ctrlShift && key == Key.D8) { S.ShowFormattingMarks = !S.ShowFormattingMarks; SettingsService.Commit(); }
        else return false;
        return true;
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleEditorShortcut(key, Keyboard.Modifiers)) e.Handled = true;
    }

    /// <summary>Shortcuts that act on the document. Returns true if the key was handled.</summary>
    internal bool HandleEditorShortcut(Key key, ModifierKeys mods)
    {
        bool ctrl = mods == ModifierKeys.Control;
        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);

        if ((ctrl && key == Key.C) || (ctrl && key == Key.Insert)) CopySelection();
        else if ((ctrl && key == Key.X) || (mods == ModifierKeys.Shift && key == Key.Delete)) CutSelection();
        else if ((ctrl && key == Key.V) || (mods == ModifierKeys.Shift && key == Key.Insert)) PasteSmart();
        else if (mods == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.V) PasteSpecial();
        else if (ctrl && key == Key.Z) { if (Editor.CanUndo) Editor.Undo(); UpdateFormattingUi(); }
        else if (ctrl && key == Key.Y) { if (Editor.CanRedo) Editor.Redo(); UpdateFormattingUi(); }
        else if (ctrl && key == Key.A) Editor.SelectAll();
        else if (ctrl && key == Key.B) { EditingCommands.ToggleBold.Execute(null, Editor); UpdateFormattingUi(); }
        else if (ctrl && key == Key.I) { EditingCommands.ToggleItalic.Execute(null, Editor); UpdateFormattingUi(); }
        else if (ctrl && key == Key.U) ToggleDecoration(TextDecorationLocation.Underline);
        else if (ctrl && key == Key.OemPlus) ToggleBaseline(BaselineAlignment.Subscript);
        else if (ctrlShift && key == Key.OemPlus) ToggleBaseline(BaselineAlignment.Superscript);
        else if (ctrl && key == Key.Space) ClearFormatting();
        else if (ctrlShift && key == Key.OemPeriod) StepFont(+1);
        else if (ctrlShift && key == Key.OemComma) StepFont(-1);
        else if (ctrlShift && key == Key.L) ToggleList(TextMarkerStyle.Disc);
        else if (ctrl && key == Key.L) SetAlignment(TextAlignment.Left);
        else if (ctrl && key == Key.E) SetAlignment(TextAlignment.Center);
        else if (ctrl && key == Key.R) SetAlignment(TextAlignment.Right);
        else if (ctrl && key == Key.J) SetAlignment(TextAlignment.Justify);
        else if (ctrl && key == Key.D1) SetLineSpacing(1.0);
        else if (ctrl && key == Key.D2) SetLineSpacing(2.0);
        else if (ctrl && key == Key.D5) SetLineSpacing(1.5);
        else if (ctrl && key == Key.Enter) InsertPageBreak();
        else if ((mods == ModifierKeys.None || mods == ModifierKeys.Shift) && (key == Key.PageDown || key == Key.PageUp))
            PageMove(key == Key.PageDown, mods == ModifierKeys.Shift);
        else return false;
        return true;
    }
}
