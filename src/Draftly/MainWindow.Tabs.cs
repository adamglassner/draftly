using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Draftly.Controls;
using Draftly.Dialogs;
using Draftly.Services;

namespace Draftly;

/// <summary>One open document: its own editor (so undo history stays with it), file details and view state.</summary>
internal sealed class DocTab
{
    public RichTextBox Box;
    public string Path;                     // null = not saved yet
    public DocFormat Format = DocFormat.Rtf;
    public Encoding Encoding;               // TXT / Markdown
    public string LineEnding = "\r\n";      // TXT / Markdown
    public bool MarkdownSource;             // Markdown tab showing its source text
    public bool Dirty;
    public PageLayout Layout;
    public string RecoveredFrom, ImportedFrom;
    public string Info;                     // note shown in the info bar for this tab
    public double ScrollV, ScrollH;
    public readonly string Key = Guid.NewGuid().ToString("N")[..8];

    public FormattingMarksAdorner Marks;
    public CurrentLineAdorner CurrentLine;
    public LineNumberAdorner LineNumbers;
    public Border Header;
    public TextBlock HeaderText, HeaderDot;
}

/// <summary>Tabs, sessions and opening files from File Explorer (like Windows 11 Notepad).</summary>
public partial class MainWindow
{
    // Exists from the start (the XAML can raise events before InitTabs runs); InitTabs gives it the XAML editor.
    private DocTab _tab = new() { Layout = PageLayout.FromSettings(SettingsService.Current) };
    private readonly List<DocTab> _tabs = new();
    private ControlTemplate _editorTemplate;
    private StackPanel _tabPanel;
    private ScrollViewer _tabScroll;

    // The rest of the window works on "the document" — these always mean the active tab's.
    private string _path { get => _tab.Path; set => _tab.Path = value; }
    private DocFormat _format { get => _tab.Format; set => _tab.Format = value; }
    private Encoding _encoding { get => _tab.Encoding; set => _tab.Encoding = value; }
    private bool _dirty { get => _tab.Dirty; set => _tab.Dirty = value; }
    private PageLayout _layout { get => _tab.Layout; set => _tab.Layout = value; }
    private string _recoveredFrom { get => _tab.RecoveredFrom; set => _tab.RecoveredFrom = value; }
    private string _importedFrom { get => _tab.ImportedFrom; set => _tab.ImportedFrom = value; }
    private FormattingMarksAdorner _marks { get => _tab.Marks; set => _tab.Marks = value; }
    private CurrentLineAdorner _currentLine { get => _tab.CurrentLine; set => _tab.CurrentLine = value; }

    private DocTab TabOf(object box) => _tabs.FirstOrDefault(t => ReferenceEquals(t.Box, box));

    private static string DocNameOf(DocTab t) =>
        t.Path != null ? System.IO.Path.GetFileName(t.Path) :
        t.ImportedFrom != null ? System.IO.Path.GetFileName(t.ImportedFrom) :
        t.RecoveredFrom != null ? "Recovered - " + System.IO.Path.GetFileName(t.RecoveredFrom) : "Untitled";

    // ================================================================== setup

    /// <summary>Called from the constructor: the editor declared in XAML becomes the first tab.</summary>
    private void InitTabs()
    {
        _editorTemplate = Editor.Template;
        _tab.Box = Editor;
        _tabs.Add(_tab);
        WireEditor(Editor);
        BuildTabStrip();
        BuildTabHeader(_tab);
        UpdateTabStrip();

        AllowDrop = true;
        PreviewDragOver += Window_PreviewDragOver;
        PreviewDrop += Window_PreviewDrop;
    }

    /// <summary>Handlers every editor needs on top of those set in XAML.</summary>
    private void WireEditor(RichTextBox box)
    {
        box.IsInactiveSelectionHighlightEnabled = true;
        box.PreviewTextInput += Editor_PreviewTextInput;
        box.PreviewMouseLeftButtonDown += Editor_LinkClick;
        box.SpellCheck.IsEnabled = S.SpellCheck;
        box.AutoWordSelection = S.SelectWholeWords;
    }

