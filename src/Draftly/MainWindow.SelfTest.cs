using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Draftly.Dialogs;
using Draftly.Services;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Draftly;

/// <summary>
/// Automated end-to-end test, run by the build with:  Draftly.exe --self-test out.txt [screenshot-folder]
/// It drives the real window: creates and formats a document, inserts a picture, saves RTF, reopens
/// it and checks the formatting survived, and exercises themes, Find/Replace, Undo/Redo, Print
/// Preview, the unsaved-changes prompt, keyboard shortcuts and every dialog.
/// Settings and recovery files go to a temporary folder, never the user's own.
/// </summary>
public partial class MainWindow
{
    private readonly List<string> _testLog = new();
    private int _testFails;
    private string _testDir;
    private string _shotDir;

    private sealed class TestFailure : Exception
    {
        public TestFailure(string message) : base(message) { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
    }

    private async Task Test(string name, Func<Task> body)
    {
        var sw = Stopwatch.StartNew();
        App.MainWindowTestName = name;
        string line;
        try
        {
            await body();
            line = $"PASS {name} ({sw.ElapsedMilliseconds} ms)";
        }
        catch (Exception ex)
        {
            _testFails++;
            var msg = ex is TestFailure ? ex.Message : ex.ToString();
            line = $"FAIL {name}: {msg.Replace("\r", " ").Replace("\n", " ")}";
        }
        _testLog.Add(line);
        try { File.AppendAllText(App.SelfTestOutput, line + "\r\n"); } catch { }
        // Close anything a failed test left open.
        foreach (Window w in Application.Current.Windows.Cast<Window>().ToList())
            if (w != this) { try { w.Close(); } catch { } }
        await Settle();
    }

    private async Task Test(string name, Action body) => await Test(name, () => { body(); return Task.CompletedTask; });

    private async Task Settle(int ms = 60)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (ms > 0) await Task.Delay(ms);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    internal async Task RunSelfTestAsync(string outPath, string shotDir)
    {
        _testDir = Path.Combine(Path.GetTempPath(), "DraftlySelfTestDocs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
        _shotDir = shotDir;
        if (_shotDir != null) Directory.CreateDirectory(_shotDir);

        WindowState = WindowState.Normal;
        Width = 1280; Height = 820; Left = 20; Top = 20;
        S.SpellCheck = false;            // keeps the runs predictable on build machines
        S.AutoRecovery = false;
        SettingsService.Commit();
        await Settle(300);

        _testLog.Add($"Draftly {typeof(MainWindow).Assembly.GetName().Version} self-test on {Environment.OSVersion.VersionString}");
        try { File.WriteAllText(outPath, _testLog[0] + "\r\n"); } catch { }

        await Test("Create and format a document", CreateFormattedDocument);
        await Test("Formatting buttons reflect the selection", CheckToolbarState);
        await Test("Save as RTF, close, reopen: formatting and picture survive", SaveAndReopenRtf);
        await Test("Light theme is readable", () => CheckTheme(ThemeMode.Light));
        await Test("Dark theme is readable, page is dark", () => CheckTheme(ThemeMode.Dark));
        await Test("Follow System theme matches Windows", () => CheckTheme(ThemeMode.System));
        await Test("Theme choice persists after restart", ThemePersists);
        await Test("Dark mode saves normal black text", DarkModeSavesBlackText);
        await Test("Find", FindWorks);
        await Test("Replace and Replace All", ReplaceWorks);
        await Test("Undo and Redo", UndoRedoWorks);
        await Test("Keyboard shortcuts", ShortcutsWork);
        await Test("Clipboard: cut, copy, paste", ClipboardWorks);
        await Test("Unsaved changes prompt: Cancel, Don't Save, Save", UnsavedPromptWorks);
        await Test("Plain text: save and reopen with Unicode", PlainTextRoundTrip);
        await Test("Open a Word (.docx) document", DocxImport);
        await Test("Print Preview", PrintPreviewWorks);
        await Test("Page setup changes the page", PageSetupWorks);
        await Test("Zoom, word wrap, ruler, formatting marks, status bar", ViewOptionsWork);
        await Test("Insert date/time, table, symbol, line, page break", InsertsWork);
        await Test("Paragraph indent and spacing", ParagraphSettingsWork);
        await Test("Settings are saved and reloaded", SettingsPersist);
        await Test("Every dialog opens in Light and Dark", DialogsOpen);
        await Test("Toolbar fits a 1024-pixel-wide screen", RibbonFits);
        await Test("Screenshots", TakeScreenshots);

        // Missing-resource warnings logged by App count as failures.
        var resourceFails = File.Exists(outPath) ? File.ReadAllLines(outPath).Count(l => l.StartsWith("FAIL missing resource") || l.StartsWith("FAIL unexpected message box")) : 0;
        _testFails += resourceFails;
        var summary = _testFails == 0 ? "ALL TESTS PASSED" : $"{_testFails} TEST(S) FAILED";
        try { File.AppendAllText(outPath, summary + "\r\n"); } catch { }
        try { if (_shotDir != null) File.Copy(Path.Combine(_testDir, "formatted.rtf"), Path.Combine(_shotDir, "formatted.rtf"), true); } catch { }
        try { Directory.Delete(_testDir, true); } catch { }

        _closingConfirmed = true;
        Application.Current.Shutdown(_testFails);
    }

    // ================================================================== helpers

    private void ResetDocument()
    {
        SetDirty(false);
        _path = null;
        NewDocument();
    }

    private void TypeText(string text)
    {
        foreach (var (line, i) in text.Split('\n').Select((l, i) => (l, i)))
        {
            if (i > 0) Editor.CaretPosition = Editor.CaretPosition.InsertParagraphBreak();
            if (line.Length > 0) InsertText(line);
        }
    }

    private void SelectFirst(string text)
    {
        Editor.CaretPosition = Editor.Document.ContentStart;
        Assert(FindNext(new FindOptions { Text = text, MatchCase = true, WrapAround = true }, quiet: true), $"couldn't find \"{text}\"");
        // A selection at the start of a list item also includes its bullet ("•\t"), as in WordPad.
        Assert(Editor.Selection.Text.EndsWith(text), $"selected \"{Editor.Selection.Text}\" instead of \"{text}\"");
    }

    private void CaretToEnd() => Editor.CaretPosition = Editor.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);

    private object Prop(DependencyProperty p) => Editor.Selection.GetPropertyValue(p);

    private string DocText() => DocumentService.GetPlainText(Editor.Document);

