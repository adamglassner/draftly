using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Draftly.Dialogs;
using Draftly.Services;

namespace Draftly;

/// <summary>Self-tests for 1.2.0: tabs, sessions, styles, Markdown, links, tables, line numbers and more.</summary>
public partial class MainWindow
{
    internal async Task RunNotepadTestsAsync()
    {
        S.UseTabs = true;
        SettingsService.Commit();
        await Settle(150);
        await Test("Tabs: new, switch, separate undo, close with prompt", TabsWork);
        await Test("Tabs: opening files, reusing a blank tab, no duplicates", TabsOpenFiles);
        await Test("Tabs: session restore keeps unsaved work", SessionRestoreWorks);
        await Test("Files from a second launch open as tabs in this window", SingleInstanceWorks);
        await Test("Headings and styles survive saving", StylesWork);
        await Test("Markdown shortcuts while typing", MarkdownShortcutsWork);
        await Test("Links: insert, save, reopen, remove", LinksWork);
        await Test("Markdown files: open, source view, save", MarkdownFilesWork);
        await Test("Table rows and columns", TableEditingWorks);
        await Test("Line numbers", LineNumbersWork);
        await Test("Line endings are kept and can be changed", LineEndingsWork);
        await Test("F5 time/date and character count", F5AndCharacterCount);
        await Test("Register as an app for .rtf/.txt/.md", FileTypesRegister);
        FreshTabs();
        S.UseTabs = false;
        SettingsService.Commit();
        await Settle(150);
    }

    /// <summary>Closes every tab without asking, leaving one blank tab.</summary>
    private void FreshTabs()
    {
        foreach (var t in _tabs.ToList()) { t.Dirty = false; CloseTab(t, ask: false); }
        HideInfo();
        _tab.Dirty = false;
    }

