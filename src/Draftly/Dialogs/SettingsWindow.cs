using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Draftly.Services;

namespace Draftly.Dialogs;

/// <summary>
/// Settings. Every change applies immediately and is saved automatically — there's no OK/Apply.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ListBox _nav = new() { Width = 200, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly ContentControl _page = new();
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, Func<UIElement>> _pages = new();
    private bool _loading;

    private static AppSettings S => SettingsService.Current;

    public SettingsWindow(Window owner, string startPage = null)
    {
        Owner = owner;
        Title = "Settings - Draftly";
        Style = (Style)Application.Current.FindResource("DraftlyWindow");
        Width = 860;
        Height = 640;
        MinWidth = 700;
        MinHeight = 480;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeManager.Attach(this);

        _pages["Appearance"] = Appearance;
        _pages["Editor"] = Editor;
        _pages["Files and saving"] = Files;
        _pages["Page and printing"] = Page;
        _pages["Window and startup"] = Startup;
        _pages["Keyboard shortcuts"] = Shortcuts;
        _pages["About"] = About;

        var glyphs = new Dictionary<string, string>
        {
            ["Appearance"] = "", ["Editor"] = "", ["Files and saving"] = "",
            ["Page and printing"] = "", ["Window and startup"] = "",
            ["Keyboard shortcuts"] = "", ["About"] = "",
        };
        foreach (var name in _pages.Keys)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Style = (Style)FindResource("Icon"), Text = glyphs[name], Width = 22, Margin = new Thickness(2, 0, 10, 0) });
            sp.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            _nav.Items.Add(new ListBoxItem { Content = sp, Tag = name, Padding = new Thickness(10, 9, 10, 9) });
        }

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel { Margin = new Thickness(12, 16, 8, 12) };
        var reset = new Button { Content = "Reset all settings", Margin = new Thickness(4, 8, 4, 0) };
        reset.Click += (_, _) =>
        {
            if (MessageDialog.Ask(this, "Reset settings", "Put every setting back to its default? Your recent files list is kept."))
            {
                SettingsService.Reset();
                ShowPage((string)((ListBoxItem)_nav.SelectedItem).Tag);
            }
        };
        DockPanel.SetDock(reset, Dock.Bottom);
        left.Children.Add(reset);
        left.Children.Add(_nav);
        root.Children.Add(left);

        var card = new Border { Margin = new Thickness(0, 12, 12, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "RibbonBg");
        card.SetResourceReference(Border.BorderBrushProperty, "RibbonBorder");
        _scroll = new ScrollViewer { Content = _page, Padding = new Thickness(28, 8, 28, 24), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        card.Child = _scroll;
        Grid.SetColumn(card, 1);
        root.Children.Add(card);
        Content = root;

        _nav.SelectionChanged += (_, _) => { if (_nav.SelectedItem is ListBoxItem li) ShowPage((string)li.Tag); };
        int index = startPage == null ? 0 : Math.Max(0, _pages.Keys.ToList().IndexOf(startPage));
        _nav.SelectedIndex = index;
    }

    private void ShowPage(string name)
    {
        _loading = true;
        var content = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(new TextBlock { Text = name, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
        content.Children.Add(_pages[name]());
        _page.Content = content;
        _scroll.ScrollToTop();
        _loading = false;
    }

    private void Changed()
    {
        if (!_loading) SettingsService.Commit();
    }

    // ================================================================== builders

    private static TextBlock H(string t) => new() { Text = t, Style = (Style)Application.Current.FindResource("SectionHeader"), Margin = new Thickness(0, 18, 0, 8) };
    private static TextBlock Hint(string t) => new() { Text = t, Style = (Style)Application.Current.FindResource("Hint"), Margin = new Thickness(0, 2, 0, 6) };

    private Border Card(params UIElement[] children)
    {
        var sp = new StackPanel();
        foreach (var c in children) sp.Children.Add(c);
        return new Border { Style = (Style)FindResource("Card"), Child = sp };
    }

    /// <summary>One setting row: title + description on the left, control on the right.</summary>
    private static Grid Setting(string title, string description, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(description))
            text.Children.Add(new TextBlock { Text = description, Style = (Style)Application.Current.FindResource("Hint"), Margin = new Thickness(0, 2, 0, 0) });
        g.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private CheckBox Toggle(bool value, Action<bool> set)
    {
        var cb = new CheckBox { IsChecked = value, Margin = new Thickness(0) };
        cb.Click += (_, _) => { set(cb.IsChecked == true); Changed(); };
        return cb;
    }

    private ComboBox Choice(string[] items, int selected, Action<int> set, double width = 200)
    {
        var c = new ComboBox { Width = width };
        foreach (var i in items) c.Items.Add(i);
        c.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) { set(c.SelectedIndex); Changed(); } };
        return c;
    }

    private static Rectangle Line()
    {
        var r = new Rectangle { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
        r.SetResourceReference(Shape.FillProperty, "Divider");
        return r;
    }

    // ================================================================== pages

    private UIElement Appearance()
    {
        var sp = new StackPanel();

        sp.Children.Add(H("Interface"));
        var styles = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (style, label, glyph) in new[] { (InterfaceStyle.Ribbon, "Ribbon", "\uE8A9"), (InterfaceStyle.Classic, "Classic menus and toolbars", "\uE700") })
        {
            var t = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)FindResource("RibbonSmallToggle"), Tag = glyph, Content = label,
                IsChecked = S.Interface == style, Height = 44, Padding = new Thickness(14, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0),
            };
            t.SetResourceReference(BorderBrushProperty, "ControlBorder");
            t.Click += (_, _) => { S.Interface = style; Changed(); ShowPage("Appearance"); };
            styles.Children.Add(t);
        }
        sp.Children.Add(styles);
        sp.Children.Add(Hint("Ribbon has Home and View tabs like WordPad in Windows 10 and 11. Classic has a menu bar (File, Edit, View, Insert, Format, Help) with a standard toolbar and a formatting toolbar, like older WordPad and Word. Both have every command."));

        sp.Children.Add(H("Theme"));
        var themes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (mode, label, glyph) in new[] { (ThemeMode.Light, "Light", ""), (ThemeMode.Dark, "Dark", ""), (ThemeMode.System, "Follow system", "") })
        {
            var t = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)FindResource("RibbonSmallToggle"), Tag = glyph, Content = label,
                IsChecked = S.Theme == mode, Height = 44, Padding = new Thickness(14, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0),
            };
            t.SetResourceReference(BorderBrushProperty, "ControlBorder");
            t.Click += (_, _) => { S.Theme = mode; Changed(); ShowPage("Appearance"); };
            themes.Children.Add(t);
        }
        sp.Children.Add(themes);
        sp.Children.Add(Hint("Follow system matches Windows' app mode (Settings › Personalization › Colors) and switches automatically when you change it."));

        sp.Children.Add(H("Accent colour"));
        var accents = new WrapPanel();
        foreach (var a in ThemeManager.Accents)
        {
            var color = ThemeManager.IsDark ? a.Dark : a.Light;
            var swatch = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                Width = 40, Height = 40, Margin = new Thickness(3, 3, 11, 11),
                ToolTip = a.Name, Background = new SolidColorBrush(color), BorderThickness = new Thickness(S.Accent == a.Name ? 3 : 1),
            };
            swatch.SetResourceReference(BorderBrushProperty, S.Accent == a.Name ? "Text" : "ControlBorder");
            if (S.Accent == a.Name)
                swatch.Content = new TextBlock { Style = (Style)FindResource("Icon"), Text = "", Foreground = ThemeManager.IsDark ? Brushes.Black : Brushes.White };
            var name = a.Name;
            swatch.Click += (_, _) => { S.Accent = name; Changed(); ShowPage("Appearance"); };
            accents.Children.Add(swatch);
        }
        sp.Children.Add(accents);

        sp.Children.Add(H("Document page"));
        sp.Children.Add(Card(
            Setting("Dark page in dark mode", "Shows the page dark with light text. Turn off to keep a white page. Files are always saved with normal black text.",
                Toggle(S.DarkPageInDarkMode, v => S.DarkPageInDarkMode = v)),
            Line(),
            Setting("Highlight current line", "Draws a soft band behind the line the cursor is on.",
                Toggle(S.HighlightCurrentLine, v => S.HighlightCurrentLine = v))));

        sp.Children.Add(H("Toolbar and text size"));
        sp.Children.Add(Card(
            Setting("Show labels on small ribbon buttons", "Turn off for a more compact ribbon. Narrow windows hide them automatically.",
                Toggle(S.ShowRibbonLabels, v => S.ShowRibbonLabels = v)),
            Line(),
            Setting("Interface text size", "Size of menus, dialogs and labels. Doesn't change your document.",
                Choice(new[] { "Small (90%)", "Normal (100%)", "Large (115%)", "Larger (130%)" },
                    S.UiScale < 0.95 ? 0 : S.UiScale < 1.1 ? 1 : S.UiScale < 1.25 ? 2 : 3,
                    i => S.UiScale = new[] { 0.9, 1.0, 1.15, 1.3 }[i], 180))));
        return sp;
    }

    private UIElement Editor()
    {
        var sp = new StackPanel();
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToArray();
        var fontBox = new ComboBox { Width = 220, MaxDropDownHeight = 360 };
        foreach (var f in fonts) fontBox.Items.Add(new ComboBoxItem { Content = f, FontFamily = new FontFamily(f) });
        fontBox.SelectedIndex = Math.Max(0, Array.IndexOf(fonts, S.DefaultFontFamily));
        fontBox.SelectionChanged += (_, _) => { if (fontBox.SelectedItem is ComboBoxItem i) { S.DefaultFontFamily = (string)i.Content; Changed(); } };

        var sizes = new[] { 8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28 };
        var sizeBox = Choice(sizes.Select(s => s.ToString(CultureInfo.CurrentCulture)).ToArray(),
            Math.Max(0, Array.FindIndex(sizes, s => Math.Abs(s - S.DefaultFontSize) < 0.01)), i => S.DefaultFontSize = sizes[i], 90);

        sp.Children.Add(H("New documents"));
        sp.Children.Add(Card(
            Setting("Default font", "Used for new documents and plain text files.", fontBox),
            Line(),
            Setting("Default size", "In points.", sizeBox)));

        sp.Children.Add(H("Layout"));
        sp.Children.Add(Card(
            Setting("Word wrap", "Wrap to ruler shows the page as it prints. Wrap to window fills the window. No wrap keeps long lines on one line.",
                Choice(new[] { "No wrap", "Wrap to window", "Wrap to ruler" }, (int)S.WordWrap, i => S.WordWrap = (WrapMode)i)),
            Line(),
            Setting("Measurement units", "Used by the ruler, Paragraph and Page setup.",
                Choice(new[] { "Inches", "Centimeters", "Points", "Picas" }, (int)S.Units, i => S.Units = (MeasureUnit)i)),
            Line(),
            Setting("Default zoom", "Zoom level when Draftly opens.",
                Choice(new[] { "75%", "90%", "100%", "110%", "125%", "150%", "175%", "200%" },
                    Math.Max(0, Array.IndexOf(new[] { 75, 90, 100, 110, 125, 150, 175, 200 }, S.DefaultZoom)),
                    i => S.DefaultZoom = new[] { 75, 90, 100, 110, 125, 150, 175, 200 }[i], 120))));

        sp.Children.Add(H("Show"));
        sp.Children.Add(Card(
            Setting("Ruler", null, Toggle(S.ShowRuler, v => S.ShowRuler = v)),
            Line(),
            Setting("Status bar", "Page, line, word count and zoom.", Toggle(S.ShowStatusBar, v => S.ShowStatusBar = v)),
            Line(),
            Setting("Formatting marks", "Paragraph marks (¶), spaces (·) and tabs (→).", Toggle(S.ShowFormattingMarks, v => S.ShowFormattingMarks = v))));

        sp.Children.Add(H("Typing"));
        sp.Children.Add(Card(
            Setting("Check spelling as you type", "Underlines misspelled words. Right-click a word for suggestions. Uses the Windows spell checker for your language.",
                Toggle(S.SpellCheck, v => S.SpellCheck = v)),
            Line(),
            Setting("Select whole words when dragging", "When you drag across several words, the selection snaps to whole words.",
                Toggle(S.SelectWholeWords, v => S.SelectWholeWords = v)),
            Line(),
            Setting("Smooth scrolling", "Animates mouse-wheel scrolling.", Toggle(S.SmoothScrolling, v => S.SmoothScrolling = v))));
        return sp;
    }

    private UIElement Files()
    {
        var sp = new StackPanel();
        sp.Children.Add(H("Saving"));
        sp.Children.Add(Card(
            Setting("Default format for new documents", "Rich Text keeps formatting and pictures. Plain text is text only.",
                Choice(new[] { "Rich Text (.rtf)", "Plain text (.txt)" }, (int)S.SaveFormat, i => S.SaveFormat = (DefaultSaveFormat)i)),
            Line(),
            Setting("Plain text encoding", "Used when saving new .txt files. Existing files keep their own encoding.",
                Choice(new[] { "UTF-8", "UTF-8 with BOM", "ANSI (Windows-1252)", "UTF-16 (Unicode)" }, (int)S.TextEncoding, i => S.TextEncoding = (TxtEncoding)i)),
            Line(),
            Setting("Keep a backup copy", "Before overwriting a file, the previous version is kept as filename.rtf.bak.",
                Toggle(S.KeepBackupOnSave, v => S.KeepBackupOnSave = v))));

        var minutes = new[] { 1, 2, 5, 10, 15 };
        sp.Children.Add(H("Auto-recovery"));
        sp.Children.Add(Card(
            Setting("Save recovery information", "If Draftly closes unexpectedly or the PC loses power, your unsaved work is offered next time.",
                Toggle(S.AutoRecovery, v => S.AutoRecovery = v)),
            Line(),
            Setting("Every", null, Choice(minutes.Select(m => m == 1 ? "1 minute" : $"{m} minutes").ToArray(),
                Math.Max(0, Array.IndexOf(minutes, S.AutoRecoveryMinutes)), i => S.AutoRecoveryMinutes = minutes[i], 140)),
            Hint("Recovery files are kept in " + RecoveryService.Folder)));

        var counts = new[] { 0, 5, 10, 15, 20, 30 };
        var clear = new Button { Content = "Clear list" };
        clear.Click += (_, _) => { S.RecentFiles.Clear(); Changed(); clear.IsEnabled = false; };
        clear.IsEnabled = S.RecentFiles.Count > 0;
        sp.Children.Add(H("Recent documents"));
        sp.Children.Add(Card(
            Setting("Number of recent documents to show", null,
                Choice(counts.Select(c => c == 0 ? "None" : c.ToString()).ToArray(), Math.Max(0, Array.IndexOf(counts, S.RecentFilesMax)),
                    i => { S.RecentFilesMax = counts[i]; while (S.RecentFiles.Count > counts[i]) S.RecentFiles.RemoveAt(S.RecentFiles.Count - 1); }, 100)),
            Line(),
            Setting("Recent documents list", $"{S.RecentFiles.Count} item(s)", clear)));
        return sp;
    }

    private UIElement Page()
    {
        var sp = new StackPanel();
        var papers = PageLayout.PaperSizes.Select(p => p.Name).ToArray();
        sp.Children.Add(H("Defaults for new documents"));
        string Fmt(double inches) => Units.Format(inches * 96, S.Units);
        var margins = new TextBlock
        {
            Text = $"Left {Fmt(S.MarginLeftIn)}, Right {Fmt(S.MarginRightIn)}, Top {Fmt(S.MarginTopIn)}, Bottom {Fmt(S.MarginBottomIn)}",
            TextWrapping = TextWrapping.Wrap,
        };
        var editMargins = new Button { Content = "Change…" };
        editMargins.Click += (_, _) =>
        {
            var d = new PageSetupDialog(PageLayout.FromSettings(S), S.Units) { Owner = this };
            if (d.ShowDialog() == true && d.Result is PageLayout l)
            {
                S.PaperSize = l.PaperName; S.Landscape = l.Landscape;
                S.MarginLeftIn = l.MarginLeft / 96; S.MarginRightIn = l.MarginRight / 96;
                S.MarginTopIn = l.MarginTop / 96; S.MarginBottomIn = l.MarginBottom / 96;
                S.PrintPageNumbers = l.PageNumbers;
                Changed();
                ShowPage("Page and printing");
            }
        };
        sp.Children.Add(Card(
            Setting("Paper size", null, Choice(papers, Math.Max(0, Array.IndexOf(papers, S.PaperSize)), i => S.PaperSize = papers[i], 160)),
            Line(),
            Setting("Orientation", null, Choice(new[] { "Portrait", "Landscape" }, S.Landscape ? 1 : 0, i => S.Landscape = i == 1, 160)),
            Line(),
            Setting("Margins", null, editMargins),
            margins,
            Line(),
            Setting("Print page numbers", "Adds \"Page 1 of 3\" at the bottom of each printed page.", Toggle(S.PrintPageNumbers, v => S.PrintPageNumbers = v))));
        sp.Children.Add(Hint("These apply to new documents. To change the open document, use File › Page setup."));
        return sp;
    }

    private UIElement Startup()
    {
        var sp = new StackPanel();
        sp.Children.Add(H("When Draftly starts"));
        sp.Children.Add(Card(
            Setting("Reopen the last document", "Opens the file you were working on when you last closed Draftly.",
                Toggle(S.ReopenLastDocument, v => S.ReopenLastDocument = v)),
            Line(),
            Setting("Remember window size and position", null, Toggle(S.RememberWindow, v => S.RememberWindow = v))));
        sp.Children.Add(H("Settings file"));
        var open = new Button { Content = "Open folder" };
        open.Click += (_, _) =>
        {
            try { System.IO.Directory.CreateDirectory(SettingsService.Folder); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsService.Folder}\"") { UseShellExecute = true }); }
            catch { }
        };
        sp.Children.Add(Card(Setting("Settings are saved automatically", SettingsService.FilePath, open)));
        return sp;
    }

    private UIElement Shortcuts()
    {
        var sp = new StackPanel();
        var groups = new (string, (string, string)[])[]
        {
            ("Files", new[] { ("New", "Ctrl+N"), ("Open", "Ctrl+O"), ("Save", "Ctrl+S"), ("Save as", "Ctrl+Shift+S / F12"), ("Print", "Ctrl+P"), ("Print preview", "Ctrl+F2") }),
            ("Editing", new[] { ("Undo", "Ctrl+Z"), ("Redo", "Ctrl+Y"), ("Cut / Copy / Paste", "Ctrl+X / Ctrl+C / Ctrl+V"), ("Paste special", "Ctrl+Alt+V"),
                ("Select all", "Ctrl+A"), ("Find", "Ctrl+F"), ("Find next / previous", "F3 / Shift+F3"), ("Replace", "Ctrl+H"), ("Page break", "Ctrl+Enter") }),
            ("Formatting", new[] { ("Bold / Italic / Underline", "Ctrl+B / Ctrl+I / Ctrl+U"), ("Subscript / Superscript", "Ctrl+= / Ctrl+Shift+="),
                ("Grow / shrink font", "Ctrl+Shift+> / Ctrl+Shift+<"), ("Clear formatting", "Ctrl+Space"), ("Bullets", "Ctrl+Shift+L"),
                ("Align left / center / right / justify", "Ctrl+L / E / R / J"), ("Line spacing 1 / 1.5 / 2", "Ctrl+1 / Ctrl+5 / Ctrl+2") }),
            ("View", new[] { ("Zoom in / out", "Ctrl+Numpad+ / Ctrl+Minus or Ctrl+wheel"), ("Reset zoom", "Ctrl+0"), ("Formatting marks", "Ctrl+Shift+8"), ("Settings", "Ctrl+,") }),
        };
        foreach (var (title, rows) in groups)
        {
            sp.Children.Add(H(title));
            var list = new StackPanel();
            foreach (var (action, keys) in rows)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock { Text = action });
                var k = new TextBlock { Text = keys, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13 };
                k.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                Grid.SetColumn(k, 1);
                g.Children.Add(k);
                list.Children.Add(g);
            }
            sp.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = list });
        }
        return sp;
    }

    private UIElement About()
    {
        var sp = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 14) };
        head.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/draftly-256.png")), Width = 72, Height = 72, Margin = new Thickness(0, 0, 18, 0) });
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = "Draftly", FontSize = 26, FontWeight = FontWeights.SemiBold });
        info.Children.Add(new TextBlock { Text = $"Version {v.Major}.{v.Minor}.{v.Build}" });
        info.Children.Add(Hint("A modern rich text word processor for Windows."));
        head.Children.Add(info);
        sp.Children.Add(head);
        var repo = new Button { Content = "Draftly on GitHub", Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Left };
        repo.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("https://github.com/adamglassner/draftly") { UseShellExecute = true }); } catch { } };
        sp.Children.Add(repo);
        sp.Children.Add(Card(
            new TextBlock { Text = "File formats", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) },
            Hint("Opens and saves Rich Text (.rtf) and plain text (.txt). Opens Word (.docx) documents for reading and editing; save them as .rtf or .txt."),
            new TextBlock { Text = "Credits", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) },
            Hint("Word import uses the Open XML SDK by Microsoft (MIT licence). Built with .NET 8 and WPF.")));
        sp.Children.Add(Hint($"© 2026 Bytehawk Studio. Running on {Environment.OSVersion.VersionString}."));
        return sp;
    }
}
