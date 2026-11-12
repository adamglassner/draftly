using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Draftly.Controls;
using Draftly.Dialogs;
using Draftly.Services;

namespace Draftly;

public partial class MainWindow : Window
{
    // ---- document state (per tab: see MainWindow.Tabs.cs) ----
    private int _suppressDirty;
    private string _pendingRecovery;      // recovery file offered in the info bar

    // ---- view state ----
    private double _zoom = 1.0;
    private bool _updatingUi;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private double _scrollTarget = double.NaN;
    private Action _infoAction;

    private static AppSettings S => SettingsService.Current;

    public MainWindow(string[] filesToOpen, bool newWindow = false)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        RestoreWindowPlacement();

        InitTabs();
        InitRibbonHide();
        PopulateFontLists();

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); UpdateStatus(); };
        _recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(1, S.AutoRecoveryMinutes)) };
        _recoveryTimer.Tick += (_, _) =>
        {
            if (!S.AutoRecovery) return;
            foreach (var t in _tabs.Where(x => x.Dirty)) RecoveryService.Write(t.Box.Document, t.Path, DocNameOf(t), t.Key);
        };

        SetDocument(DocumentService.NewDocument());
        _format = S.SaveFormat == DefaultSaveFormat.Txt ? DocFormat.Txt : DocFormat.Rtf;

        Ruler.IndentsChanged += Ruler_IndentsChanged;
        SettingsService.Changed += ApplySettings;
        ThemeManager.ThemeChanged += OnThemeChanged;
        PreviewKeyDown += Window_PreviewKeyDown;
        Closing += MainWindow_Closing;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        SizeChanged += (_, _) => UpdateRibbonDensity();

        Loaded += (_, _) =>
        {
            AttachAdorners(_tab);
            ApplySettings();
            SetZoom(S.DefaultZoom / 100.0);

            if (App.IsSelfTest)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => await RunSelfTestAsync(App.SelfTestOutput, App.SelfTestShots)));
                return;
            }
            if (newWindow) { foreach (var f in filesToOpen) OpenFromUser(f); OfferDefaultApp(); }
            else StartupOpen(filesToOpen);

            Editor.Focus();
            UpdateFormattingUi();
            UpdateStatus();
        };
    }

    // ================================================================== document plumbing

    private string DocName => DocNameOf(_tab);

    private void SetDocument(FlowDocument doc)
    {
        _suppressDirty++;
        try
        {
            doc.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
            doc.ClearValue(TextElement.ForegroundProperty);   // follow the theme (see NormaliseAutomaticColour)
            doc.ClearValue(FlowDocument.BackgroundProperty);
            DocumentService.NormaliseAutomaticColour(doc);
            DocumentService.PushDownDecorations(doc.Blocks);
            DocumentService.FixHighlightContrast(DocumentService.Walk(doc.Blocks));
            DocumentService.ApplyLinkColors(doc, ThemeManager.IsDarkPage);
            Editor.Document = doc;
            UpdatePageLayout();
            Editor.CaretPosition = doc.ContentStart;
            Editor.ScrollToHome();
            EditorScroll.ScrollToTop();
        }
        finally { _suppressDirty--; }
        SetDirty(false);
        _statusTimer.Stop(); _statusTimer.Start();
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        Title = (dirty ? "*" : "") + DocName + " - Draftly";
        if (!dirty) RecoveryService.Clear(_tab.Key);
        UpdateTabHeader(_tab);
    }

    // ================================================================== settings & theme

    private void ApplySettings()
    {
        _updatingUi = true;
        try
        {
            RulerHost.Visibility = S.ShowRuler ? Visibility.Visible : Visibility.Collapsed;
            StatusBar.Visibility = S.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
            ShowRulerBtn.IsChecked = S.ShowRuler;
            ShowStatusBtn.IsChecked = S.ShowStatusBar;
            ShowMarksBtn.IsChecked = S.ShowFormattingMarks;
            WrapBox.SelectedIndex = (int)S.WordWrap;
            UnitsBox.SelectedIndex = (int)S.Units;
            ThemeLightBtn.IsChecked = S.Theme == ThemeMode.Light;
            ThemeDarkBtn.IsChecked = S.Theme == ThemeMode.Dark;
            ThemeSystemBtn.IsChecked = S.Theme == ThemeMode.System;

            UpdateRibbonDensity();
            ApplyInterfaceStyle();
            ApplyRibbonDisplay();
            ShowLineNumbersBtn.IsChecked = S.ShowLineNumbers;
            foreach (var t in _tabs)
            {
                t.Box.SpellCheck.IsEnabled = S.SpellCheck;
                t.Box.AutoWordSelection = S.SelectWholeWords;
                if (t.Marks != null) t.Marks.ShowMarks = S.ShowFormattingMarks;
                if (t.CurrentLine != null) t.CurrentLine.Visibility = S.HighlightCurrentLine ? Visibility.Visible : Visibility.Collapsed;
                if (t.LineNumbers != null) t.LineNumbers.Show = S.ShowLineNumbers;
            }
            Ruler.Unit = S.Units;
            UpdateTabStrip();
            _recoveryTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, S.AutoRecoveryMinutes));
            if (S.AutoRecovery) _recoveryTimer.Start(); else { _recoveryTimer.Stop(); RecoveryService.Clear(); }
        }
        finally { _updatingUi = false; }
        UpdatePageLayout();
        RefreshOverlays();
    }

    /// <summary>On narrow windows the small toolbar buttons drop their labels so every group still fits.</summary>
    private void UpdateRibbonDensity()
    {
        bool compact = ActualWidth > 0 && ActualWidth < 1220;
        Resources["RibbonLabelVisibility"] = compact || !S.ShowRibbonLabels ? Visibility.Collapsed : Visibility.Visible;
        FontFamilyBox.Width = compact ? 104 : 170;
        FontSizeBox.Width = compact ? 64 : 68;
        if (_classicFont != null) _classicFont.Width = ActualWidth > 0 && ActualWidth < 1100 ? 120 : 170;
    }

    private void OnThemeChanged()
    {
        _updatingUi = true;
        ThemeLightBtn.IsChecked = S.Theme == ThemeMode.Light;
        ThemeDarkBtn.IsChecked = S.Theme == ThemeMode.Dark;
        ThemeSystemBtn.IsChecked = S.Theme == ThemeMode.System;
        _updatingUi = false;
        foreach (var t in _tabs) DocumentService.ApplyLinkColors(t.Box.Document, ThemeManager.IsDarkPage);
        Ruler.Refresh();
        RefreshOverlays();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg == WM_SETTINGCHANGE && S.Theme == ThemeMode.System && lParam != IntPtr.Zero)
        {
            var area = Marshal.PtrToStringUni(lParam);
            if (area == "ImmersiveColorSet")
                Dispatcher.BeginInvoke(new Action(() => ThemeManager.Apply(S)));
        }
        return IntPtr.Zero;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        S.Theme = sender == ThemeLightBtn ? ThemeMode.Light : sender == ThemeDarkBtn ? ThemeMode.Dark : ThemeMode.System;
        SettingsService.Commit();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings(string page = null)
    {
        new SettingsWindow(this, page).ShowDialog();
        Editor.Focus();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePanel == null) return;
        HomePanel.Visibility = TabHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewPanel.Visibility = TabView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowRuler_Click(object sender, RoutedEventArgs e) { S.ShowRuler = ShowRulerBtn.IsChecked == true; SettingsService.Commit(); }
    private void ShowStatus_Click(object sender, RoutedEventArgs e) { S.ShowStatusBar = ShowStatusBtn.IsChecked == true; SettingsService.Commit(); }
    private void ShowMarks_Click(object sender, RoutedEventArgs e) { S.ShowFormattingMarks = ShowMarksBtn.IsChecked == true; SettingsService.Commit(); }
    private void ShowLineNumbers_Click(object sender, RoutedEventArgs e) { S.ShowLineNumbers = ShowLineNumbersBtn.IsChecked == true; SettingsService.Commit(); }

    private void WrapBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || WrapBox.SelectedIndex < 0) return;
        S.WordWrap = (WrapMode)WrapBox.SelectedIndex;
        SettingsService.Commit();
    }

    private void UnitsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || UnitsBox.SelectedIndex < 0) return;
        S.Units = (MeasureUnit)UnitsBox.SelectedIndex;
        SettingsService.Commit();
    }

    // ================================================================== page layout & zoom

    private const double PageGap = 24; // PageHost horizontal margin

    private void UpdatePageLayout()
    {
        if (Editor?.Document == null) return;
        var doc = Editor.Document;
        double width;
        Thickness padding;
        double minHeight;

        switch (S.WordWrap)
        {
            case WrapMode.WrapToWindow:
                double avail = (EditorScroll.ViewportWidth > 0 ? EditorScroll.ViewportWidth : EditorScroll.ActualWidth) / _zoom - PageGap * 2 - 2;
                width = Math.Max(320, avail);
                double side = Math.Min(_layout.MarginLeft, Math.Max(24, width * 0.06));
                padding = new Thickness(side, Math.Min(_layout.MarginTop, 48), side, 48);
                minHeight = Math.Max(200, EditorScroll.ActualHeight / _zoom - 70);
                break;
            case WrapMode.NoWrap:
                width = Math.Max(_layout.PaperWidth, 4000);
                padding = new Thickness(_layout.MarginLeft, _layout.MarginTop, _layout.MarginRight, _layout.MarginBottom);
                minHeight = _layout.PaperHeight;
                break;
            default:
                width = _layout.PaperWidth;
                padding = new Thickness(_layout.MarginLeft, _layout.MarginTop, _layout.MarginRight, _layout.MarginBottom);
                minHeight = _layout.PaperHeight;
                break;
        }

        // Line numbers sit in the left margin, so make sure there's room for them.
        if (S.ShowLineNumbers && padding.Left < 46) padding = new Thickness(46, padding.Top, padding.Right, padding.Bottom);

        Editor.Width = width;
        Editor.MinHeight = minHeight;
        doc.PageWidth = width;
        doc.PagePadding = padding;
        doc.ColumnWidth = width;
        _textWidth = Math.Max(48, width - padding.Left - padding.Right);
        _padLeft = padding.Left;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRuler));
    }

    private double _textWidth, _padLeft;

    private void UpdateRuler()
    {
        if (RulerHost.Visibility != Visibility.Visible || !IsLoaded) return;
        try
        {
            var origin = PageBorder.TranslatePoint(new Point(0, 0), Ruler);
            Ruler.PageLeft = origin.X;
            Ruler.PageWidth = Editor.Width;
            Ruler.MarginLeft = _padLeft;
            Ruler.TextWidth = _textWidth;
            Ruler.Zoom = _zoom;
            Ruler.Unit = S.Units;

            var p = Editor.Selection.Start.Paragraph;
            if (p != null)
            {
                Ruler.IndentsEnabled = true;
                Ruler.LeftIndent = Safe(p.Margin.Left);
                Ruler.RightIndent = Safe(p.Margin.Right);
                Ruler.FirstLineIndent = Safe(p.TextIndent);
            }
            else Ruler.IndentsEnabled = false;
            Ruler.Refresh();
        }
        catch { /* not laid out yet */ }
    }

    private static double Safe(double d) => double.IsNaN(d) || double.IsInfinity(d) ? 0 : d;

    private void Ruler_IndentsChanged(double left, double first, double right)
    {
        Editor.BeginChange();
        try
        {
            foreach (var p in SelectedParagraphs())
            {
                var m = p.Margin;
                p.Margin = new Thickness(left, Safe(m.Top), right, Safe(m.Bottom));
                p.TextIndent = first;
            }
        }
        finally { Editor.EndChange(); }
        Editor.Focus();
    }

    private void SetZoom(double zoom)
    {
        zoom = Math.Clamp(Math.Round(zoom, 2), 0.1, 5.0);
        _zoom = zoom;
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = zoom;
        ZoomLabel.Content = $"{Math.Round(zoom * 100)}%";
        _updatingUi = true;
        ZoomSlider.Value = zoom <= 1 ? (zoom - 0.1) / 0.9 * 50 : 50 + (zoom - 1) / 4 * 50;
        _updatingUi = false;
        if (S.WordWrap == WrapMode.WrapToWindow) UpdatePageLayout();
        else Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRuler));
        RefreshOverlays();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(EnsureCaretVisible));
    }

    private static readonly int[] ZoomSteps = { 10, 25, 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400, 500 };

    private void ZoomStep(int dir)
    {
        int cur = (int)Math.Round(_zoom * 100);
        int next = dir > 0 ? ZoomSteps.FirstOrDefault(z => z > cur, 500) : ZoomSteps.LastOrDefault(z => z < cur, 10);
        SetZoom(next / 100.0);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomStep(+1);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomStep(-1);
    private void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomPageWidth_Click(object sender, RoutedEventArgs e)
    {
        double w = EditorScroll.ViewportWidth - PageGap * 2 - 4;
        if (w > 100 && Editor.Width > 0) SetZoom(w / Editor.Width);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || ZoomTransform == null) return;
        double v = e.NewValue;
        double z = v <= 50 ? 0.1 + v / 50 * 0.9 : 1 + (v - 50) / 50 * 4;
        // Snap to 100% near the middle so it's easy to get back to.
        if (Math.Abs(z - 1) < 0.04) z = 1;
        SetZoom(z);
    }

    private void EditorScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (S.WordWrap == WrapMode.WrapToWindow) UpdatePageLayout();
        else Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRuler));
        RefreshOverlays();
    }

    private void EditorScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 || e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0) UpdateRuler();
        if (e.VerticalChange != 0) RefreshOverlays();
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0) _statusTimer.Start();
    }

    /// <summary>Clicking the grey area around the page puts the cursor at the nearest text, like a real page.</summary>
    private void EditorScroll_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PageBorder.IsMouseOver) return;
        if (e.OriginalSource is DependencyObject d && FindParent<System.Windows.Controls.Primitives.ScrollBar>(d) != null) return;
        var p = e.GetPosition(Editor);
        p.X = Math.Clamp(p.X, 1, Editor.ActualWidth - 1);
        p.Y = Math.Clamp(p.Y, 1, Editor.ActualHeight - 1);
        var pos = Editor.GetPositionFromPoint(p, true);
        Editor.Focus();
        if (pos != null) Editor.CaretPosition = pos;
        e.Handled = true;
    }

    private static T FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private Rect VisibleEditorRegion()
    {
        try
        {
            var topLeft = EditorScroll.TranslatePoint(new Point(0, 0), Editor);
            var bottomRight = EditorScroll.TranslatePoint(new Point(EditorScroll.ViewportWidth, EditorScroll.ViewportHeight), Editor);
            return new Rect(topLeft, bottomRight);
        }
        catch { return new Rect(0, 0, Editor.ActualWidth, Editor.ActualHeight); }
    }

    private void RefreshOverlays()
    {
        _marks?.Schedule();
        _currentLine?.Refresh();
        _tab?.LineNumbers?.Schedule();
    }

    // ================================================================== scrolling & caret

    private void EnsureCaretVisible()
    {
        try
        {
            var r = Editor.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
            if (r.IsEmpty) return;
            r.Inflate(8, 24);
            Editor.BringIntoView(r);
        }
        catch { }
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomStep(e.Delta > 0 ? +1 : -1);
            return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            EditorScroll.ScrollToHorizontalOffset(EditorScroll.HorizontalOffset - e.Delta);
            return;
        }
        double step = e.Delta * 0.9;
        if (!S.SmoothScrolling)
        {
            EditorScroll.ScrollToVerticalOffset(EditorScroll.VerticalOffset - step);
            return;
        }
        if (double.IsNaN(_scrollTarget)) { _scrollTarget = EditorScroll.VerticalOffset; CompositionTarget.Rendering += SmoothScrollTick; }
        _scrollTarget = Math.Clamp(_scrollTarget - step, 0, EditorScroll.ScrollableHeight);
    }

    private void SmoothScrollTick(object sender, EventArgs e)
    {
        double cur = EditorScroll.VerticalOffset;
        double diff = _scrollTarget - cur;
        if (Math.Abs(diff) < 0.5)
        {
            EditorScroll.ScrollToVerticalOffset(_scrollTarget);
            CompositionTarget.Rendering -= SmoothScrollTick;
            _scrollTarget = double.NaN;
            return;
        }
        EditorScroll.ScrollToVerticalOffset(cur + diff * 0.28);
    }

    /// <summary>Page Up / Page Down move by one screen of the outer scroller (the editor itself doesn't scroll).</summary>
    private void PageMove(bool down, bool extend)
    {
        var caret = Editor.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
        if (caret.IsEmpty) return;
        double page = EditorScroll.ViewportHeight / _zoom * 0.9;
        var target = new Point(caret.X, Math.Clamp(caret.Y + (down ? page : -page), 0, Editor.ActualHeight - 1));
        var pos = Editor.GetPositionFromPoint(target, true);
        if (pos == null) return;
        if (extend)
        {
            var anchor = Editor.Selection.Start.CompareTo(Editor.CaretPosition) == 0 ? Editor.Selection.End : Editor.Selection.Start;
            Editor.Selection.Select(anchor, pos);
        }
        else Editor.CaretPosition = pos;
        EditorScroll.ScrollToVerticalOffset(EditorScroll.VerticalOffset + (down ? 1 : -1) * EditorScroll.ViewportHeight * 0.9);
    }

    // ================================================================== editor events

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender != Editor || _tab == null) return;   // a background tab (or still starting up)
        if (_suppressDirty == 0 && !_dirty) SetDirty(true);
        _statusTimer.Stop();
        _statusTimer.Start();
        RefreshOverlays();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(EnsureCaretVisible));
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender != Editor || _tab == null) return;   // a background tab (or still starting up)
        UpdateFormattingUi();
        _currentLine?.Refresh();
        _tab?.LineNumbers?.Schedule();
        _statusTimer.Stop();
        _statusTimer.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRuler));
        if (Mouse.LeftButton != MouseButtonState.Pressed)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(EnsureCaretVisible));
    }

    // ================================================================== status bar

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.Compiled);

    private void UpdateStatus()
    {
        if (!IsLoaded || Editor.Document == null) return;
        var text = DocumentService.GetPlainText(Editor.Document);
        int words = WordRegex.Matches(text).Count;
        int chars = text.Count(c => c != '\r' && c != '\n');

        if (!Editor.Selection.IsEmpty)
        {
            var sel = Editor.Selection.Text;
            WordInfo.Text = $"{WordRegex.Matches(sel).Count:N0} of {words:N0} words";
        }
        else WordInfo.Text = words == 1 ? "1 word" : $"{words:N0} words";
        CharInfo.Text = !Editor.Selection.IsEmpty
            ? $"{Editor.Selection.Text.Count(c => c != '\r' && c != '\n'):N0} of {chars:N0} characters"
            : chars == 1 ? "1 character" : $"{chars:N0} characters";
        WordCountBtn.ToolTip = $"{chars:N0} characters. Click for details.";
        UpdateLinkStatus();

        try
        {
            var caret = Editor.CaretPosition;
            var lineStart = caret.GetLineStartPosition(0) ?? caret;
            int col = new TextRange(lineStart, caret).Text.Length + 1;
            caret.GetLineStartPosition(-int.MaxValue, out int moved);
            int line = -moved + 1;
            LineInfo.Text = $"Line {line:N0}, Col {col}";

            var r = caret.GetCharacterRect(LogicalDirection.Forward);
            double textHeight = _layout.TextHeight;
            double contentHeight = Math.Max(1, Editor.ActualHeight - _layout.MarginTop - _layout.MarginBottom);
            int pages = Math.Max(1, (int)Math.Ceiling(contentHeight / textHeight - 0.02));
            int page = r.IsEmpty ? 1 : Math.Clamp((int)((r.Top - _layout.MarginTop) / textHeight) + 1, 1, pages);
            PageInfo.Text = S.WordWrap == WrapMode.WrapToRuler ? $"Page {page} of {pages}" : $"Page {page}";
        }
        catch { }

        FormatInfo.Text = _format switch
        {
            DocFormat.Txt => "Plain text" + (_encoding != null ? " · " + DocumentService.EncodingLabel(_encoding) : ""),
            DocFormat.Md => _tab.MarkdownSource ? "Markdown (source)" : "Markdown",
            DocFormat.Docx => "Word document (imported)",
            _ => "Rich Text",
        };
        bool plain = _format is DocFormat.Txt or DocFormat.Md;
        LineEndingBtn.Visibility = plain ? Visibility.Visible : Visibility.Collapsed;
        LineEndingInfo.Text = DocumentService.LineEndingLabel(_tab.LineEnding);
        MarkdownViewBtn.Visibility = _format == DocFormat.Md ? Visibility.Visible : Visibility.Collapsed;
        MarkdownViewInfo.Text = _tab.MarkdownSource ? "Show formatted" : "Show source";
    }

    private void WordCount_Click(object sender, RoutedEventArgs e)
    {
        var text = DocumentService.GetPlainText(Editor.Document);
        var sel = Editor.Selection.IsEmpty ? null : Editor.Selection.Text;
        string Stats(string t) =>
            $"Words: {WordRegex.Matches(t).Count:N0}\n" +
            $"Characters (no spaces): {t.Count(c => !char.IsWhiteSpace(c)):N0}\n" +
            $"Characters (with spaces): {t.Count(c => c != '\r' && c != '\n'):N0}\n" +
            $"Paragraphs: {t.Split('\n').Count(l => l.Trim().Length > 0):N0}";
        var msg = "Document\n" + Stats(text);
        if (sel != null) msg += "\n\nSelection\n" + Stats(sel);
        MessageDialog.Show(this, "Word count", msg);
        Editor.Focus();
    }

    // ================================================================== info bar

    private bool _infoIsWindowLevel;   // a prompt (with a button) rather than a note about the current tab

    private void ShowInfo(string text, string actionLabel = null, Action action = null)
    {
        _infoIsWindowLevel = action != null;
        if (!_infoIsWindowLevel && _tab != null) _tab.Info = text;
        InfoText.Text = text;
        _infoAction = action;
        InfoAction.Visibility = action != null ? Visibility.Visible : Visibility.Collapsed;
        InfoAction.Content = actionLabel;
        InfoBar.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRuler));
    }

    private void HideInfo()
    {
        if (!_infoIsWindowLevel && _tab != null) _tab.Info = null;
        _infoIsWindowLevel = false;
        InfoBar.Visibility = Visibility.Collapsed;
        _infoAction = null;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateRuler));
    }

    private void InfoAction_Click(object sender, RoutedEventArgs e)
    {
        var a = _infoAction;
        HideInfo();
        a?.Invoke();
    }

    private void InfoClose_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingRecovery != null)
        {
            RecoveryService.Discard(_pendingRecovery);
            _pendingRecovery = null;
        }
        HideInfo();
    }

    // ================================================================== recovery

    private void CheckForRecovery()
    {
        var orphans = RecoveryService.FindOrphans();
        if (orphans.Count == 0) return;
        var (rtf, info) = orphans[0];
        // Older leftovers are superseded by the newest one.
        foreach (var o in orphans.Skip(1)) RecoveryService.Discard(o.RtfPath);
        _pendingRecovery = rtf;
        ShowInfo($"Draftly found unsaved work from {info.SavedAt:g} ({info.DisplayName}). Restore it? Close this bar to delete it.",
            "Restore", () =>
            {
                if (S.UseTabs) { if (!IsReusable(_tab)) NewTab(); }
                else if (!ConfirmDiscard()) return;
                try
                {
                    var doc = DocumentService.LoadRtf(rtf);
                    _path = null;
                    _importedFrom = null;
                    _recoveredFrom = info.OriginalPath ?? info.DisplayName;
                    _format = DocFormat.Rtf;
                    _encoding = null;
                    SetDocument(doc);
                    SetDirty(true);
                    RecoveryService.Discard(rtf);
                    _pendingRecovery = null;
                    RecoveryService.Write(Editor.Document, null, DocName, _tab.Key);
                }
                catch (Exception ex)
                {
                    MessageDialog.Show(this, "Restore", "The recovered document couldn't be opened.\n\n" + ex.Message, MessageDialog.Kind.Error);
                }
            });
    }

    /// <summary>Called by the crash handler.</summary>
    public void EmergencySave()
    {
        foreach (var t in _tabs.Where(x => x.Dirty)) RecoveryService.Write(t.Box.Document, t.Path, DocNameOf(t), t.Key);
    }

    // ================================================================== window placement & closing

    private void RestoreWindowPlacement()
    {
        if (!S.RememberWindow) return;
        Width = S.WinWidth;
        Height = S.WinHeight;
        if (!double.IsNaN(S.WinLeft) && !double.IsNaN(S.WinTop))
        {
            // Only restore the position if it's still on a screen.
            var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (vs.IntersectsWith(new Rect(S.WinLeft, S.WinTop, 200, 100)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = S.WinLeft;
                Top = S.WinTop;
            }
        }
        if (S.WinMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        S.WinLeft = b.Left; S.WinTop = b.Top; S.WinWidth = b.Width; S.WinHeight = b.Height;
        S.WinMaximized = WindowState == WindowState.Maximized;
        S.LastSessionFile = _path ?? "";
        SettingsService.Save();
    }
}

/// <summary>Soft band behind the line the caret is on (optional, see Settings › Appearance).</summary>
public sealed class CurrentLineAdorner : Adorner
{
    private readonly RichTextBox _box;
    private Rect _line = Rect.Empty;

    public CurrentLineAdorner(RichTextBox box) : base(box)
    {
        _box = box;
        IsHitTestVisible = false;
    }

    public void Refresh()
    {
        if (Visibility != Visibility.Visible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            try
            {
                var r = _box.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                _line = r.IsEmpty ? Rect.Empty : new Rect(0, r.Top - 1, _box.ActualWidth, r.Height + 2);
            }
            catch { _line = Rect.Empty; }
            InvalidateVisual();
        }));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_line.IsEmpty || !_box.Selection.IsEmpty) return;
        var brush = (Brush)FindResource("AccentSoft");
        dc.DrawRectangle(brush, null, _line);
    }
}