    private void TypeSpace()
    {
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, Editor, " "))
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
        };
        Editor.RaiseEvent(args);
        if (!args.Handled) InsertText(" ");
    }

    private string WriteTestFile(string name, string text, Encoding enc = null)
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllText(path, text, enc ?? new UTF8Encoding(false));
        return path;
    }

    // ------------------------------------------------------------------

    private async Task TabsWork()
    {
        FreshTabs();
        await Settle();
        Assert(_tabs.Count == 1 && _tabPanel.Children.Count == 1, $"expected 1 tab, found {_tabs.Count} ({_tabPanel.Children.Count} headers)");
        Assert(DocTabsHost.IsVisible, "the tab strip isn't shown");
        TypeText("first tab");
        var first = _tab;

        Assert(HandleWindowShortcut(Key.T, ModifierKeys.Control), "Ctrl+T not handled");
        await Settle();
        Assert(_tabs.Count == 2 && _tab != first, "Ctrl+T didn't open a new tab");
        Assert(DocText() == "", "the new tab isn't empty");
        Assert(ReferenceEquals(PageBorder.Child, Editor), "the page doesn't show the new tab's editor");
        TypeText("second tab");
        var second = _tab;
        Assert(second.HeaderDot.Visibility == Visibility.Visible, "unsaved dot not shown on the tab");

        HandleWindowShortcut(Key.Tab, ModifierKeys.Control);
        await Settle();
        Assert(_tab == first && DocText() == "first tab", $"Ctrl+Tab: expected the first tab, got \"{DocText()}\"");
        Editor.Undo();
        Assert(DocText() == "", "undo in the first tab didn't undo its typing");
        Editor.Redo();
        HandleWindowShortcut(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
        await Settle();
        Assert(_tab == second && DocText() == "second tab", "undo in one tab changed another tab");

        ClickWhenOpen<SaveChangesDialog>("Cancel");
        Assert(!CloseTab(_tab), "Cancel should keep the tab");
        Assert(_tabs.Count == 2, "Cancel closed the tab");
        ClickWhenOpen<SaveChangesDialog>("Don't Save");
        Assert(HandleWindowShortcut(Key.W, ModifierKeys.Control), "Ctrl+W not handled");
        await Settle(200);
        Assert(_tabs.Count == 1 && _tab == first && _tabPanel.Children.Count == 1, "Ctrl+W didn't close the tab");

        // Closing the last tab leaves a blank one.
        first.Dirty = false;
        CloseTab(first, ask: false);
        Assert(_tabs.Count == 1 && _tab != first && DocText() == "", "closing the last tab should leave a blank document");

        if (_shotDir != null)
        {
            TypeText("Draftly 1.2 has tabs");
            HandleWindowShortcut(Key.T, ModifierKeys.Control);
            TypeText("# Notes\n- headings\n- links");
            await Settle(300);
            SnapshotWindow(this, "main-tabs-light");
            FreshTabs();
        }

        // With tabs turned off, a single document hides the strip.
        S.UseTabs = false; SettingsService.Commit(); await Settle();
        Assert(!DocTabsHost.IsVisible, "the tab strip shows with tabs turned off");
        S.UseTabs = true; SettingsService.Commit(); await Settle();
    }

    private async Task TabsOpenFiles()
    {
        FreshTabs();
        var a = WriteTestFile("tab-a.txt", "alpha");
        var b = WriteTestFile("tab-b.txt", "bravo");
        OpenFromUser(a);
        await Settle();
        Assert(_tabs.Count == 1 && _path == a, "the file should open in the blank tab");
        OpenFromUser(b);
        await Settle();
        Assert(_tabs.Count == 2 && _path == b && DocText() == "bravo", "the second file should get its own tab");
        Assert(_tab.HeaderText.Text == "tab-b.txt", $"tab header says \"{_tab.HeaderText.Text}\"");
        OpenFromUser(a);
        await Settle();
        Assert(_tabs.Count == 2 && _path == a && DocText() == "alpha", "opening an open file should switch to its tab");
        Assert(Title.StartsWith("tab-a.txt"), $"title is \"{Title}\"");
        FreshTabs();
    }

    private async Task SessionRestoreWorks()
    {
        FreshTabs();
        var a = WriteTestFile("session-a.txt", "saved file");
        OpenFromUser(a);
        NewTab();
        TypeText("unsaved session text");
        await Settle();
        SaveSession(keepUnsaved: true);
        FreshTabs();
        Assert(_tabs.Count == 1 && DocText() == "", "tabs weren't cleared");

        bool oldKeep = S.KeepUnsavedOnClose;
        Assert(RestoreSession(), "nothing was restored");
        await Settle(150);
        Assert(_tabs.Any(t => t.Path == a), "the saved file's tab didn't come back");
        var u = _tabs.FirstOrDefault(t => DocumentService.GetPlainText(t.Box.Document) == "unsaved session text");
        Assert(u != null, "the unsaved tab didn't come back");
        Assert(u.Dirty, "the restored unsaved tab should still be marked as changed");
        Assert(_tab == u, "the active tab wasn't restored");
        Assert(!Directory.Exists(Path.Combine(SettingsService.Folder, "Session")), "the session folder wasn't cleaned up");
        S.KeepUnsavedOnClose = oldKeep;
        FreshTabs();
    }

    private async Task SingleInstanceWorks()
    {
        FreshTabs();
        var f = WriteTestFile("from-explorer.txt", "sent from another launch");
        SingleInstance.PipeName = "DraftlySelfTest-" + Guid.NewGuid().ToString("N")[..8];
        SingleInstance.Listen(files => Dispatcher.BeginInvoke(new Action(() => ReceiveFiles(files))));
        bool sent = await Task.Run(() => SingleInstance.TrySend(new[] { f }, 3000));
        Assert(sent, "couldn't reach the running window");
        for (int i = 0; i < 30 && _path != f; i++) await Settle(100);
        Assert(_path == f && DocText() == "sent from another launch", "the file didn't open in this window");
        FreshTabs();
    }

    private async Task StylesWork()
    {
        FreshTabs();
        TypeText("My title\nFirst heading\nBody text\nSmaller heading");
        var blocks = Editor.Document.Blocks.OfType<Paragraph>().ToList();
        Editor.CaretPosition = blocks[0].ContentStart;
        Assert(HandleEditorShortcut(Key.T, ModifierKeys.Control | ModifierKeys.Alt), "Ctrl+Alt+T not handled");
        Editor.CaretPosition = blocks[1].ContentStart;
        HandleEditorShortcut(Key.D1, ModifierKeys.Control | ModifierKeys.Alt);
        Editor.CaretPosition = blocks[3].ContentStart;
        ApplyParagraphStyle(ParaStyle.Heading2);
        await Settle();
        Assert(ParagraphStyles.Detect(blocks[0]) == ParaStyle.Title, "Title not applied");
        Assert(ParagraphStyles.Detect(blocks[1]) == ParaStyle.Heading1, "Heading 1 not applied");
        Assert(ParagraphStyles.Detect(blocks[2]) == ParaStyle.Normal, "body text changed");
        Assert((string)StylesBtn.Content == "Heading 2", $"Styles button says \"{StylesBtn.Content}\"");

        // Enter at the end of a heading starts normal text.
        Editor.CaretPosition = blocks[1].ContentEnd;
        Assert(HandleEnterAfterHeading(), "Enter after a heading wasn't handled");
        InsertText("after heading");
        var p = Editor.CaretPosition.Paragraph;
        Assert(p != blocks[1] && ParagraphStyles.Detect(p) == ParaStyle.Normal, "the paragraph after a heading isn't Normal");

        var path = Path.Combine(_testDir, "styles.rtf");
        _path = path; _format = DocFormat.Rtf;
        Assert(Save(), "save failed");
        FreshTabs();
        OpenFromUser(path);
        await Settle(150);
        var after = Editor.Document.Blocks.OfType<Paragraph>().ToList();
        Assert(ParagraphStyles.Detect(after[0]) == ParaStyle.Title, "Title lost after reopening");
        Assert(ParagraphStyles.Detect(after[1]) == ParaStyle.Heading1, "Heading 1 lost after reopening");
        Assert(after.Any(x => new TextRange(x.ContentStart, x.ContentEnd).Text == "Smaller heading" && ParagraphStyles.Detect(x) == ParaStyle.Heading2), "Heading 2 lost after reopening");
        FreshTabs();
    }

    private async Task MarkdownShortcutsWork()
    {
        FreshTabs();
        InsertText("#");
        TypeSpace();
        InsertText("Heading");
        var h = Editor.CaretPosition.Paragraph;
        Assert(new TextRange(h.ContentStart, h.ContentEnd).Text == "Heading", $"heading text is \"{new TextRange(h.ContentStart, h.ContentEnd).Text}\"");
        Assert(ParagraphStyles.Detect(h) == ParaStyle.Heading1, "\"# \" didn't make a heading");

        HandleEnterAfterHeading();
        InsertText("-");
        TypeSpace();
        Assert(Editor.CaretPosition.Paragraph?.Parent is ListItem li && ((List)li.Parent).MarkerStyle == TextMarkerStyle.Disc, "\"- \" didn't start a bullet list");

        FreshTabs();
        InsertText("1.");
        TypeSpace();
        Assert(Editor.CaretPosition.Paragraph?.Parent is ListItem li2 && ((List)li2.Parent).MarkerStyle == TextMarkerStyle.Decimal, "\"1. \" didn't start a numbered list");

        FreshTabs();
        InsertText(">");
        TypeSpace();
        Assert(ParagraphStyles.Detect(Editor.CaretPosition.Paragraph) == ParaStyle.Quote, "\"> \" didn't make a quote");

        // Not in the middle of a line, and not when turned off.
        FreshTabs();
        InsertText("a #");
        TypeSpace();
        Assert(DocText() == "a # ", "a # in the middle of a line was converted");
        FreshTabs();
        S.MarkdownShortcuts = false;
        InsertText("#");
        TypeSpace();
        S.MarkdownShortcuts = true;
        Assert(DocText() == "# ", "converted although Markdown shortcuts are off");
        FreshTabs();
        await Settle();
    }

    private async Task LinksWork()
    {
        FreshTabs();
        TypeText("Visit the site today");
        SelectFirst("site");
        ClickWhenOpen<LinkDialog>("OK", d => FindVisuals<TextBox>(d).ElementAt(1).Text = "example.com");
        EditLink();
        await Settle();
        var link = DocumentService.Walk(Editor.Document.Blocks).OfType<Hyperlink>().FirstOrDefault();
        Assert(link != null, "no link was created");
        Assert(link.NavigateUri?.AbsoluteUri == "https://example.com/", $"link goes to {link.NavigateUri}");
        Assert(new TextRange(link.ContentStart, link.ContentEnd).Text == "site", "the link text changed");
        Assert(DocText() == "Visit the site today", $"text is \"{DocText()}\"");
        Editor.CaretPosition = link.ContentStart.GetPositionAtOffset(2);
        UpdateStatus();
        Assert(LinkInfo.IsVisible && LinkInfo.Text.Contains("example.com"), "the status bar doesn't show the link");

        var path = Path.Combine(_testDir, "link.rtf");
        _path = path; _format = DocFormat.Rtf;
        Assert(Save(), "save failed");
        FreshTabs();
        OpenFromUser(path);
        await Settle(150);
        link = DocumentService.Walk(Editor.Document.Blocks).OfType<Hyperlink>().FirstOrDefault();
        Assert(link?.NavigateUri?.AbsoluteUri == "https://example.com/", "the link didn't survive saving as RTF");
        Assert(MarkdownService.FromDocument(Editor.Document).Contains("[site](https://example.com"), "Markdown export lost the link");

        Editor.CaretPosition = link.ContentStart.GetPositionAtOffset(3);
        Assert(LinkAt(Editor.Selection.Start) != null, "the cursor isn't in the reopened link");
        ClickWhenOpen<LinkDialog>("Remove link");
        EditLink();
        await Settle();
        Assert(!DocumentService.Walk(Editor.Document.Blocks).OfType<Hyperlink>().Any(), "Remove link left the link");
        Assert(DocText() == "Visit the site today", "removing the link changed the text");
        FreshTabs();
    }

    private async Task MarkdownFilesWork()
    {
        FreshTabs();
        var md = "# Project notes\n\nSome **bold** and *italic* text with a [link](https://example.com).\n\n- one\n- two\n\n| Name | Count |\n| --- | --- |\n| Apples | 3 |\n";
        var path = WriteTestFile("notes.md", md);
        OpenFromUser(path);
        await Settle(150);
        Assert(_format == DocFormat.Md, $"format is {_format}");
        Assert(_tab.LineEnding == "\n", "LF line endings not detected");
        var blocks = DocumentService.Walk(Editor.Document.Blocks).ToList();
        var first = Editor.Document.Blocks.FirstBlock as Paragraph;
        Assert(first != null && ParagraphStyles.Detect(first) == ParaStyle.Heading1, "the heading isn't a heading");
        Assert(blocks.OfType<Run>().Any(r => r.Text == "bold" && r.FontWeight == FontWeights.Bold), "bold text isn't bold");
        Assert(blocks.OfType<List>().Any(), "the list isn't a list");
        Assert(blocks.OfType<Table>().Any(), "the table isn't a table");
        Assert(blocks.OfType<Hyperlink>().Any(), "the link isn't a link");
        Assert(MarkdownViewBtn.IsVisible || !S.ShowStatusBar, "no Markdown view switch in the status bar");

        ToggleMarkdownSource();
        await Settle();
        Assert(_tab.MarkdownSource && DocText().Contains("# Project notes") && DocText().Contains("**bold**"), $"source view shows \"{DocText()}\"");
        Assert(!_dirty, "switching views marked the file as changed");
        ToggleMarkdownSource();
        await Settle();
        Assert(!_tab.MarkdownSource && ParagraphStyles.Detect((Paragraph)Editor.Document.Blocks.FirstBlock) == ParaStyle.Heading1, "formatted view didn't come back");

        Editor.Document.Blocks.Add(new Paragraph(new Run("Added line")));
        Assert(Save(), "saving the Markdown file failed");
        var saved = File.ReadAllText(path);
        Assert(saved.Contains("# Project notes") && saved.Contains("**bold**") && saved.Contains("[link](https://example.com") &&
               saved.Contains("Apples") && saved.Contains("- one") && saved.Contains("Added line"), $"saved Markdown: {saved}");
        Assert(!saved.Contains('\r'), "LF file was saved with CRLF");
        FreshTabs();
    }

    private async Task TableEditingWorks()
    {
        FreshTabs();
        InsertTable(2, 2);
        await Settle();
        Table Tbl() => DocumentService.Walk(Editor.Document.Blocks).OfType<Table>().FirstOrDefault();
        int Rows() => Tbl().RowGroups.Sum(g => g.Rows.Count);
        int Cols() => Tbl().RowGroups[0].Rows[0].Cells.Count;
        Assert(CurrentCell() != null, "caret isn't in the table");
        TableInsertRow(true);
        Assert(Rows() == 3, $"rows: {Rows()}");
        TableInsertRow(false);
        Assert(Rows() == 4, $"rows: {Rows()}");
        TableInsertColumn(true);
        Assert(Cols() == 3 && Tbl().RowGroups[0].Rows.All(r => r.Cells.Count == 3), $"columns: {Cols()}");
        TableInsertColumn(false);
        Assert(Cols() == 4, $"columns: {Cols()}");
        TableDeleteColumn();
        Assert(Cols() == 3, $"columns after delete: {Cols()}");
        TableDeleteRow();
        Assert(Rows() == 3, $"rows after delete: {Rows()}");
        TableDelete();
        Assert(Tbl() == null, "the table wasn't deleted");
        Assert(_dirty, "table edits didn't mark the document as changed");
        FreshTabs();
        await Settle();
    }

    private async Task LineNumbersWork()
    {
        FreshTabs();
        var oldWrap = S.WordWrap;
        TypeText("one\ntwo\nthree\nfour");
        S.ShowLineNumbers = true; SettingsService.Commit();
        await Settle(200);
        Assert(ShowLineNumbersBtn.IsChecked == true, "the View tab toggle isn't on");
        Assert(Editor.Document.PagePadding.Left >= 46, $"no room for line numbers in the margin (padding {Editor.Document.PagePadding}, computed {_padLeft}, wrap {S.WordWrap})");
        _tab.LineNumbers.CollectNow();
        var n = _tab.LineNumbers.Numbers;
        Assert(n.Count >= 4 && n[0] == 1 && n.Take(4).SequenceEqual(new[] { 1, 2, 3, 4 }), $"numbers: {string.Join(",", n)}");
        Assert(_tab.LineNumbers.IsVisible, "line numbers aren't visible");

        S.WordWrap = WrapMode.WrapToWindow; SettingsService.Commit(); await Settle(200);
        Assert(Editor.Document.PagePadding.Left >= 46, $"no room for line numbers when wrapping to the window (padding {Editor.Document.PagePadding}, computed {_padLeft})");
        S.WordWrap = oldWrap; SettingsService.Commit();

        // A new tab gets them too.
        NewTab();
        TypeText("x\ny");
        await Settle(200);
        _tab.LineNumbers.CollectNow();
        Assert(_tab.LineNumbers.Numbers.Take(2).SequenceEqual(new[] { 1, 2 }), "a new tab has no line numbers");
        if (_shotDir != null) SnapshotWindow(this, "main-line-numbers-light");

        Assert(HandleWindowShortcut(Key.L, ModifierKeys.Control | ModifierKeys.Alt), "Ctrl+Alt+L not handled");
        await Settle();
        Assert(!S.ShowLineNumbers && !_tab.LineNumbers.IsVisible, "Ctrl+Alt+L didn't turn them off");
        FreshTabs();
    }

    private async Task LineEndingsWork()
    {
        FreshTabs();
        var path = WriteTestFile("unix.txt", "one\ntwo\n");
        OpenFromUser(path);
        await Settle();
        Assert(_tab.LineEnding == "\n", "LF not detected");
        UpdateStatus();
        Assert(LineEndingInfo.Text.Contains("LF") && !LineEndingInfo.Text.Contains("CRLF"), $"status bar says {LineEndingInfo.Text}");
        Editor.CaretPosition = Editor.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        InsertText("three");
        Assert(Save(), "save failed");
        var bytes = File.ReadAllText(path);
        Assert(!bytes.Contains('\r') && bytes.Contains("two\n"), "LF file saved with CR characters");
        SetLineEnding("\r\n");
        Assert(_dirty, "changing the line ending should mark the file as changed");
        Assert(Save(), "save failed");
        Assert(File.ReadAllText(path).Contains("one\r\ntwo"), "file wasn't converted to CRLF");
        FreshTabs();
    }

    private async Task F5AndCharacterCount()
    {
        FreshTabs();
        Assert(HandleWindowShortcut(Key.F5, ModifierKeys.None), "F5 not handled");
        Assert(DocText().Contains(DateTime.Now.ToString("d")), $"F5 inserted \"{DocText()}\"");
        FreshTabs();
        TypeText("abc de");
        UpdateStatus();
        Assert(CharInfo.Text == "6 characters", $"character count says \"{CharInfo.Text}\"");
        FreshTabs();
        await Settle();
    }

    private Task FileTypesRegister()
    {
        try
        {
            FileAssociations.Register();
            foreach (var ext in new[] { ".rtf", ".txt", ".md" })
                Assert(FileAssociations.IsRegistered(ext), $"not registered for {ext}");
        }
        finally { FileAssociations.Unregister(); }
        Assert(!FileAssociations.IsRegistered(".rtf"), "Unregister left Draftly registered");
        return Task.CompletedTask;
    }

    private async Task RibbonAutoHideWorks()
    {
        try
        {
            double pageTop = EditorScroll.TranslatePoint(new Point(0, 0), this).Y;
            S.AutoHideRibbon = true; SettingsService.Commit();
            await Settle(250);
            Assert(TabRow.IsVisible && !RootGrid.Children.Contains(RibbonBorder) && !RibbonBorder.IsVisible, "the ribbon is still in the window");
            Assert(EditorScroll.TranslatePoint(new Point(0, 0), this).Y < pageTop - 60, "the page didn't move up into the ribbon's space");

            TabHome.IsChecked = true;
            TabHome.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, TabHome));
            await Settle(300);
            Assert(RibbonPopup.IsOpen && RibbonBorder.IsVisible && HomePanel.IsVisible, "clicking Home didn't show the ribbon");
            Assert(Math.Abs(RibbonBorder.ActualWidth - (TabRow.ActualWidth - 16)) < 2, $"the dropped-down ribbon is {RibbonBorder.ActualWidth:0} px wide");

            TabHome.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, TabHome));
            await Settle(200);
            Assert(!RibbonPopup.IsOpen, "clicking Home again didn't hide the ribbon");

            // Using a command hides it again.
            TabHome.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, TabHome));
            await Settle(200);
            TypeText("auto hide");
            Editor.SelectAll();
            BoldBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, BoldBtn));
            await Settle(300);
            Assert(!RibbonPopup.IsOpen, "the ribbon stayed open after Bold");

            Assert(HandleWindowShortcut(Key.F1, ModifierKeys.Control), "Ctrl+F1 not handled");
            await Settle(250);
            Assert(!S.AutoHideRibbon && RootGrid.Children.Contains(RibbonBorder) && RibbonBorder.IsVisible, "Ctrl+F1 didn't bring the toolbar back");
            Assert(Math.Abs(EditorScroll.TranslatePoint(new Point(0, 0), this).Y - pageTop) < 2, "the page didn't go back to its place");
        }
        finally
        {
            S.AutoHideRibbon = false; SettingsService.Commit();
            ResetDocument();
        }
    }
}
