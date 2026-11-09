using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Draftly.Dialogs;
using Draftly.Services;

namespace Draftly;

/// <summary>
/// The features Windows 11 Notepad added: headings and styles, Markdown typing shortcuts, links,
/// Markdown files, table editing, line endings, F5 time/date — plus line numbers and default-app setup.
/// </summary>
public partial class MainWindow
{
    // ================================================================== paragraph styles

    private static ParaStyle? StyleForKey(Key key) => key switch
    {
        Key.D0 or Key.NumPad0 => ParaStyle.Normal,
        Key.D1 or Key.NumPad1 => ParaStyle.Heading1,
        Key.D2 or Key.NumPad2 => ParaStyle.Heading2,
        Key.D3 or Key.NumPad3 => ParaStyle.Heading3,
        Key.T => ParaStyle.Title,
        _ => null,
    };

    private void ApplyParagraphStyle(ParaStyle style)
    {
        if (_tab.MarkdownSource) return;   // the source view is plain text
        Editor.BeginChange();
        try
        {
            foreach (var p in SelectedParagraphs()) ParagraphStyles.Apply(p, style);
        }
        finally { Editor.EndChange(); }
        if (!_dirty) SetDirty(true);
        UpdateFormattingUi();
        RefreshOverlays();
        Editor.Focus();
    }