    private RichTextBox CreateEditorBox()
    {
        var b = new RichTextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            AcceptsTab = true,
            AcceptsReturn = true,
            IsDocumentEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SelectionOpacity = 0.35,
            FocusVisualStyle = null,
            Template = _editorTemplate,
            ContextMenu = new ContextMenu(),
        };
        b.SetResourceReference(Control.ForegroundProperty, "PageText");
        b.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "PageText");
        b.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "Accent");
        b.SelectionChanged += Editor_SelectionChanged;
        b.TextChanged += Editor_TextChanged;
        b.PreviewMouseWheel += Editor_PreviewMouseWheel;
        b.PreviewKeyDown += Editor_PreviewKeyDown;
        b.ContextMenuOpening += Editor_ContextMenuOpening;
        WireEditor(b);
        return b;
    }

    // ================================================================== tab strip

    private void BuildTabStrip()
    {
        _tabPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button
        {
            Style = (Style)FindResource("IconButton"), Content = "", FontSize = 12, Width = 30, Height = 30,
            ToolTip = "New tab (Ctrl+T)", Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(add, "New tab");
        add.Click += (_, _) => NewTab();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_tabPanel);
        row.Children.Add(add);
        _tabScroll = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        _tabScroll.PreviewMouseWheel += (_, e) =>
        {
            _tabScroll.ScrollToHorizontalOffset(_tabScroll.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        };
        DocTabsHost.Child = _tabScroll;
    }

    private void BuildTabHeader(DocTab t)
    {
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        var dot = new TextBlock { Text = "●", FontSize = 9, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Unsaved changes" };
        dot.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        var close = new Button
        {
            Style = (Style)FindResource("IconButton"), Content = "", FontSize = 9, Width = 22, Height = 22,
            Margin = new Thickness(6, 0, 0, 0), ToolTip = "Close tab (Ctrl+W)",
        };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close tab");
        close.Click += (_, _) => CloseTab(t);
        var dock = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        dock.Children.Add(close);
        dock.Children.Add(dot);
        dock.Children.Add(name);
        var header = new Border
        {
            Child = dock,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 3, 4, 3),
            Margin = new Thickness(0, 0, 4, 0),
            MinWidth = 96,
            MaxWidth = 230,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
        };
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not DependencyObject d || !IsInside(d, close)) { ActivateTab(t); e.Handled = true; } };
        header.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { CloseTab(t); e.Handled = true; } };
        header.MouseEnter += (_, _) => { if (t != _tab) header.SetResourceReference(Border.BackgroundProperty, "ControlHoverBg"); };
        header.MouseLeave += (_, _) => StyleTabHeader(t);
        t.Header = header;
        t.HeaderText = name;
        t.HeaderDot = dot;
        int index = _tabs.IndexOf(t);
        _tabPanel.Children.Insert(Math.Clamp(index, 0, _tabPanel.Children.Count), header);
        UpdateTabHeader(t);
    }

    private static bool IsInside(DependencyObject d, DependencyObject ancestor)
    {
        for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == ancestor) return true;
        return false;
    }

    private void UpdateTabHeader(DocTab t)
    {
        if (t.Header == null) return;
        t.HeaderText.Text = DocNameOf(t);
        t.HeaderDot.Visibility = t.Dirty ? Visibility.Visible : Visibility.Collapsed;
        t.Header.ToolTip = (t.Path ?? t.ImportedFrom ?? DocNameOf(t)) + (t.Dirty ? "\nUnsaved changes" : "");
        System.Windows.Automation.AutomationProperties.SetName(t.Header, DocNameOf(t) + (t.Dirty ? ", unsaved changes" : ""));
        StyleTabHeader(t);
    }

    private void StyleTabHeader(DocTab t)
    {
        if (t.Header == null) return;
        if (t == _tab)
        {
            t.Header.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            t.Header.SetResourceReference(Border.BorderBrushProperty, "Accent");
            t.HeaderText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            t.HeaderText.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            t.Header.Background = Brushes.Transparent;
            t.Header.BorderBrush = Brushes.Transparent;
            t.HeaderText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            t.HeaderText.FontWeight = FontWeights.Normal;
        }
    }

    private void UpdateTabStrip()
    {
        DocTabsHost.Visibility = S.UseTabs || _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in _tabs) StyleTabHeader(t);
        _tab?.Header?.BringIntoView();
    }

    // ================================================================== new / switch / close

    /// <summary>Opens a new empty tab and switches to it.</summary>
    private DocTab NewTab()
    {
        var t = new DocTab { Box = CreateEditorBox(), Layout = PageLayout.FromSettings(S) };
        t.Format = S.SaveFormat == DefaultSaveFormat.Txt ? DocFormat.Txt : DocFormat.Rtf;
        _tabs.Insert(_tab == null ? _tabs.Count : _tabs.IndexOf(_tab) + 1, t);
        BuildTabHeader(t);
        ActivateTab(t);
        SetDocument(DocumentService.NewDocument());
        UpdateTabStrip();
        return t;
    }

    private void ActivateTab(DocTab t)
    {
        if (t == null || (t == _tab && ReferenceEquals(PageBorder.Child, t.Box))) return;
        if (_tab != null && _tab != t)
        {
            _tab.ScrollV = EditorScroll.VerticalOffset;
            _tab.ScrollH = EditorScroll.HorizontalOffset;
            DetachAdorners(_tab);
        }
        _tab = t;
        PageBorder.Child = t.Box;
        Editor = t.Box;
        if (IsLoaded) AttachAdorners(t);
        UpdatePageLayout();
        Title = (t.Dirty ? "*" : "") + DocNameOf(t) + " - Draftly";
        UpdateTabStrip();
        if (!(_infoIsWindowLevel && InfoBar.Visibility == Visibility.Visible))
        {
            if (t.Info != null) ShowInfo(t.Info); else HideInfo();
        }
        UpdateFormattingUi();
        UpdateStatus();
        RefreshOverlays();
        var v = t.ScrollV; var h = t.ScrollH;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            EditorScroll.ScrollToVerticalOffset(v);
            EditorScroll.ScrollToHorizontalOffset(h);
            UpdateRuler();
        }));
        if (IsLoaded) Editor.Focus();
    }

    private void AttachAdorners(DocTab t)
    {
        var layer = AdornerLayer.GetAdornerLayer(t.Box);
        if (layer == null) return;
        t.CurrentLine ??= new CurrentLineAdorner(t.Box);
        t.Marks ??= new FormattingMarksAdorner(t.Box) { VisibleRegion = VisibleEditorRegion };
        t.LineNumbers ??= new LineNumberAdorner(t.Box) { VisibleRegion = VisibleEditorRegion };
        foreach (Adorner a in new Adorner[] { t.CurrentLine, t.Marks, t.LineNumbers })
        {
            var existing = layer.GetAdorners(t.Box);
            if (existing == null || !existing.Contains(a)) layer.Add(a);
        }
        t.Marks.ShowMarks = S.ShowFormattingMarks;
        t.CurrentLine.Visibility = S.HighlightCurrentLine ? Visibility.Visible : Visibility.Collapsed;
        t.LineNumbers.Show = S.ShowLineNumbers;
    }

    private void DetachAdorners(DocTab t)
    {
        var layer = AdornerLayer.GetAdornerLayer(PageBorder);
        if (layer == null) return;
        foreach (Adorner a in new Adorner[] { t.CurrentLine, t.Marks, t.LineNumbers })
            if (a != null) { try { layer.Remove(a); } catch { } }
    }

    private void NextTab(int dir)
    {
        if (_tabs.Count < 2) return;
        int i = (_tabs.IndexOf(_tab) + dir + _tabs.Count) % _tabs.Count;
        ActivateTab(_tabs[i]);
    }

    /// <summary>Closes a tab, asking to save first. Returns false if the user cancelled.</summary>
    private bool CloseTab(DocTab t, bool ask = true)
    {
        if (t == null) return true;
        if (ask && t.Dirty)
        {
            ActivateTab(t);
            if (!ConfirmDiscard()) return false;
        }
        if (_tabs.Count == 1)
            NewTab();   // keep a blank document open, like WordPad
        if (t == _tab)
        {
            int i = _tabs.IndexOf(t);
            ActivateTab(_tabs[i == _tabs.Count - 1 ? i - 1 : i + 1]);
        }
        DetachAdorners(t);
        RecoveryService.Clear(t.Key);
        _tabs.Remove(t);
        if (t.Header != null) _tabPanel.Children.Remove(t.Header);
        UpdateTabStrip();
        return true;
    }

    /// <summary>An untitled, unchanged, empty tab that a file can open into instead of adding another tab.</summary>
    private static bool IsReusable(DocTab t) =>
        t.Path == null && !t.Dirty && t.ImportedFrom == null && t.RecoveredFrom == null &&
        DocumentService.GetPlainText(t.Box.Document).Length == 0 &&
        !DocumentService.Walk(t.Box.Document.Blocks).Any(e => e is InlineUIContainer or BlockUIContainer or Table);

    /// <summary>Opens a file the way the user expects: a new tab (or the tab it's already in), or replacing the document when tabs are off.</summary>
    public void OpenFromUser(string path)
    {
        if (!S.UseTabs)
        {
            if (ConfirmDiscard()) OpenFile(path);
            return;
        }
        string full;
        try { full = System.IO.Path.GetFullPath(path); } catch { full = path; }
        var existing = _tabs.FirstOrDefault(x => x.Path != null && string.Equals(System.IO.Path.GetFullPath(x.Path), full, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { ActivateTab(existing); return; }
        bool created = false;
        if (!IsReusable(_tab)) { NewTab(); created = true; }
        var tab = _tab;
        if (!OpenFile(full) && created) CloseTab(tab, ask: false);
    }

    /// <summary>Files sent from another Draftly launch (double-clicked in File Explorer).</summary>
    private void ReceiveFiles(string[] files)
    {
        foreach (var f in files)
            if (File.Exists(f)) OpenFromUser(f);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;   // a reliable way to come to the front on Windows
        Topmost = false;
        Editor.Focus();
    }

    private void NewWindow()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FileAssociations.ExePath, "--new-window") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "New window", "Draftly couldn't open a new window.\n\n" + ex.Message, MessageDialog.Kind.Error);
        }
    }

    // ================================================================== drag and drop

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico" };

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>Dropping documents opens them (in tabs); dropping pictures on the page inserts them.</summary>
    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        e.Handled = true;
        foreach (var f in files)
        {
            if (ImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
            {
                var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor), true);
                if (pos != null) Editor.CaretPosition = pos;
                InsertPictureFile(f);
            }
            else if (File.Exists(f)) OpenFromUser(f);
        }
        Activate();
    }

    // ================================================================== startup & sessions

    private void StartupOpen(string[] files)
    {
        bool restored = false;
        if (S.RestoreTabs || S.KeepUnsavedOnClose) restored = RestoreSession();
        if (!restored && S.ReopenLastDocument && File.Exists(S.LastSessionFile)) OpenFile(S.LastSessionFile);
        foreach (var f in files) OpenFromUser(f);
        CheckForRecovery();
        if (S.OpenInExistingWindow) SingleInstance.Listen(received => Dispatcher.BeginInvoke(new Action(() => ReceiveFiles(received))));
        OfferDefaultApp();
    }

    private sealed class SessionTab
    {
        public string Path { get; set; }
        public DocFormat Format { get; set; }
        public int CodePage { get; set; }
        public bool Bom { get; set; }
        public string LineEnding { get; set; } = "\r\n";
        public bool MarkdownSource { get; set; }
        public string ContentFile { get; set; }
        public string ImportedFrom { get; set; }
        public string RecoveredFrom { get; set; }
    }

    private sealed class SessionData
    {
        public List<SessionTab> Tabs { get; set; } = new();
        public int Active { get; set; }
    }

    private static string SessionFolder => System.IO.Path.Combine(SettingsService.Folder, "Session");
    private static string SessionFile => System.IO.Path.Combine(SessionFolder, "session.json");

    /// <summary>Remembers the open tabs; with <paramref name="keepUnsaved"/>, also their unsaved changes.</summary>
    private void SaveSession(bool keepUnsaved)
    {
        try
        {
            if (Directory.Exists(SessionFolder)) Directory.Delete(SessionFolder, true);
            var data = new SessionData();
            int n = 0;
            foreach (var t in _tabs)
            {
                bool hasText = !IsReusable(t);
                var entry = new SessionTab
                {
                    Path = t.Path, Format = t.Format, LineEnding = t.LineEnding, MarkdownSource = t.MarkdownSource,
                    ImportedFrom = t.ImportedFrom, RecoveredFrom = t.RecoveredFrom,
                    CodePage = t.Encoding?.CodePage ?? 0, Bom = t.Encoding?.GetPreamble().Length > 0,
                };
                if (keepUnsaved && (t.Dirty || (t.Path == null && hasText)))
                {
                    Directory.CreateDirectory(SessionFolder);
                    entry.ContentFile = $"tab{n++}.rtf";
                    var doc = t.Box.Document;
                    string rtf = null;
                    DocumentService.WithBlackText(doc, () => rtf = RtfBaseline.ToRtf(new TextRange(doc.ContentStart, doc.ContentEnd), doc));
                    File.WriteAllBytes(System.IO.Path.Combine(SessionFolder, entry.ContentFile), Encoding.Latin1.GetBytes(rtf));
                }
                else if (t.Path == null) continue;   // untitled and not kept
                if (t == _tab) data.Active = data.Tabs.Count;
                data.Tabs.Add(entry);
            }
            Directory.CreateDirectory(SessionFolder);
            File.WriteAllText(SessionFile, JsonSerializer.Serialize(data));
        }
        catch { /* sessions are best-effort */ }
    }

    private static void ClearSession()
    {
        try { if (Directory.Exists(SessionFolder)) Directory.Delete(SessionFolder, true); } catch { }
    }

    /// <summary>Reopens the tabs from last time. Returns true if anything was restored.</summary>
    private bool RestoreSession()
    {
        SessionData data;
        try
        {
            if (!File.Exists(SessionFile)) return false;
            data = JsonSerializer.Deserialize<SessionData>(File.ReadAllText(SessionFile));
        }
        catch { ClearSession(); return false; }
        if (data?.Tabs == null || data.Tabs.Count == 0) { ClearSession(); return false; }

        var entries = S.UseTabs ? data.Tabs : new List<SessionTab> { data.Tabs[Math.Clamp(data.Active, 0, data.Tabs.Count - 1)] };
        DocTab active = null;
        int index = 0;
        foreach (var e in entries)
        {
            var content = e.ContentFile == null ? null : System.IO.Path.Combine(SessionFolder, e.ContentFile);
            try
            {
                if (content != null && File.Exists(content))
                {
                    if (!IsReusable(_tab)) NewTab();
                    var doc = DocumentService.LoadRtf(content);
                    _tab.Path = e.Path;
                    _tab.Format = e.Format;
                    _tab.LineEnding = e.LineEnding ?? "\r\n";
                    _tab.MarkdownSource = e.MarkdownSource;
                    _tab.ImportedFrom = e.ImportedFrom;
                    _tab.RecoveredFrom = e.RecoveredFrom;
                    _tab.Encoding = e.CodePage == 0 ? null : e.CodePage == 65001 ? new UTF8Encoding(e.Bom) : Encoding.GetEncoding(e.CodePage);
                    _tab.Layout = PageLayout.FromSettings(S);
                    SetDocument(doc);
                    SetDirty(true);
                }
                else if (e.Path != null && File.Exists(e.Path))
                {
                    OpenFromUser(e.Path);
                    if (e.MarkdownSource && _tab.Format == DocFormat.Md) ToggleMarkdownSource();
                }
                else continue;
                if (index == (S.UseTabs ? data.Active : 0)) active = _tab;
            }
            catch { }
            finally { index++; }
        }
        ClearSession();
        if (active != null) ActivateTab(active);
        return _tabs.Any(t => t.Path != null || t.Dirty);
    }

    // ================================================================== closing

    private bool _closingConfirmed;

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (!S.KeepUnsavedOnClose)
        {
            foreach (var t in _tabs.Where(x => x.Dirty).ToList())
            {
                ActivateTab(t);
                if (!ConfirmDiscard()) { e.Cancel = true; return; }
            }
        }
        _closingConfirmed = true;
        if (S.RestoreTabs || S.KeepUnsavedOnClose) SaveSession(S.KeepUnsavedOnClose);
        else ClearSession();
        SaveWindowPlacement();
        RecoveryService.Clear();
        _findWindow?.Close();
        CompositionTarget.Rendering -= SmoothScrollTick;
    }
}