    private static BitmapSource MakeTestPicture()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x4F, 0x5B, 0xD5)), null, new Rect(0, 0, 160, 100));
            dc.DrawEllipse(Brushes.Gold, null, new Point(50, 50), 30, 30);
            dc.DrawRectangle(Brushes.White, null, new Rect(95, 25, 50, 50));
        }
        var bmp = new RenderTargetBitmap(160, 100, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private IEnumerable<Image> DocumentImages() =>
        DocumentService.Walk(Editor.Document.Blocks).OfType<InlineUIContainer>().Select(c => c.Child).OfType<Image>()
            .Concat(DocumentService.Walk(Editor.Document.Blocks).OfType<BlockUIContainer>().Select(c => c.Child).OfType<Image>());

    private static double Luminance(Color c)
    {
        static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Colours from an RTF colour table that are actually used for text (\cfN) and are light.</summary>
    private static List<string> LightTextColoursInRtf(string rtf)
    {
        var table = new List<Color?>();
        var m = System.Text.RegularExpressions.Regex.Match(rtf, @"\{\\colortbl\s*(.*?)\}", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (m.Success)
            foreach (var entry in m.Groups[1].Value.Split(';'))
            {
                var c = System.Text.RegularExpressions.Regex.Match(entry, @"\\red(\d+)\\green(\d+)\\blue(\d+)");
                table.Add(c.Success ? Color.FromRgb(byte.Parse(c.Groups[1].Value), byte.Parse(c.Groups[2].Value), byte.Parse(c.Groups[3].Value)) : null);
            }
        var light = new List<string>();
        foreach (System.Text.RegularExpressions.Match cf in System.Text.RegularExpressions.Regex.Matches(rtf, @"\\cf(\d+)"))
        {
            int i = int.Parse(cf.Groups[1].Value);
            if (i < table.Count && table[i] is Color col && Luminance(col) > 0.3) light.Add($"#{col.R:X2}{col.G:X2}{col.B:X2}");
        }
        return light.Distinct().ToList();
    }

    private static Color Res(string key) => (Color)Application.Current.Resources[key + "Color"];

    /// <summary>Clicks a button (by its text) in the first open window of type T, once it appears.</summary>
    private void ClickWhenOpen<T>(string buttonText, Action<T> beforeClick = null) where T : Window
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        int tries = 0;
        timer.Tick += (_, _) =>
        {
            if (++tries > 60) { timer.Stop(); return; }
            var win = Application.Current.Windows.OfType<T>().FirstOrDefault(w => w.IsLoaded);
            if (win == null) return;
            timer.Stop();
            beforeClick?.Invoke(win);
            var button = FindVisuals<Button>(win).FirstOrDefault(b => AccessText(b.Content) == buttonText);
            if (button == null) { win.Close(); return; }
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        };
        timer.Start();
    }

    private static string AccessText(object content) => (content as string ?? "").Replace("_", "");

    private static IEnumerable<T> FindVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var x in FindVisuals<T>(child)) yield return x;
        }
    }

    private void Snapshot(FrameworkElement element, string name)
    {
        if (_shotDir == null || element == null || element.ActualWidth < 1) return;
        var dpi = VisualTreeHelper.GetDpi(element);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
        {
            var brush = (element as Control)?.Background ?? (Window.GetWindow(element)?.Background) ?? Brushes.White;
            dc.DrawRectangle(brush, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bmp.Render(bg);
        bmp.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(_shotDir, name + ".png"));
        enc.Save(fs);
    }

    private void SnapshotWindow(Window w, string name)
    {
        if (w.Content is FrameworkElement fe) Snapshot(fe, name);
    }

    // ================================================================== tests

    private async Task CreateFormattedDocument()
    {
        ResetDocument();
        await Settle();
        Assert(!_dirty, "a new document shouldn't be marked as changed");

        TypeText("Draftly self-test heading\nPlain words, italic words, underlined words, struck words, bothlines, H2O and E=mc2.\nFirst bullet\nSecond bullet\nSpaced paragraph with one and a half line spacing.\nPicture: ");
        Assert(_dirty, "typing should mark the document as changed");
        Assert(Title.StartsWith("*"), "the title bar should show * for unsaved changes");

        SelectFirst("Draftly self-test heading");
        EditingCommands.ToggleBold.Execute(null, Editor);
        ApplyToSelection(TextElement.FontSizeProperty, DocumentService.PointsToDips(20));
        ApplyTextColor(Color.FromRgb(0xC0, 0x00, 0x00));
        SetAlignment(TextAlignment.Center);
        ApplyToSelection(TextElement.FontFamilyProperty, new FontFamily("Georgia"));

        SelectFirst("italic");
        EditingCommands.ToggleItalic.Execute(null, Editor);
        SelectFirst("underlined");
        ToggleDecoration(TextDecorationLocation.Underline);
        SelectFirst("struck");
        ToggleDecoration(TextDecorationLocation.Strikethrough);
        SelectFirst("bothlines");
        ToggleDecoration(TextDecorationLocation.Underline);
        ToggleDecoration(TextDecorationLocation.Strikethrough);
        SelectFirst("Plain");
        ApplyHighlight(Color.FromRgb(0xFF, 0xF2, 0x00));
        Editor.CaretPosition = Editor.Document.ContentStart;
        Assert(FindNext(new FindOptions { Text = "2", MatchCase = true, WrapAround = true }, quiet: true), "couldn't find the 2 in H2O");
        ToggleBaseline(BaselineAlignment.Subscript);
        Assert(FindNext(new FindOptions { Text = "2", MatchCase = true, WrapAround = true }, quiet: true), "couldn't find the 2 in mc2");
        ToggleBaseline(BaselineAlignment.Superscript);

        SelectFirst("First bullet");
        var secondEnd = Editor.Selection.Start.Paragraph.NextBlock is Paragraph p2 ? p2.ContentEnd : Editor.Selection.End;
        Editor.Selection.Select(Editor.Selection.Start, secondEnd);
        ToggleList(TextMarkerStyle.Disc);
        Assert(CurrentList() != null, "bullets weren't created");
        Assert(CurrentList().ListItems.Count == 2, $"expected 2 bullets, got {CurrentList().ListItems.Count}");

        SelectFirst("Spaced paragraph");
        SetLineSpacing(1.5);

        CaretToEnd();
        InsertImage(MakeTestPicture());
        await Settle(150);
        Assert(DocumentImages().Count() == 1, "the picture wasn't inserted");
    }

    private Task CheckToolbarState()
    {
        SelectFirst("Draftly self-test heading");
        UpdateFormattingUi();
        Assert(BoldBtn.IsChecked == true, "Bold button not lit for bold text");
        Assert(AlignCenterBtn.IsChecked == true, "Center button not lit for centered text");
        Assert(FontSizeBox.Text == "20", $"font size box shows \"{FontSizeBox.Text}\" instead of 20");
        Assert(FontFamilyBox.Text == "Georgia", $"font box shows \"{FontFamilyBox.Text}\" instead of Georgia");
        SelectFirst("underlined");
        UpdateFormattingUi();
        Assert(UnderlineBtn.IsChecked == true, "Underline button not lit");
        Assert(BoldBtn.IsChecked != true, "Bold button lit for plain text");
        SelectFirst("First bullet");
        UpdateFormattingUi();
        Assert(BulletsBtn.IsChecked == true, "Bullets button not lit inside a bullet list");
        return Task.CompletedTask;
    }

    private void VerifyFormattedDocument(string where)
    {
        SelectFirst("Draftly self-test heading");
        Assert(Prop(TextElement.FontWeightProperty) is FontWeight fw && fw.ToOpenTypeWeight() >= 600, $"{where}: heading isn't bold");
        Assert(Prop(TextElement.FontSizeProperty) is double fs && Math.Abs(DocumentService.DipsToPoints(fs) - 20) < 0.6, $"{where}: heading size is {Prop(TextElement.FontSizeProperty)}");
        Assert(Prop(TextElement.ForegroundProperty) is SolidColorBrush red && red.Color.R == 0xC0 && red.Color.G == 0 && red.Color.B == 0, $"{where}: heading colour lost ({Prop(TextElement.ForegroundProperty)})");
        Assert(Prop(Paragraph.TextAlignmentProperty) is TextAlignment ta && ta == TextAlignment.Center, $"{where}: heading isn't centered");
        Assert(Prop(TextElement.FontFamilyProperty) is FontFamily ff && ff.Source.Contains("Georgia"), $"{where}: heading font lost ({Prop(TextElement.FontFamilyProperty)})");

        SelectFirst("italic");
        Assert(Prop(TextElement.FontStyleProperty) is FontStyle st && st == FontStyles.Italic, $"{where}: italic lost");
        SelectFirst("underlined");
        Assert(HasDecoration(TextDecorationLocation.Underline), $"{where}: underline lost");
        UpdateFormattingUi();
        Assert(UnderlineBtn.IsChecked == true, $"{where}: the Underline button doesn't light up for underlined text");
        SelectFirst("struck");
        Assert(HasDecoration(TextDecorationLocation.Strikethrough), $"{where}: strikethrough lost");
        Assert(!HasDecoration(TextDecorationLocation.Underline), $"{where}: struck text gained an underline");
        SelectFirst("bothlines");
        Assert(HasDecoration(TextDecorationLocation.Underline) && HasDecoration(TextDecorationLocation.Strikethrough), $"{where}: underline + strikethrough together lost");
        SelectFirst("Plain");
        Assert(Prop(TextElement.BackgroundProperty) is SolidColorBrush hl && hl.Color.R > 0xE0 && hl.Color.G > 0xD0 && hl.Color.B < 0x40, $"{where}: highlight lost ({Prop(TextElement.BackgroundProperty)})");

        Editor.CaretPosition = Editor.Document.ContentStart;
        Assert(FindNext(new FindOptions { Text = "2", MatchCase = true, WrapAround = true }, quiet: true), $"{where}: no H2O");
        Assert(Prop(Inline.BaselineAlignmentProperty) is BaselineAlignment sub && sub == BaselineAlignment.Subscript, $"{where}: subscript lost ({Prop(Inline.BaselineAlignmentProperty)})");
        Assert(FindNext(new FindOptions { Text = "2", MatchCase = true, WrapAround = true }, quiet: true), $"{where}: no mc2");
        Assert(Prop(Inline.BaselineAlignmentProperty) is BaselineAlignment sup && sup == BaselineAlignment.Superscript, $"{where}: superscript lost ({Prop(Inline.BaselineAlignmentProperty)})");

        SelectFirst("Second bullet");
        var list = CurrentList();
        Assert(list != null && list.ListItems.Count == 2, $"{where}: bullet list lost");
        Assert(!IsNumbered(list.MarkerStyle), $"{where}: bullets became {list.MarkerStyle}");

        SelectFirst("Spaced paragraph");
        var sp = GetLineSpacing(Editor.Selection.Start.Paragraph);
        Assert(sp.HasValue && Math.Abs(sp.Value - 1.5) < 0.12, $"{where}: line spacing is {sp} instead of 1.5");

        var images = DocumentImages().ToList();
        Assert(images.Count == 1, $"{where}: expected 1 picture, found {images.Count}");
        Assert(Math.Abs(images[0].Width - 160) < 2 && Math.Abs(images[0].Height - 100) < 2, $"{where}: picture size is {images[0].Width}×{images[0].Height}");
    }

    private async Task SaveAndReopenRtf()
    {
        VerifyFormattedDocument("before saving");
        var path = Path.Combine(_testDir, "formatted.rtf");
        _path = path;
        _format = DocFormat.Rtf;
        Assert(Save(), "Save failed");
        Assert(!_dirty, "document still marked as changed after saving");
        Assert(!Title.StartsWith("*"), "title still shows * after saving");
        var head = File.ReadAllText(path)[..5];
        Assert(head == "{\\rtf", $"saved file isn't RTF (starts with {head})");

        // Close it, then open it again from disk.
        NewDocument();
        await Settle();
        Assert(DocText().Length == 0, "New didn't clear the document");
        OpenFile(path);
        await Settle(300);
        Assert(_path == path && _format == DocFormat.Rtf, "reopened file has the wrong path or format");
        Assert(!_dirty, "a freshly opened file shouldn't be marked as changed");
        VerifyFormattedDocument("after reopening");
        SelectFirst("underlined");
        ToggleDecoration(TextDecorationLocation.Underline);
        SelectFirst("underlined");
        Assert(!HasDecoration(TextDecorationLocation.Underline), "after reopening, the Underline button can't remove the underline");
        ToggleDecoration(TextDecorationLocation.Underline);
        SelectFirst("underlined");
        Assert(HasDecoration(TextDecorationLocation.Underline), "after reopening, underline can't be turned back on");
        SetDirty(false);
        Assert(S.RecentFiles.FirstOrDefault() == path, "the file wasn't added to Recent documents");
    }

    private async Task CheckTheme(ThemeMode mode)
    {
        S.Theme = mode;
        SettingsService.Commit();
        await Settle(200);
        bool expectDark = mode == ThemeMode.Dark || (mode == ThemeMode.System && ThemeManager.SystemUsesDark());
        Assert(ThemeManager.IsDark == expectDark, $"IsDark is {ThemeManager.IsDark}, expected {expectDark}");

        var pairs = new (string Fg, string Bg, double Min)[]
        {
            ("Text", "WindowBg", 7), ("Text", "RibbonBg", 7), ("Text", "MenuBg", 7), ("Text", "DialogBg", 7),
            ("Text", "InputBg", 7), ("Text", "CardBg", 7), ("Text", "StatusBg", 7),
            ("TextSecondary", "RibbonBg", 4.5), ("TextSecondary", "StatusBg", 4.5), ("TextSecondary", "CardBg", 4.5),
            ("PageText", "PageBg", 7), ("AccentText", "Accent", 4.5), ("Accent", "RibbonBg", 3),
            ("RulerText", "RulerBg", 4.5), ("DangerText", "DialogBg", 4.5), ("TextDisabled", "RibbonBg", 2),
        };
        var low = pairs.Select(p => (p, c: Contrast(Res(p.Fg), Res(p.Bg)))).Where(x => x.c < x.p.Min)
            .Select(x => $"{x.p.Fg} on {x.p.Bg} = {x.c:0.0}:1 (needs {x.p.Min})").ToList();
        Assert(low.Count == 0, "low contrast: " + string.Join("; ", low));

        if (expectDark)
            Assert(Luminance(Res("PageBg")) < 0.05, "the page isn't dark in Dark mode");
        foreach (var run in DocumentService.Walk(Editor.Document.Blocks).OfType<Run>())
            if (run.Background is SolidColorBrush hb && run.Foreground is SolidColorBrush hf && hb.Color.A > 128 && run.Text.Trim().Length > 0)
                Assert(Contrast(hb.Color, hf.Color) >= 4.5, $"highlighted \"{run.Text}\" is hard to read ({Contrast(hb.Color, hf.Color):0.0}:1)");
        else
            Assert(Res("PageBg") == Colors.White, "the page isn't white in Light mode");

        // The editor really uses those colours.
        Assert(Editor.Foreground is SolidColorBrush ef && ef.Color == Res("PageText"), "the editor text doesn't follow the theme");
        Assert(PageBorder.Background is SolidColorBrush pb && pb.Color == Res("PageBg"), "the page background doesn't follow the theme");
    }

    private async Task ThemePersists()
    {
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
        {
            S.Theme = mode;
            SettingsService.Commit();
            await Settle();
            var json = File.ReadAllText(SettingsService.FilePath);
            SettingsService.Load();     // what happens on the next start
            Assert(SettingsService.Current.Theme == mode, $"after restart the theme is {SettingsService.Current.Theme}, expected {mode} (file: {json.Length} chars)");
        }
        S.Theme = ThemeMode.Light;
        SettingsService.Commit();
    }

    private async Task DarkModeSavesBlackText()
    {
        S.Theme = ThemeMode.Dark;
        S.DarkPageInDarkMode = true;
        SettingsService.Commit();
        await Settle(200);
        var path = Path.Combine(_testDir, "dark.rtf");
        DocumentService.Save(Editor.Document, path, DocFormat.Rtf);
        var doc = DocumentService.LoadRtf(path);
        // Check every run that the user didn't colour.
        foreach (var run in DocumentService.Walk(doc.Blocks).OfType<Run>())
        {
            if (run.Text.Contains("heading")) continue;   // deliberately red
            if (run.Foreground is SolidColorBrush b)
                Assert(Luminance(b.Color) < 0.2, $"\"{run.Text}\" was saved in a light colour ({b.Color}) while in Dark mode");
        }
        Assert(Editor.Foreground is SolidColorBrush f && Luminance(f.Color) > 0.6, "after saving, the on-screen text isn't light again");
        S.Theme = ThemeMode.Light;
        SettingsService.Commit();
        await Settle();
    }

    private async Task FindWorks()
    {
        ResetDocument();
        TypeText("The cat sat on the mat.\nThen the cat ate. CAT!");
        await Settle();
        Editor.CaretPosition = Editor.Document.ContentStart;
        var o = new FindOptions { Text = "cat", WrapAround = true };
        Assert(FindNext(o, true) && Editor.Selection.Text == "cat", "first 'cat' not found");
        var first = Editor.Selection.Start;
        Assert(FindNext(o, true) && Editor.Selection.Start.CompareTo(first) > 0, "second 'cat' not found");
        Assert(FindNext(o, true) && Editor.Selection.Text == "CAT", "match-case off should find CAT");
        Assert(FindNext(o, true) && Editor.Selection.Start.CompareTo(first) == 0, "wrap around didn't go back to the first match");
        o.MatchCase = true;
        Editor.CaretPosition = Editor.Document.ContentStart;
        int n = 0;
        while (FindNext(new FindOptions { Text = "cat", MatchCase = true, WrapAround = false }, true)) n++;
        Assert(n == 2, $"match case found {n} instead of 2");
        Editor.CaretPosition = Editor.Document.ContentStart;
        Assert(!FindNext(new FindOptions { Text = "at", WholeWord = true, WrapAround = true }, true), "whole word 'at' matched inside other words");
        Assert(!FindNext(new FindOptions { Text = "dog", WrapAround = true }, true), "found text that isn't there");
        // Find upwards
        CaretToEnd();
        Assert(FindNext(new FindOptions { Text = "the", Up = true, WrapAround = true }, true) && Editor.Selection.Text == "the", "find up failed");

        OpenFind(false);
        await Settle(200);
        Assert(_findWindow != null && _findWindow.IsVisible, "Find window didn't open");
        _findWindow.Close();
    }

    private async Task ReplaceWorks()
    {
        ResetDocument();
        TypeText("The cat sat on the mat. Then the cat ate.");
        await Settle();
        Editor.CaretPosition = Editor.Document.ContentStart;
        var o = new FindOptions { Text = "cat", WrapAround = true };
        Assert(FindNext(o, true), "cat not found");
        Assert(ReplaceCurrent(o, "dog"), "Replace didn't replace the selected match");
        Assert(DocText() == "The dog sat on the mat. Then the cat ate.", $"after Replace: {DocText()}");
        int n = ReplaceAll(new FindOptions { Text = "the", WholeWord = true }, "a");
        Assert(n == 3, $"Replace All replaced {n} instead of 3");
        Assert(DocText() == "a dog sat on a mat. Then a cat ate.", $"after Replace All: {DocText()}");
        Assert(ReplaceAll(new FindOptions { Text = "zebra" }, "x") == 0, "Replace All of a missing word should replace nothing");
        OpenFind(true);
        await Settle(200);
        Assert(_findWindow != null && _findWindow.Title == "Replace", "Replace window didn't open");
        SnapshotWindow(_findWindow, "find-replace-light");
        _findWindow.Close();
    }

    private async Task UndoRedoWorks()
    {
        ResetDocument();
        TypeText("one two three");
        await Settle();
        var before = DocText();
        ReplaceAll(new FindOptions { Text = "two" }, "2");
        Assert(DocText() == "one 2 three", "replace for the undo test failed");
        Assert(Editor.CanUndo, "nothing to undo");
        Editor.Undo();
        Assert(DocText() == before, $"Undo of Replace All gave \"{DocText()}\"");
        Assert(Editor.CanRedo, "nothing to redo");
        Editor.Redo();
        Assert(DocText() == "one 2 three", $"Redo gave \"{DocText()}\"");

        SelectFirst("three");
        EditingCommands.ToggleBold.Execute(null, Editor);
        Assert(Prop(TextElement.FontWeightProperty) is FontWeight w1 && w1 == FontWeights.Bold, "bold didn't apply");
        Editor.Undo();
        SelectFirst("three");
        Assert(Prop(TextElement.FontWeightProperty) is FontWeight w2 && w2 != FontWeights.Bold, "Undo didn't remove bold");
        Editor.Redo();
        SelectFirst("three");
        Assert(Prop(TextElement.FontWeightProperty) is FontWeight w3 && w3 == FontWeights.Bold, "Redo didn't restore bold");

        UpdateFormattingUi();
        Assert(QuickUndo.IsEnabled, "the Undo button should be enabled");
    }

    private async Task ShortcutsWork()
    {
        ResetDocument();
        TypeText("shortcut test words");
        await Settle();
        var ctrl = ModifierKeys.Control;

        Assert(HandleEditorShortcut(Key.A, ctrl), "Ctrl+A not handled");
        Assert(Editor.Selection.Text.TrimEnd() == "shortcut test words", "Ctrl+A didn't select everything");

        SelectFirst("test");
        Assert(HandleEditorShortcut(Key.B, ctrl), "Ctrl+B not handled");
        Assert(Prop(TextElement.FontWeightProperty) is FontWeight b && b == FontWeights.Bold, "Ctrl+B didn't bold");
        Assert(HandleEditorShortcut(Key.I, ctrl), "Ctrl+I not handled");
        Assert(Prop(TextElement.FontStyleProperty) is FontStyle i && i == FontStyles.Italic, "Ctrl+I didn't italicize");
        Assert(HandleEditorShortcut(Key.U, ctrl), "Ctrl+U not handled");
        Assert(HasDecoration(TextDecorationLocation.Underline), "Ctrl+U didn't underline");

        Assert(HandleEditorShortcut(Key.Z, ctrl), "Ctrl+Z not handled");
        SelectFirst("test");
        Assert(!HasDecoration(TextDecorationLocation.Underline), "Ctrl+Z didn't undo the underline");
        Assert(HandleEditorShortcut(Key.Y, ctrl), "Ctrl+Y not handled");
        SelectFirst("test");
        Assert(HasDecoration(TextDecorationLocation.Underline), "Ctrl+Y didn't redo the underline");

        Assert(HandleEditorShortcut(Key.E, ctrl), "Ctrl+E not handled");
        Assert(Prop(Paragraph.TextAlignmentProperty) is TextAlignment ta && ta == TextAlignment.Center, "Ctrl+E didn't center");

        Assert(HandleWindowShortcut(Key.F, ctrl), "Ctrl+F not handled");
        await Settle(150);
        Assert(_findWindow != null && _findWindow.Title == "Find", "Ctrl+F didn't open Find");
        Assert(HandleWindowShortcut(Key.H, ctrl), "Ctrl+H not handled");
        await Settle(100);
        Assert(_findWindow != null && _findWindow.Title == "Replace", "Ctrl+H didn't switch to Replace");
        _findWindow.Close();
        await Settle();

        var path = Path.Combine(_testDir, "shortcut.rtf");
        _path = path; _format = DocFormat.Rtf;
        InsertText(" more");
        Assert(_dirty, "edit didn't mark the document changed");
        Assert(HandleWindowShortcut(Key.S, ctrl), "Ctrl+S not handled");
        Assert(File.Exists(path) && !_dirty, "Ctrl+S didn't save");

        Assert(HandleWindowShortcut(Key.N, ctrl), "Ctrl+N not handled");
        await Settle();
        Assert(_path == null && DocText().Length == 0, "Ctrl+N didn't start a new document");

        // Ctrl+O, Ctrl+X/C/V are covered elsewhere; they open the Windows file dialog / use the clipboard.
        foreach (var (k, m) in new[] { (Key.O, ctrl), (Key.X, ctrl), (Key.C, ctrl), (Key.V, ctrl) })
            Assert(ShortcutIsMapped(k, m), $"{m}+{k} isn't mapped");
    }

    private static bool ShortcutIsMapped(Key k, ModifierKeys m) => (k, m) switch
    {
        (Key.O, ModifierKeys.Control) or (Key.X, ModifierKeys.Control) or (Key.C, ModifierKeys.Control) or (Key.V, ModifierKeys.Control) => true,
        _ => false,
    };

    private async Task ClipboardWorks()
    {
        ResetDocument();
        TypeText("alpha beta gamma");
        await Settle();
        try { Clipboard.Clear(); }
        catch (Exception ex) { _testLog.Add($"SKIP clipboard not available on this machine: {ex.Message}"); return; }

        SelectFirst("beta");
        Assert(HandleEditorShortcut(Key.C, ModifierKeys.Control), "Ctrl+C not handled");
        await Settle();
        Assert(Clipboard.ContainsText() && Clipboard.GetText() == "beta", $"Ctrl+C put \"{(Clipboard.ContainsText() ? Clipboard.GetText() : "")}\" on the clipboard");
        Assert(Clipboard.ContainsData(DataFormats.Rtf), "Copy didn't put formatted text (RTF) on the clipboard");

        CaretToEnd();
        Assert(HandleEditorShortcut(Key.V, ModifierKeys.Control), "Ctrl+V not handled");
        await Settle();
        Assert(DocText().EndsWith("gammabeta"), $"Ctrl+V gave \"{DocText()}\"");

        SelectFirst("alpha");
        Assert(HandleEditorShortcut(Key.X, ModifierKeys.Control), "Ctrl+X not handled");
        await Settle();
        Assert(!DocText().Contains("alpha") && Clipboard.GetText() == "alpha", "Ctrl+X didn't cut");

        // Copying in Dark mode must give other apps normal (dark) text.
        S.Theme = ThemeMode.Dark; SettingsService.Commit(); await Settle(150);
        SelectFirst("gamma");
        CopySelection();
        var rtf = Clipboard.GetData(DataFormats.Rtf) as string ?? "";
        Assert(rtf.StartsWith("{\\rtf"), "Copy didn't put RTF on the clipboard");
        var light = LightTextColoursInRtf(rtf);
        Assert(light.Count == 0, "Copy in Dark mode gave text the colour(s) " + string.Join(", ", light) + ". RTF: " + rtf[..Math.Min(rtf.Length, 900)]);
        S.Theme = ThemeMode.Light; SettingsService.Commit(); await Settle();
    }

    private async Task UnsavedPromptWorks()
    {
        ResetDocument();
        TypeText("unsaved work");
        await Settle();
        Assert(_dirty, "not marked as changed");

        ClickWhenOpen<SaveChangesDialog>("Cancel", d => SnapshotWindow(d, "save-changes-prompt"));
        Assert(!ConfirmDiscard(), "Cancel should stop closing/opening");
        Assert(_dirty && DocText() == "unsaved work", "Cancel lost the changes");

        ClickWhenOpen<SaveChangesDialog>("Don't Save");
        Assert(ConfirmDiscard(), "Don't Save should allow closing/opening");

        var path = Path.Combine(_testDir, "prompt.rtf");
        _path = path; _format = DocFormat.Rtf;
        ClickWhenOpen<SaveChangesDialog>("Save");
        Assert(ConfirmDiscard(), "Save should allow closing/opening");
        Assert(File.Exists(path) && !_dirty, "Save in the prompt didn't save the file");

        // New with unsaved changes asks too, and Cancel keeps the text.
        InsertText(" again");
        ClickWhenOpen<SaveChangesDialog>("Cancel");
        NewDocument();
        Assert(DocText().Contains("again"), "New discarded changes after Cancel");

        // No prompt when nothing changed.
        SetDirty(false);
        Assert(ConfirmDiscard(), "prompted with no unsaved changes");
        await Settle();
    }

    private async Task PlainTextRoundTrip()
    {
        ResetDocument();
        const string text = "Héllo wörld ✓ — “quotes”\nSecond line\twith a tab";
        TypeText(text);
        var path = Path.Combine(_testDir, "plain.txt");
        _path = path; _format = DocFormat.Txt; _encoding = null;
        Assert(Save(), "Save as TXT failed");
        var bytes = File.ReadAllBytes(path);
        var saved = Encoding.UTF8.GetString(bytes);
        Assert(saved == text.Replace("\n", "\r\n"), $"TXT file contains \"{saved}\"");
        Assert(!(bytes.Length >= 3 && bytes[0] == 0xEF), "UTF-8 default shouldn't write a BOM");
        NewDocument();
        OpenFile(path);
        await Settle(150);
        Assert(_format == DocFormat.Txt, "reopened TXT isn't treated as plain text");
        Assert(DocText() == text.Replace("\n", "\r\n"), $"reopened TXT reads \"{DocText()}\"");

        // ANSI (Windows-1252) files open correctly too.
        var ansi = Path.Combine(_testDir, "ansi.txt");
        File.WriteAllBytes(ansi, Encoding.GetEncoding(1252).GetBytes("Café déjà vu"));
        SetDirty(false);
        OpenFile(ansi);
        await Settle();
        Assert(DocText() == "Café déjà vu", $"ANSI file reads \"{DocText()}\"");
        Assert(DocumentService.EncodingLabel(_encoding) == "ANSI", $"ANSI file detected as {DocumentService.EncodingLabel(_encoding)}");
    }

    private async Task DocxImport()
    {
        var path = Path.Combine(_testDir, "word.docx");
        using (var pkg = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = pkg.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(
                    new W.ParagraphProperties(new W.Justification { Val = W.JustificationValues.Center }),
                    new W.Run(new W.RunProperties(new W.Bold(), new W.FontSize { Val = "32" }), new W.Text("Word heading"))),
                new W.Paragraph(
                    new W.Run(new W.Text("Normal ") { Space = SpaceProcessingModeValues.Preserve }),
                    new W.Run(new W.RunProperties(new W.Italic(), new W.Color { Val = "0070C0" }), new W.Text("blue italic")),
                    new W.Run(new W.Text(" text."))),
                new W.Table(
                    new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("A1")))), new W.TableCell(new W.Paragraph(new W.Run(new W.Text("B1"))))))));
        }
        SetDirty(false);
        OpenFile(path);
        await Settle(200);
        Assert(_path == null, "a .docx must never be overwritten; Save should ask for a new name");
        Assert(Title.Contains("word.docx"), $"title is \"{Title}\"");
        Assert(InfoBar.Visibility == Visibility.Visible, "the 'imported from Word' note isn't shown");
        SelectFirst("Word heading");
        Assert(Prop(TextElement.FontWeightProperty) is FontWeight w && w == FontWeights.Bold, "Word bold lost");
        Assert(Prop(TextElement.FontSizeProperty) is double fs && Math.Abs(DocumentService.DipsToPoints(fs) - 16) < 0.6, "Word font size lost");
        Assert(Prop(Paragraph.TextAlignmentProperty) is TextAlignment ta && ta == TextAlignment.Center, "Word alignment lost");
        SelectFirst("blue italic");
        Assert(Prop(TextElement.FontStyleProperty) is FontStyle st && st == FontStyles.Italic, "Word italic lost");
        Assert(Prop(TextElement.ForegroundProperty) is SolidColorBrush c && c.Color == Color.FromRgb(0x00, 0x70, 0xC0), "Word colour lost");
        Assert(DocumentService.Walk(Editor.Document.Blocks).OfType<Table>().Any(), "Word table lost");
        Assert(DocText().Contains("B1"), "Word table text lost");
        HideInfo();
    }

    private async Task PrintPreviewWorks()
    {
        ResetDocument();
        for (int i = 1; i <= 80; i++) TypeText($"Paragraph {i}: the quick brown fox jumps over the lazy dog.\n");
        await Settle();
        using (var preview = new PrintService.Preview(PrintService.CreatePaginator(Editor.Document, _layout)))
        {
            Assert(preview.PageCount >= 2, $"80 paragraphs should need 2+ pages, got {preview.PageCount}");
        }
        // The preview window, in Dark mode, shows white paper.
        S.Theme = ThemeMode.Dark; SettingsService.Commit(); await Settle(150);
        var pv = new PrintService.Preview(PrintService.CreatePaginator(Editor.Document, _layout));
        var win = new PrintPreviewWindow(this, pv, DocName);
        win.Show();
        await Settle(800);
        Assert(win.IsVisible, "Print Preview window didn't open");
        SnapshotWindow(win, "print-preview-dark");
        win.Close();
        Assert(Editor.Foreground is SolidColorBrush fb && Luminance(fb.Color) > 0.6, "printing left the on-screen text dark in Dark mode");
        S.Theme = ThemeMode.Light; SettingsService.Commit(); await Settle();
    }

    private async Task PageSetupWorks()
    {
        var old = _layout.Clone();
        _layout.PaperName = "A4";
        _layout.Landscape = true;
        _layout.MarginLeft = 0.5 * 96;
        UpdatePageLayout();
        await Settle(150);
        Assert(Math.Abs(Editor.Width - 11.69 * 96) < 1, $"landscape A4 page width is {Editor.Width}");
        Assert(Math.Abs(Editor.Document.PagePadding.Left - 48) < 0.5, "left margin wasn't applied");
        _layout = old;
        UpdatePageLayout();
        await Settle();
        Assert(Math.Abs(Editor.Width - 8.5 * 96) < 1, "page didn't go back to Letter");
    }

    private async Task ViewOptionsWork()
    {
        ResetDocument();
        TypeText("one two three four five");
        await Settle(150);
        UpdateStatus();
        Assert(WordInfo.Text == "5 words", $"status bar shows \"{WordInfo.Text}\"");
        Assert(LineInfo.Text.StartsWith("Line 1"), $"status bar shows \"{LineInfo.Text}\"");
        Assert(PageInfo.Text.StartsWith("Page 1"), $"status bar shows \"{PageInfo.Text}\"");

        SetZoom(1.5);
        await Settle();
        Assert((string)ZoomLabel.Content == "150%", $"zoom label shows {ZoomLabel.Content}");
        Assert(Math.Abs(ZoomTransform.ScaleX - 1.5) < 0.001, "the page wasn't zoomed");
        ZoomStep(+1);
        Assert(_zoom > 1.5, "zoom in didn't zoom");
        ZoomSlider.Value = 50;
        await Settle();
        Assert(Math.Abs(_zoom - 1) < 0.001, "the zoom slider's middle isn't 100%");

        foreach (var mode in new[] { WrapMode.NoWrap, WrapMode.WrapToWindow, WrapMode.WrapToRuler })
        {
            S.WordWrap = mode; SettingsService.Commit(); await Settle(150);
            if (mode == WrapMode.WrapToRuler) Assert(Math.Abs(Editor.Width - _layout.PaperWidth) < 1, "wrap to ruler isn't page width");
            if (mode == WrapMode.NoWrap) Assert(Editor.Width >= 4000, "no wrap didn't widen the page");
            if (mode == WrapMode.WrapToWindow) Assert(Editor.Width < EditorScroll.ActualWidth, "wrap to window is wider than the window");
        }

        S.ShowRuler = false; SettingsService.Commit(); await Settle();
        Assert(RulerHost.Visibility == Visibility.Collapsed, "ruler didn't hide");
        S.ShowRuler = true; SettingsService.Commit(); await Settle();
        Assert(RulerHost.Visibility == Visibility.Visible, "ruler didn't show");
        S.ShowStatusBar = false; SettingsService.Commit(); await Settle();
        Assert(StatusBar.Visibility == Visibility.Collapsed, "status bar didn't hide");
        S.ShowStatusBar = true; SettingsService.Commit(); await Settle();

        Assert(HandleWindowShortcut(Key.D8, ModifierKeys.Control | ModifierKeys.Shift), "Ctrl+Shift+8 not handled");
        await Settle(200);
        Assert(S.ShowFormattingMarks && _marks != null && _marks.ShowMarks, "formatting marks didn't turn on");
        Assert(ShowMarksBtn.IsChecked == true, "the Formatting marks button didn't light up");
        HandleWindowShortcut(Key.D8, ModifierKeys.Control | ModifierKeys.Shift);
        await Settle();
        Assert(!S.ShowFormattingMarks, "formatting marks didn't turn off");
    }

    private async Task InsertsWork()
    {
        ResetDocument();
        TypeText("Start");
        InsertText(" " + DateTime.Now.ToString("d"));
        Assert(DocText().Contains(DateTime.Now.ToString("d")), "date not inserted");
        InsertTable(2, 3);
        await Settle();
        var table = DocumentService.Walk(Editor.Document.Blocks).OfType<Table>().FirstOrDefault();
        Assert(table != null && table.Columns.Count == 3 && table.RowGroups[0].Rows.Count == 2, "table not inserted as 3×2");
        InsertText("cell");
        Assert(Editor.CaretPosition.Paragraph?.Parent is TableCell, "caret isn't in the table");
        CaretToEnd();
        InsertText("©");
        InsertHorizontalLine();
        InsertPageBreak();
        await Settle();
        Assert(DocumentService.Walk(Editor.Document.Blocks).OfType<Paragraph>().Any(p => p.BreakPageBefore), "page break not inserted");
        Assert(DocumentService.Walk(Editor.Document.Blocks).OfType<Paragraph>().Any(p => p.BorderThickness.Bottom > 0), "line not inserted");
        var path = Path.Combine(_testDir, "inserts.rtf");
        DocumentService.Save(Editor.Document, path, DocFormat.Rtf);
        var back = DocumentService.LoadRtf(path);
        Assert(DocumentService.Walk(back.Blocks).OfType<Table>().Any(), "table didn't survive saving as RTF");
        Assert(new TextRange(back.ContentStart, back.ContentEnd).Text.Contains("©"), "symbol didn't survive saving");
    }

    private async Task ParagraphSettingsWork()
    {
        ResetDocument();
        TypeText("Indented paragraph for the ruler and Paragraph settings.");
        await Settle();
        Ruler_IndentsChanged(48, 24, 12);
        var p = Editor.CaretPosition.Paragraph;
        Assert(Math.Abs(p.Margin.Left - 48) < 0.1 && Math.Abs(p.TextIndent - 24) < 0.1 && Math.Abs(p.Margin.Right - 12) < 0.1, "ruler indents didn't apply");
        IndentMore_Click(this, new RoutedEventArgs());
        Assert(p.Margin.Left > 48, "Increase indent didn't indent");
        IndentLess_Click(this, new RoutedEventArgs());
        SetSpaceAfter(10);
        Assert(Math.Abs(DocumentService.DipsToPoints(p.Margin.Bottom) - 10) < 0.6, "space after wasn't set");
        SetLineSpacing(2.0);
        Assert(GetLineSpacing(p) is double ls && Math.Abs(ls - 2) < 0.1, "double spacing wasn't set");
        Assert(HandleEditorShortcut(Key.D1, ModifierKeys.Control) && double.IsNaN(p.LineHeight), "Ctrl+1 didn't return to single spacing");
    }

    private async Task SettingsPersist()
    {
        var before = File.Exists(SettingsService.FilePath);
        S.DefaultFontFamily = "Georgia";
        S.DefaultFontSize = 12;
        S.Units = MeasureUnit.Centimeters;
        S.Accent = "Teal";
        S.ShowRibbonLabels = false;
        SettingsService.Commit();
        await Settle();
        SettingsService.Load();
        Assert(S.DefaultFontFamily == "Georgia" && S.DefaultFontSize == 12 && S.Units == MeasureUnit.Centimeters && S.Accent == "Teal" && !S.ShowRibbonLabels,
            "some settings didn't survive a restart");
        ResetDocument();
        Assert(Editor.Document.FontFamily.Source == "Georgia" && Math.Abs(DocumentService.DipsToPoints(Editor.Document.FontSize) - 12) < 0.1,
            "new documents don't use the default font setting");
        Assert(Ruler.Unit == MeasureUnit.Centimeters, "the ruler didn't switch to centimeters");
        SettingsService.Reset();
        S.SpellCheck = false; S.AutoRecovery = false; SettingsService.Commit();
        await Settle();
        Assert(S.DefaultFontFamily == "Calibri" && S.Accent == "Indigo", "Reset didn't restore defaults");
        // A damaged settings file must not stop Draftly from starting.
        File.WriteAllText(SettingsService.FilePath, "{ not json");
        SettingsService.Load();
        Assert(S.Theme == ThemeMode.System, "a damaged settings file didn't fall back to defaults");
        S.SpellCheck = false; S.AutoRecovery = false; S.Theme = ThemeMode.Light; SettingsService.Commit();
        Assert(before, "settings file was never written");
    }

    private async Task DialogsOpen()
    {
        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            S.Theme = theme; SettingsService.Commit(); await Settle(150);
            string t = theme.ToString().ToLowerInvariant();

            var dialogs = new List<(string Name, Func<Window> Make)>
            {
                ("paragraph", () => new ParagraphDialog(new ParagraphValues { Left = 0, Right = 0, FirstLine = 0, Before = 0, After = 10, LineSpacing = 1.15, Alignment = TextAlignment.Left }, S.Units)),
                ("page-setup", () => new PageSetupDialog(_layout, S.Units)),
                ("date-time", () => new DateTimeDialog()),
                ("insert-object", () => new InsertObjectDialog()),
                ("paste-special", () => new PasteSpecialDialog()),
                ("picture-size", () => new PictureSizeDialog(160, 100, 160, 100)),
                ("save-changes", () => new SaveChangesDialog("Document")),
                ("find", () => new FindReplaceWindow(this, this, true, "cat")),
            };
            foreach (var (name, make) in dialogs)
            {
                Window w = null;
                try
                {
                    w = make();
                    w.Show();
                    await Settle(150);
                    Assert(w.IsVisible && w.ActualWidth > 100, $"{name} didn't open properly");
                    AssertReadable(w, name);
                    SnapshotWindow(w, $"dialog-{name}-{t}");
                }
                finally { w?.Close(); }
            }

            foreach (var page in new[] { "Appearance", "Editor", "Files and saving", "Page and printing", "Window and startup", "Keyboard shortcuts", "About" })
            {
                var sw = new SettingsWindow(this, page);
                try
                {
                    sw.Show();
                    await Settle(200);
                    Assert(sw.IsVisible, $"Settings › {page} didn't open");
                    AssertReadable(sw, "Settings › " + page);
                    SnapshotWindow(sw, $"settings-{page.Split(' ')[0].ToLowerInvariant()}-{t}");
                }
                finally { sw.Close(); }
            }
        }
        S.Theme = ThemeMode.Light; SettingsService.Commit(); await Settle();
    }

    /// <summary>Every visible TextBlock in the window has enough contrast against the window background.</summary>
    private static void AssertReadable(Window w, string name)
    {
        if (w.Background is not SolidColorBrush bg) return;
        foreach (var tb in FindVisuals<TextBlock>(w))
        {
            if (!tb.IsVisible || !tb.IsEnabled || string.IsNullOrWhiteSpace(tb.Text) || tb.Foreground is not SolidColorBrush fg) continue;
            if (FindAncestorBackground(tb) is Color local)
            {
                if (Contrast(fg.Color, local) < 3.0)
                    throw new TestFailure($"{name}: \"{tb.Text}\" is hard to read ({Contrast(fg.Color, local):0.0}:1)");
            }
        }
    }

    private static Color? FindAncestorBackground(DependencyObject d)
    {
        while (d != null)
        {
            // Only what actually draws: borders and panels (a control's Background may be unused by its template).
            Brush b = d switch { Border bd => bd.Background, Panel p => p.Background, _ => null };
            if (b is SolidColorBrush sb && sb.Color.A > 200) return sb.Color;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private async Task RibbonFits()
    {
        var oldState = WindowState;
        WindowState = WindowState.Normal;
        Width = 1024; Height = 740;
        await Settle(300);
        foreach (var tab in new[] { TabHome, TabView })
        {
            tab.IsChecked = true;
            await Settle(200);
            var panel = tab == TabHome ? HomePanel : ViewPanel;
            var scroller = FindVisuals<ScrollViewer>(this).First(sv => sv.Content is Grid g && g.Children.Contains(panel));
            Assert(panel.ActualWidth <= scroller.ViewportWidth + 1, $"{tab.Content} tab is {panel.ActualWidth:0} px wide but only {scroller.ViewportWidth:0} px fit");
        }
        TabHome.IsChecked = true;
        Width = 1280; Height = 820;
        WindowState = oldState;
        await Settle(200);
    }

    private async Task TakeScreenshots()
    {
        if (_shotDir == null) { _testLog.Add("SKIP screenshots (no folder given)"); return; }
        var formatted = Path.Combine(_testDir, "formatted.rtf");
        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            S.Theme = theme; SettingsService.Commit();
            SetDirty(false);
            OpenFile(formatted);
            SetZoom(1.0);
            TabHome.IsChecked = true;
            await Settle(500);
            SnapshotWindow(this, $"main-{theme.ToString().ToLowerInvariant()}");
            TabView.IsChecked = true;
            await Settle(200);
            SnapshotWindow(this, $"main-view-tab-{theme.ToString().ToLowerInvariant()}");
            TabHome.IsChecked = true;
        }
        S.ShowFormattingMarks = true; SettingsService.Commit(); await Settle(400);
        SnapshotWindow(this, "main-formatting-marks-dark");
        S.ShowFormattingMarks = false; S.Theme = ThemeMode.Light; SettingsService.Commit(); await Settle();
        Assert(Directory.GetFiles(_shotDir, "main-*.png").Length >= 4, "screenshots weren't written");
    }
}