    private void StylesBtn_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var current = Editor.Selection.Start.Paragraph is Paragraph sp ? ParagraphStyles.Detect(sp) : ParaStyle.Normal;
        foreach (var (style, name, shortcut) in ParagraphStyles.All)
        {
            var st = style;
            // Each entry previews the style.
            var preview = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            switch (st)
            {
                case ParaStyle.Title: preview.FontSize = 22; preview.FontWeight = FontWeights.Bold; break;
                case ParaStyle.Heading1: preview.FontSize = 19; preview.FontWeight = FontWeights.Bold; break;
                case ParaStyle.Heading2: preview.FontSize = 16; preview.FontWeight = FontWeights.Bold; break;
                case ParaStyle.Heading3: preview.FontSize = 14; preview.FontWeight = FontWeights.Bold; break;
                case ParaStyle.Quote: preview.FontStyle = FontStyles.Italic; break;
                case ParaStyle.Code: preview.FontFamily = ParagraphStyles.CodeFont; break;
            }
            var mi = new MenuItem { Header = preview, InputGestureText = shortcut, IsCheckable = true, IsChecked = st == current };
            mi.Click += (_, _) => ApplyParagraphStyle(st);
            menu.Items.Add(mi);
        }
        menu.Closed += (_, _) => Editor.Focus();
        menu.IsOpen = true;
    }

    /// <summary>Pressing Enter at the end of a heading starts a Normal paragraph, as in Word.</summary>
    private bool HandleEnterAfterHeading()
    {
        var caret = Editor.CaretPosition;
        var p = caret.Paragraph;
        if (p == null || !Editor.Selection.IsEmpty || p.Parent is ListItem) return false;
        var style = ParagraphStyles.Detect(p);
        if (style is not (ParaStyle.Title or ParaStyle.Heading1 or ParaStyle.Heading2 or ParaStyle.Heading3)) return false;
        if (new TextRange(caret, p.ContentEnd).Text.Length > 0) return false;   // not at the end
        Editor.BeginChange();
        try
        {
            EditingCommands.EnterParagraphBreak.Execute(null, Editor);
            var next = Editor.CaretPosition.Paragraph;
            if (next != null && next != p)
            {
                ParagraphStyles.Apply(next, ParaStyle.Normal);
                Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Editor.Document.FontSize);
            }
        }
        finally { Editor.EndChange(); }
        UpdateFormattingUi();
        return true;
    }

    // ================================================================== Markdown typing shortcuts

    /// <summary>"# " makes a heading, "- " a bullet list, "1. " a numbered list and "> " a quote (like Notepad).</summary>
    private void Editor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender != Editor || e.Text != " " || !S.MarkdownShortcuts || !Editor.Selection.IsEmpty) return;
        if (_format == DocFormat.Txt || _tab.MarkdownSource) return;
        var caret = Editor.CaretPosition;
        var p = caret.Paragraph;
        if (p == null || p.Parent is TableCell) return;
        var before = new TextRange(p.ContentStart, caret).Text;
        if (before.Length is 0 or > 3) return;
        bool inList = p.Parent is ListItem;

        Action apply = before switch
        {
            "#" => () => ParagraphStyles.Apply(p, ParaStyle.Heading1),
            "##" => () => ParagraphStyles.Apply(p, ParaStyle.Heading2),
            "###" => () => ParagraphStyles.Apply(p, ParaStyle.Heading3),
            ">" => () => ParagraphStyles.Apply(p, ParaStyle.Quote),
            "-" or "*" when !inList => () => ToggleList(TextMarkerStyle.Disc),
            "1." when !inList => () => ToggleList(TextMarkerStyle.Decimal),
            _ => null,
        };
        if (apply == null) return;
        e.Handled = true;
        Editor.BeginChange();
        try
        {
            new TextRange(p.ContentStart, caret).Text = "";
            apply();
        }
        finally { Editor.EndChange(); }
        UpdateFormattingUi();
        RefreshOverlays();
    }

    // ================================================================== links

    private static Hyperlink LinkAt(TextPointer pos)
    {
        for (DependencyObject d = pos?.Parent; d is TextElement te; d = te.Parent)
            if (d is Hyperlink h) return h;
        return null;
    }

    private static string LinkAddress(Hyperlink h) =>
        h.NavigateUri?.OriginalString ?? new TextRange(h.ContentStart, h.ContentEnd).Text;

    private void InsertLink_Click(object sender, RoutedEventArgs e) => EditLink();

    /// <summary>Insert a link, or edit the one at the cursor (Ctrl+K).</summary>
    private void EditLink()
    {
        if (_tab.MarkdownSource) return;
        var sel = Editor.Selection;
        var link = LinkAt(sel.Start) ?? (sel.IsEmpty ? null : LinkAt(sel.End));
        string text = link != null ? new TextRange(link.ContentStart, link.ContentEnd).Text : sel.Text.Trim('\r', '\n');
        string address = link != null ? LinkAddress(link) :
            Uri.TryCreate(text, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http") ? text : null;
        var d = new LinkDialog(text, address, link != null);
        if (d.ShowDialog() != true) { Editor.Focus(); return; }

        Editor.BeginChange();
        try
        {
            if (d.RemoveRequested) RemoveLink(link);
            else if (link != null)
            {
                link.NavigateUri = new Uri(d.Address);
                if (d.LinkText != new TextRange(link.ContentStart, link.ContentEnd).Text)
                {
                    link.Inlines.Clear();
                    link.Inlines.Add(new Run(d.LinkText));
                }
            }
            else
            {
                if (sel.IsEmpty || sel.Text != d.LinkText || sel.Start.Paragraph != sel.End.Paragraph || sel.Start.Paragraph == null)
                    sel.Text = d.LinkText;
                var h = new Hyperlink(sel.Start, sel.End) { NavigateUri = new Uri(d.Address) };
                Editor.CaretPosition = h.ElementEnd.GetInsertionPosition(LogicalDirection.Forward) ?? Editor.CaretPosition;
            }
        }
        finally { Editor.EndChange(); }
        DocumentService.ApplyLinkColors(Editor.Document, ThemeManager.IsDarkPage);
        SetDirty(true);
        UpdateStatus();
        Editor.Focus();
    }

    private void RemoveLink(Hyperlink link)
    {
        if (link == null) return;
        Editor.BeginChange();
        try
        {
            var inlines = link.Inlines.ToList();
            var parent = link.SiblingInlines;
            foreach (var i in inlines)
            {
                link.Inlines.Remove(i);
                i.ClearValue(TextElement.ForegroundProperty);
                parent.InsertBefore(link, i);
            }
            parent.Remove(link);
        }
        finally { Editor.EndChange(); }
        SetDirty(true);
    }

    private void OpenLink(Hyperlink link)
    {
        var address = LinkAddress(link);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https" or "mailto") &&
            !MessageDialog.Ask(this, "Open link", $"This link opens:\n\n{address}\n\nOnly open links you trust. Open it?"))
            return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageDialog.Show(this, "Open link", "The link couldn't be opened.\n\n" + ex.Message, MessageDialog.Kind.Error); }
    }

    /// <summary>Ctrl+click opens a link (a plain click just puts the cursor in it, so links stay editable).</summary>
    private void Editor_LinkClick(object sender, MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || sender is not RichTextBox box) return;
        var pos = box.GetPositionFromPoint(e.GetPosition(box), false);
        var link = LinkAt(pos);
        if (link == null) return;
        e.Handled = true;
        OpenLink(link);
    }

    private void UpdateLinkStatus()
    {
        var link = Editor.Selection.IsEmpty ? LinkAt(Editor.CaretPosition) : null;
        if (link == null) { LinkInfo.Visibility = Visibility.Collapsed; return; }
        LinkInfo.Text = LinkAddress(link) + "  ·  Ctrl+click to open";
        LinkInfo.Visibility = Visibility.Visible;
    }

    // ================================================================== tables

    private TableCell CurrentCell()
    {
        for (DependencyObject d = Editor.Selection.Start.Parent; d is TextElement te; d = te.Parent)
            if (d is TableCell c) return c;
        return null;
    }

    private static TableCell NewCellLike(TableCell c) => new(new Paragraph { Margin = new Thickness(0) })
    {
        BorderBrush = c.BorderBrush, BorderThickness = c.BorderThickness, Padding = c.Padding, Background = c.Background,
    };

    private static Table TableOf(TableCell c) => (Table)((TableRowGroup)((TableRow)c.Parent).Parent).Parent;

    private void CaretTo(TableCell cell)
    {
        if (cell?.Blocks.FirstBlock is Block b) Editor.CaretPosition = b.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        Editor.Focus();
    }

    private void TableInsertRow(bool below)
    {
        var cell = CurrentCell();
        if (cell == null) return;
        var row = (TableRow)cell.Parent;
        var group = (TableRowGroup)row.Parent;
        var table = (Table)group.Parent;
        int cols = Math.Max(table.Columns.Count, group.Rows.Max(r => r.Cells.Count));
        var newRow = new TableRow();
        for (int i = 0; i < cols; i++) newRow.Cells.Add(NewCellLike(row.Cells[Math.Min(i, row.Cells.Count - 1)]));
        Editor.BeginChange();
        try { group.Rows.Insert(group.Rows.IndexOf(row) + (below ? 1 : 0), newRow); }
        finally { Editor.EndChange(); }
        CaretTo(newRow.Cells[Math.Min(row.Cells.IndexOf(cell), newRow.Cells.Count - 1)]);
        SetDirty(true);
    }

    private void TableInsertColumn(bool right)
    {
        var cell = CurrentCell();
        if (cell == null) return;
        var table = TableOf(cell);
        int col = ((TableRow)cell.Parent).Cells.IndexOf(cell) + (right ? 1 : 0);
        TableCell target = null;
        Editor.BeginChange();
        try
        {
            if (table.Columns.Count > 0) table.Columns.Insert(Math.Min(col, table.Columns.Count), new TableColumn());
            foreach (var group in table.RowGroups)
                foreach (var row in group.Rows)
                {
                    if (row.Cells.Count == 0) continue;
                    var like = row.Cells[Math.Min(Math.Max(col - (right ? 1 : 0), 0), row.Cells.Count - 1)];
                    var c = NewCellLike(like);
                    row.Cells.Insert(Math.Min(col, row.Cells.Count), c);
                    if (row == cell.Parent) target = c;
                }
        }
        finally { Editor.EndChange(); }
        CaretTo(target);
        SetDirty(true);
    }

    private void TableDeleteRow()
    {
        var cell = CurrentCell();
        if (cell == null) return;
        var row = (TableRow)cell.Parent;
        var group = (TableRowGroup)row.Parent;
        var table = (Table)group.Parent;
        if (table.RowGroups.Sum(g => g.Rows.Count) <= 1) { TableDelete(); return; }
        int index = group.Rows.IndexOf(row);
        int col = row.Cells.IndexOf(cell);
        Editor.BeginChange();
        try
        {
            group.Rows.Remove(row);
            if (group.Rows.Count == 0) table.RowGroups.Remove(group);
        }
        finally { Editor.EndChange(); }
        var rows = table.RowGroups.SelectMany(g => g.Rows).ToList();
        var next = rows[Math.Clamp(index, 0, rows.Count - 1)];
        CaretTo(next.Cells.Count > 0 ? next.Cells[Math.Min(col, next.Cells.Count - 1)] : null);
        SetDirty(true);
    }

    private void TableDeleteColumn()
    {
        var cell = CurrentCell();
        if (cell == null) return;
        var table = TableOf(cell);
        var row0 = (TableRow)cell.Parent;
        int col = row0.Cells.IndexOf(cell);
        if (table.RowGroups.SelectMany(g => g.Rows).All(r => r.Cells.Count <= 1)) { TableDelete(); return; }
        Editor.BeginChange();
        try
        {
            foreach (var group in table.RowGroups)
                foreach (var row in group.Rows)
                    if (col < row.Cells.Count) row.Cells.RemoveAt(col);
            if (col < table.Columns.Count) table.Columns.RemoveAt(col);
        }
        finally { Editor.EndChange(); }
        CaretTo(row0.Cells.Count > 0 ? row0.Cells[Math.Min(col, row0.Cells.Count - 1)] : null);
        SetDirty(true);
    }

    private void TableDelete()
    {
        var cell = CurrentCell();
        if (cell == null) return;
        var table = TableOf(cell);
        Editor.BeginChange();
        try
        {
            var next = table.NextBlock ?? table.PreviousBlock;
            var siblings = table.SiblingBlocks;
            siblings.Remove(table);
            if (next == null)
            {
                next = new Paragraph();
                siblings.Add(next);
            }
            Editor.CaretPosition = next.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        }
        finally { Editor.EndChange(); }
        SetDirty(true);
        Editor.Focus();
    }

    // ================================================================== F5, line endings, Markdown view

    /// <summary>F5 inserts the time and date, as in Notepad.</summary>
    private void InsertNowTimeDate()
    {
        var now = DateTime.Now;
        InsertText(now.ToString("t") + " " + now.ToString("d"));
        Editor.Focus();
    }

    private void LineEnding_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = LineEndingBtn, Placement = PlacementMode.Top };
        foreach (var nl in new[] { "\r\n", "\n", "\r" })
        {
            var value = nl;
            var mi = new MenuItem { Header = DocumentService.LineEndingLabel(nl), IsCheckable = true, IsChecked = _tab.LineEnding == nl };
            mi.Click += (_, _) => SetLineEnding(value);
            menu.Items.Add(mi);
        }
        menu.Closed += (_, _) => Editor.Focus();
        menu.IsOpen = true;
    }

    private void SetLineEnding(string nl)
    {
        if (_tab.LineEnding == nl) return;
        _tab.LineEnding = nl;
        SetDirty(true);
        UpdateStatus();
    }

    private void MarkdownSource_Click(object sender, RoutedEventArgs e) => ToggleMarkdownSource();

    /// <summary>Switches a Markdown tab between its formatted view and its source text.</summary>
    private void ToggleMarkdownSource()
    {
        if (_format != DocFormat.Md) { UpdateFormattingUi(); return; }
        bool dirty = _dirty;
        if (!_tab.MarkdownSource)
        {
            var doc = DocumentService.FromPlainText(MarkdownService.FromDocument(Editor.Document, "\r\n"));
            doc.FontFamily = ParagraphStyles.CodeFont;
            _tab.MarkdownSource = true;
            SetDocument(doc);
        }
        else
        {
            var text = DocumentService.GetPlainText(Editor.Document);
            _tab.MarkdownSource = false;
            SetDocument(MarkdownService.ToDocument(text));
        }
        SetDirty(dirty);
        UpdateFormattingUi();
        UpdateStatus();
        Editor.Focus();
    }

    private void UseTabs_Click(object sender, RoutedEventArgs e)
    {
        S.UseTabs = UseTabsBtn.IsChecked == true;
        SettingsService.Commit();
        Editor.Focus();
    }

    // ================================================================== default app

    /// <summary>Asks once whether to make Draftly the app that opens .rtf files.</summary>
    private void OfferDefaultApp()
    {
        if (App.IsSelfTest || !S.AskDefaultApp) return;
        if (InfoBar.Visibility == Visibility.Visible && _infoIsWindowLevel) return;   // e.g. the recovery offer comes first
        bool isDefault;
        try { isDefault = FileAssociations.IsDefault(".rtf"); } catch { isDefault = false; }
        S.AskDefaultApp = false;
        SettingsService.Save();
        if (isDefault) return;
        ShowInfo("Want .rtf files to open in Draftly? Windows asks you to confirm this in Settings (you can also do it later in Draftly's Settings › Files and saving).",
            "Make Draftly the default", FileAssociations.OpenDefaultAppsSettings);
    }
}
