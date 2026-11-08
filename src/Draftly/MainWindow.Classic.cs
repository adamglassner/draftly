using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Draftly.Services;

namespace Draftly;

/// <summary>
/// The Classic interface: a menu bar (File, Edit, View, Insert, Format, Help) with a standard toolbar and
/// a formatting toolbar, instead of the ribbon. Switch in View, or Settings › Appearance › Interface.
/// Every command calls the same code as the ribbon.
/// </summary>
public partial class MainWindow
{
    private FrameworkElement _classicChrome;
    private Menu _classicMenu;
    private ComboBox _classicFont, _classicSize;
    private Button _classicUndo, _classicRedo, _classicTextDrop, _classicHighlightDrop;
    private Rectangle _classicTextSwatch, _classicHighlightSwatch;
    private readonly List<(ToggleButton Classic, ToggleButton Ribbon)> _classicToggles = new();
    private readonly List<(MenuItem Item, Func<bool> IsChecked)> _menuChecks = new();
    private readonly List<(MenuItem Item, Func<bool> IsEnabled)> _menuEnables = new();

    private bool IsClassic => S.Interface == InterfaceStyle.Classic;

    private void UseClassic_Click(object sender, RoutedEventArgs e)
    {
        S.Interface = InterfaceStyle.Classic;
        SettingsService.Commit();
        Editor.Focus();
    }

    /// <summary>Shows the ribbon or the classic menus/toolbars, per the setting.</summary>
    private void ApplyInterfaceStyle()
    {
        if (IsClassic && _classicChrome == null) BuildClassicUi();
        var ribbon = IsClassic ? Visibility.Collapsed : Visibility.Visible;
        TabRow.Visibility = ribbon;
        RibbonBorder.Visibility = ribbon;
        if (_classicChrome != null) _classicChrome.Visibility = IsClassic ? Visibility.Visible : Visibility.Collapsed;
        if (IsClassic) UpdateClassicUi();
    }

    /// <summary>Mirrors the ribbon's state (bold, alignment, font...) onto the classic toolbar.</summary>
    private void UpdateClassicUi()
    {
        if (_classicChrome == null) return;
        bool old = _updatingUi;
        _updatingUi = true;
        try
        {
            foreach (var (c, r) in _classicToggles) c.IsChecked = r.IsChecked;
            if (!_classicFont.IsKeyboardFocusWithin) { _classicFont.SelectedItem = null; _classicFont.Text = FontFamilyBox.Text; }
            if (!_classicSize.IsKeyboardFocusWithin) { _classicSize.SelectedItem = null; _classicSize.Text = FontSizeBox.Text; }
            _classicUndo.IsEnabled = Editor.CanUndo;
            _classicRedo.IsEnabled = Editor.CanRedo;
        }
        finally { _updatingUi = old; }
    }

    // ================================================================== building

    private void BuildClassicUi()
    {
        var panel = new StackPanel();

        _classicMenu = new Menu { Style = (Style)FindResource("MenuBar") };
        BuildMenus(_classicMenu);
        panel.Children.Add(_classicMenu);

        var bars = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)FindResource("RadiusLarge"),
            Margin = new Thickness(8, 0, 8, 6),
            Padding = new Thickness(4, 2, 4, 2),
        };
        bars.SetResourceReference(Border.BackgroundProperty, "RibbonBg");
        bars.SetResourceReference(Border.BorderBrushProperty, "RibbonBorder");
        var rows = new StackPanel();
        rows.Children.Add(BuildStandardToolbar());
        var line = new Rectangle { Height = 1, Margin = new Thickness(4, 1, 4, 1) };
        line.SetResourceReference(Shape.FillProperty, "Divider");
        rows.Children.Add(line);
        rows.Children.Add(BuildFormattingToolbar());
        bars.Child = rows;
        panel.Children.Add(bars);

        Grid.SetRow(panel, 1);
        RootGrid.Children.Add(panel);
        _classicChrome = panel;
    }

    private Button ToolButton(string glyph, string tip, Action click)
    {
        var b = new Button { Style = (Style)FindResource("IconButton"), Content = glyph, ToolTip = tip };
        AutomationName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private Button ToolButton(UIElement content, string tip, Action click)
    {
        var b = new Button { Style = (Style)FindResource("IconButton"), Content = content, ToolTip = tip };
        AutomationName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A toggle that runs the ribbon toggle's handler and mirrors its state.</summary>
    private ToggleButton ToolToggle(UIElement content, string tip, ToggleButton ribbonTwin, RoutedEventHandler handler, string tag = null)
    {
        var t = new ToggleButton { Style = (Style)FindResource("IconToggle"), Content = content, ToolTip = tip, Tag = tag };
        AutomationName(t, tip);
        t.Click += handler;
        _classicToggles.Add((t, ribbonTwin));
        return t;
    }

    private static void AutomationName(FrameworkElement e, string tip) =>
        System.Windows.Automation.AutomationProperties.SetName(e, tip.Split(" (")[0]);

    private UIElement Divider()
    {
        var r = new Rectangle { Width = 1, Height = 22, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        r.SetResourceReference(Shape.FillProperty, "Divider");
        return r;
    }

    private TextBlock Letter(string text, FontWeight? weight = null, FontStyle? style = null, TextDecorationCollection deco = null, string font = null, double size = 15)
    {
        var tb = new TextBlock { Text = text, FontSize = size, TextDecorations = deco };
        if (font != null) tb.FontFamily = new FontFamily(font); else tb.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        if (weight.HasValue) tb.FontWeight = weight.Value;
        if (style.HasValue) tb.FontStyle = style.Value;
        return tb;
    }

    private Path LineIcon(string geometryKey) =>
        new() { Style = (Style)FindResource("LineIcon"), Data = (Geometry)FindResource(geometryKey) };

    private static RoutedEventArgs NoArgs => new();

    private UIElement BuildStandardToolbar()
    {
        var row = new WrapPanel();
        row.Children.Add(ToolButton("", "New (Ctrl+N)", NewDocument));
        row.Children.Add(ToolButton("", "Open (Ctrl+O)", OpenDocument));
        row.Children.Add(ToolButton("", "Save (Ctrl+S)", () => Save()));
        row.Children.Add(Divider());
        row.Children.Add(ToolButton("", "Print (Ctrl+P)", Print));
        row.Children.Add(ToolButton("", "Print preview (Ctrl+F2)", PrintPreview));
        row.Children.Add(Divider());
        row.Children.Add(ToolButton("", "Find (Ctrl+F)", () => OpenFind(false)));
        row.Children.Add(ToolButton("", "Replace (Ctrl+H)", () => OpenFind(true)));
        row.Children.Add(Divider());
        row.Children.Add(ToolButton("", "Cut (Ctrl+X)", CutSelection));
        row.Children.Add(ToolButton("", "Copy (Ctrl+C)", CopySelection));
        row.Children.Add(ToolButton("", "Paste (Ctrl+V)", PasteSmart));
        row.Children.Add(Divider());
        _classicUndo = ToolButton("", "Undo (Ctrl+Z)", () => { if (Editor.CanUndo) Editor.Undo(); UpdateFormattingUi(); Editor.Focus(); });
        _classicRedo = ToolButton("", "Redo (Ctrl+Y)", () => { if (Editor.CanRedo) Editor.Redo(); UpdateFormattingUi(); Editor.Focus(); });
        row.Children.Add(_classicUndo);
        row.Children.Add(_classicRedo);
        row.Children.Add(Divider());
        row.Children.Add(ToolButton("", "Insert picture", () => InsertPicture_Click(this, NoArgs)));
        row.Children.Add(ToolButton("", "Insert date and time", () => InsertDateTime_Click(this, NoArgs)));
        row.Children.Add(ToolButton("", "Insert object: table, symbol, line, page break or a file", () => InsertObject_Click(this, NoArgs)));
        row.Children.Add(Divider());
        row.Children.Add(ToolButton(Letter("¶", size: 17), "Show formatting marks (Ctrl+Shift+8)", () => { S.ShowFormattingMarks = !S.ShowFormattingMarks; SettingsService.Commit(); }));
        row.Children.Add(ToolButton("", "Settings (Ctrl+,)", () => OpenSettings()));
        return row;
    }

    private UIElement BuildFormattingToolbar()
    {
        var row = new WrapPanel();

        _classicFont = new ComboBox
        {
            Width = 170, IsEditable = true, IsTextSearchEnabled = true, MaxDropDownHeight = 420, ToolTip = "Font",
            ItemsSource = FontFamilyBox.ItemsSource, ItemTemplate = FontFamilyBox.ItemTemplate, Margin = new Thickness(2, 1, 2, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationName(_classicFont, "Font");
        _classicFont.SelectionChanged += FontFamilyBox_SelectionChanged;
        _classicFont.KeyDown += FontFamilyBox_KeyDown;
        _classicFont.LostKeyboardFocus += FontFamilyBox_LostFocus;
        row.Children.Add(_classicFont);

        _classicSize = new ComboBox { Width = 64, IsEditable = true, ToolTip = "Font size", Margin = new Thickness(2, 1, 2, 1), VerticalAlignment = VerticalAlignment.Center };
        AutomationName(_classicSize, "Font size");
        foreach (var size in StandardSizes) _classicSize.Items.Add(size.ToString(CultureInfo.CurrentCulture));
        _classicSize.SelectionChanged += FontSizeBox_SelectionChanged;
        _classicSize.KeyDown += FontSizeBox_KeyDown;
        _classicSize.LostKeyboardFocus += FontSizeBox_LostFocus;
        row.Children.Add(_classicSize);
        row.Children.Add(ToolButton("", "Grow font (Ctrl+Shift+>)", () => StepFont(+1)));
        row.Children.Add(ToolButton("", "Shrink font (Ctrl+Shift+<)", () => StepFont(-1)));
        row.Children.Add(Divider());

        row.Children.Add(ToolToggle(Letter("B", FontWeights.Bold, font: "Georgia", size: 16), "Bold (Ctrl+B)", BoldBtn, Bold_Click));
        row.Children.Add(ToolToggle(Letter("I", style: FontStyles.Italic, font: "Georgia", size: 16), "Italic (Ctrl+I)", ItalicBtn, Italic_Click));
        row.Children.Add(ToolToggle(Letter("U", deco: TextDecorations.Underline, font: "Georgia", size: 16), "Underline (Ctrl+U)", UnderlineBtn, Underline_Click));
        row.Children.Add(ToolToggle(Letter("abc", deco: TextDecorations.Strikethrough, size: 13), "Strikethrough", StrikeBtn, Strike_Click));
        var sub = Letter("x", size: 14); sub.Inlines.Add(new Run("2") { BaselineAlignment = BaselineAlignment.Subscript, FontSize = 9 });
        row.Children.Add(ToolToggle(sub, "Subscript (Ctrl+=)", SubBtn, Subscript_Click));
        var sup = Letter("x", size: 14); sup.Inlines.Add(new Run("2") { BaselineAlignment = BaselineAlignment.Superscript, FontSize = 9 });
        row.Children.Add(ToolToggle(sup, "Superscript (Ctrl+Shift+=)", SuperBtn, Superscript_Click));
        row.Children.Add(Divider());

        // Text colour and highlight: the button applies the last colour, the arrow picks another.
        _classicTextSwatch = new Rectangle { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, RadiusX = 1, RadiusY = 1, Fill = TextColorSwatch.Fill };
        var textIcon = new Grid { Width = 18, Height = 20 };
        textIcon.Children.Add(new TextBlock { Text = "A", FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -1, 0, 0), FontFamily = new FontFamily("Segoe UI") });
        textIcon.Children.Add(_classicTextSwatch);
        row.Children.Add(ToolButton(textIcon, "Text colour", () => TextColor_Click(this, NoArgs)));
        _classicTextDrop = new Button { Style = (Style)FindResource("ChevronButton"), ToolTip = "Choose text colour" };
        AutomationName(_classicTextDrop, "Choose text colour");
        _classicTextDrop.Click += TextColorDrop_Click;
        row.Children.Add(_classicTextDrop);

        _classicHighlightSwatch = new Rectangle { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, RadiusX = 1, RadiusY = 1, Fill = HighlightSwatch.Fill };
        var hlIcon = new Grid { Width = 18, Height = 20 };
        hlIcon.Children.Add(new TextBlock { Style = (Style)FindResource("Icon"), Text = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Top });
        hlIcon.Children.Add(_classicHighlightSwatch);
        row.Children.Add(ToolButton(hlIcon, "Highlight", () => Highlight_Click(this, NoArgs)));
        _classicHighlightDrop = new Button { Style = (Style)FindResource("ChevronButton"), ToolTip = "Choose highlight colour" };
        AutomationName(_classicHighlightDrop, "Choose highlight colour");
        _classicHighlightDrop.Click += HighlightDrop_Click;
        row.Children.Add(_classicHighlightDrop);
        var clear = new Grid();
        clear.Children.Add(new TextBlock { Text = "A", FontSize = 15, Margin = new Thickness(0, 0, 5, 2), FontFamily = new FontFamily("Segoe UI") });
        var x = new TextBlock { Style = (Style)FindResource("Icon"), Text = "", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        x.SetResourceReference(TextBlock.ForegroundProperty, "DangerText");
        clear.Children.Add(x);
        row.Children.Add(ToolButton(clear, "Clear formatting (Ctrl+Space)", ClearFormatting));
        row.Children.Add(Divider());

        row.Children.Add(ToolToggle(LineIcon("GeoAlignLeft"), "Align left (Ctrl+L)", AlignLeftBtn, Align_Click, "Left"));
        row.Children.Add(ToolToggle(LineIcon("GeoAlignCenter"), "Center (Ctrl+E)", AlignCenterBtn, Align_Click, "Center"));
        row.Children.Add(ToolToggle(LineIcon("GeoAlignRight"), "Align right (Ctrl+R)", AlignRightBtn, Align_Click, "Right"));
        row.Children.Add(ToolToggle(LineIcon("GeoJustify"), "Justify (Ctrl+J)", AlignJustifyBtn, Align_Click, "Justify"));
        row.Children.Add(Divider());

        var bullets = new TextBlock { Style = (Style)FindResource("Icon"), Text = "" };
        row.Children.Add(ToolToggle(bullets, "Bullets (Ctrl+Shift+L)", BulletsBtn, Bullets_Click));
        var numbering = new Grid { Width = 16, Height = 17 };
        numbering.Children.Add(LineIcon("GeoNumberLines"));
        var digits = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var d in new[] { "1", "2", "3" })
            digits.Children.Add(new TextBlock { Text = d, FontSize = 6.5, LineHeight = 5.3, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontFamily = new FontFamily("Segoe UI") });
        numbering.Children.Add(digits);
        row.Children.Add(ToolToggle(numbering, "Numbering", NumberingBtn, Numbering_Click));
        row.Children.Add(ToolButton(LineIcon("GeoIndentLess"), "Decrease indent", () => ChangeIndent(-1)));
        row.Children.Add(ToolButton(LineIcon("GeoIndentMore"), "Increase indent", () => ChangeIndent(+1)));
        var spacing = new Button { Style = (Style)FindResource("IconButton"), Content = LineIcon("GeoLineSpacing"), ToolTip = "Line and paragraph spacing" };
        AutomationName(spacing, "Line spacing");
        spacing.Click += LineSpacing_Click;
        row.Children.Add(spacing);
        row.Children.Add(ToolButton(Letter("¶", FontWeights.SemiBold, size: 15), "Paragraph settings", ShowParagraphDialog));
        return row;
    }

    // ================================================================== menus

    private MenuItem Top(string header) => new() { Header = header, Style = (Style)FindResource("TopMenuItem") };

    private MenuItem Item(string header, Action action, string gesture = "", string glyph = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture };
        if (glyph != null) mi.Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 15 };
        mi.Click += (_, e) => { e.Handled = true; action(); };
        return mi;
    }

    private MenuItem Checked(string header, Func<bool> isChecked, Action action, string gesture = "")
    {
        var mi = Item(header, action, gesture);
        _menuChecks.Add((mi, isChecked));
        return mi;
    }

    private MenuItem Enabled(MenuItem mi, Func<bool> isEnabled)
    {
        _menuEnables.Add((mi, isEnabled));
        return mi;
    }

    private static MenuItem Sub(string header, params object[] items)
    {
        var mi = new MenuItem { Header = header };
        foreach (var i in items) mi.Items.Add(i);
        return mi;
    }

    private void RefreshMenuStates()
    {
        foreach (var (mi, f) in _menuChecks) { try { mi.IsChecked = f(); } catch { mi.IsChecked = false; } }
        foreach (var (mi, f) in _menuEnables) { try { mi.IsEnabled = f(); } catch { mi.IsEnabled = true; } }
    }

    private void SetSetting(Action change)
    {
        change();
        SettingsService.Commit();
        Editor.Focus();
    }

    private void BuildMenus(Menu menu)
    {
        bool HasSelection() => !Editor.Selection.IsEmpty;
        TextAlignment? Align() => Editor.Selection.GetPropertyValue(Paragraph.TextAlignmentProperty) is TextAlignment a ? a : null;
        TextMarkerStyle? ListStyle() => CurrentList()?.MarkerStyle;
        double? Spacing() => Editor.Selection.Start.Paragraph is Paragraph p ? GetLineSpacing(p) : null;

        // ---- File
        var file = Top("_File");
        file.Items.Add(Item("_New", NewDocument, "Ctrl+N", ""));
        file.Items.Add(Item("_Open…", OpenDocument, "Ctrl+O", ""));
        file.Items.Add(Item("_Save", () => Save(), "Ctrl+S", ""));
        file.Items.Add(Sub("Save _as",
            Item("_Rich Text document (.rtf)…", () => SaveAs(DocFormat.Rtf), "Ctrl+Shift+S"),
            Item("_Plain text document (.txt)…", () => SaveAs(DocFormat.Txt)),
            Item("_Other format…", () => SaveAs(null), "F12")));
        file.Items.Add(new Separator());
        file.Items.Add(Item("_Print…", Print, "Ctrl+P", ""));
        file.Items.Add(Item("Print pre_view", PrintPreview, "Ctrl+F2", ""));
        file.Items.Add(Item("Page set_up…", () => PageSetup()));
        file.Items.Add(new Separator());
        var recent = new MenuItem { Header = "_Recent documents" };
        recent.Items.Add(new MenuItem());   // placeholder so the arrow shows; filled when opened
        recent.SubmenuOpened += (_, e) =>
        {
            if (e.OriginalSource != recent) return;
            recent.Items.Clear();
            var files = S.RecentFiles.Take(S.RecentFilesMax).ToList();
            if (files.Count == 0) recent.Items.Add(new MenuItem { Header = "No recent documents", IsEnabled = false });
            int n = 1;
            foreach (var f in files)
            {
                var path = f;
                var mi = Item($"_{n++}  {System.IO.Path.GetFileName(f)}", () => { if (ConfirmDiscard()) OpenFile(path); });
                mi.ToolTip = f;
                recent.Items.Add(mi);
            }
            if (files.Count > 0)
            {
                recent.Items.Add(new Separator());
                recent.Items.Add(Item("Clear list", () => { S.RecentFiles.Clear(); SettingsService.Save(); }));
            }
        };
        file.Items.Add(recent);
        file.Items.Add(new Separator());
        file.Items.Add(Item("Se_ttings…", () => OpenSettings(), "Ctrl+,", ""));
        file.Items.Add(new Separator());
        file.Items.Add(Item("E_xit", Close, "Alt+F4"));
        menu.Items.Add(file);

        // ---- Edit
        var edit = Top("_Edit");
        edit.Items.Add(Enabled(Item("_Undo", () => { Editor.Undo(); UpdateFormattingUi(); }, "Ctrl+Z", ""), () => Editor.CanUndo));
        edit.Items.Add(Enabled(Item("_Redo", () => { Editor.Redo(); UpdateFormattingUi(); }, "Ctrl+Y", ""), () => Editor.CanRedo));
        edit.Items.Add(new Separator());
        edit.Items.Add(Enabled(Item("Cu_t", CutSelection, "Ctrl+X", ""), HasSelection));
        edit.Items.Add(Enabled(Item("_Copy", CopySelection, "Ctrl+C", ""), HasSelection));
        edit.Items.Add(Item("_Paste", PasteSmart, "Ctrl+V", ""));
        edit.Items.Add(Item("Paste _special…", PasteSpecial, "Ctrl+Alt+V"));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("Select _all", () => { Editor.SelectAll(); Editor.Focus(); }, "Ctrl+A", ""));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("_Find…", () => OpenFind(false), "Ctrl+F", ""));
        edit.Items.Add(Item("Find _next", () => HandleWindowShortcut(Key.F3, ModifierKeys.None), "F3"));
        edit.Items.Add(Item("R_eplace…", () => OpenFind(true), "Ctrl+H", ""));
        menu.Items.Add(edit);

        // ---- View
        var view = Top("_View");
        view.Items.Add(Checked("_Ruler", () => S.ShowRuler, () => SetSetting(() => S.ShowRuler = !S.ShowRuler)));
        view.Items.Add(Checked("_Status bar", () => S.ShowStatusBar, () => SetSetting(() => S.ShowStatusBar = !S.ShowStatusBar)));
        view.Items.Add(Checked("Formatting _marks", () => S.ShowFormattingMarks, () => SetSetting(() => S.ShowFormattingMarks = !S.ShowFormattingMarks), "Ctrl+Shift+8"));
        view.Items.Add(new Separator());
        view.Items.Add(Sub("_Word wrap",
            Checked("_No wrap", () => S.WordWrap == WrapMode.NoWrap, () => SetSetting(() => S.WordWrap = WrapMode.NoWrap)),
            Checked("Wrap to _window", () => S.WordWrap == WrapMode.WrapToWindow, () => SetSetting(() => S.WordWrap = WrapMode.WrapToWindow)),
            Checked("Wrap to _ruler", () => S.WordWrap == WrapMode.WrapToRuler, () => SetSetting(() => S.WordWrap = WrapMode.WrapToRuler))));
        view.Items.Add(Sub("_Units",
            Checked("_Inches", () => S.Units == MeasureUnit.Inches, () => SetSetting(() => S.Units = MeasureUnit.Inches)),
            Checked("_Centimeters", () => S.Units == MeasureUnit.Centimeters, () => SetSetting(() => S.Units = MeasureUnit.Centimeters)),
            Checked("_Points", () => S.Units == MeasureUnit.Points, () => SetSetting(() => S.Units = MeasureUnit.Points)),
            Checked("Pi_cas", () => S.Units == MeasureUnit.Picas, () => SetSetting(() => S.Units = MeasureUnit.Picas))));
        view.Items.Add(Sub("_Zoom",
            Item("Zoom _in", () => ZoomStep(+1), "Ctrl+Plus"),
            Item("Zoom _out", () => ZoomStep(-1), "Ctrl+Minus"),
            Item("_100%", () => SetZoom(1.0), "Ctrl+0"),
            Item("_Page width", () => ZoomPageWidth_Click(this, NoArgs))));
        view.Items.Add(new Separator());
        view.Items.Add(Sub("_Theme",
            Checked("_Light", () => S.Theme == ThemeMode.Light, () => SetSetting(() => S.Theme = ThemeMode.Light)),
            Checked("_Dark", () => S.Theme == ThemeMode.Dark, () => SetSetting(() => S.Theme = ThemeMode.Dark)),
            Checked("Follow _system", () => S.Theme == ThemeMode.System, () => SetSetting(() => S.Theme = ThemeMode.System))));
        view.Items.Add(new Separator());
        view.Items.Add(Item("Switch to the _ribbon", () => SetSetting(() => S.Interface = InterfaceStyle.Ribbon)));
        menu.Items.Add(view);

        // ---- Insert
        var insert = Top("_Insert");
        insert.Items.Add(Item("_Picture…", () => InsertPicture_Click(this, NoArgs), "", ""));
        insert.Items.Add(Item("_Date and time…", () => InsertDateTime_Click(this, NoArgs), "", ""));
        insert.Items.Add(Item("_Object…", () => InsertObject_Click(this, NoArgs), "", ""));
        insert.Items.Add(new Separator());
        insert.Items.Add(Item("_Horizontal line", () => { InsertHorizontalLine(); Editor.Focus(); }));
        insert.Items.Add(Item("Page _break", () => { InsertPageBreak(); Editor.Focus(); }, "Ctrl+Enter"));
        menu.Items.Add(insert);

        // ---- Format
        var format = Top("F_ormat");
        format.Items.Add(Checked("_Bold", () => BoldBtn.IsChecked == true, () => Bold_Click(this, NoArgs), "Ctrl+B"));
        format.Items.Add(Checked("_Italic", () => ItalicBtn.IsChecked == true, () => Italic_Click(this, NoArgs), "Ctrl+I"));
        format.Items.Add(Checked("_Underline", () => UnderlineBtn.IsChecked == true, () => Underline_Click(this, NoArgs), "Ctrl+U"));
        format.Items.Add(Checked("Stri_kethrough", () => StrikeBtn.IsChecked == true, () => Strike_Click(this, NoArgs)));
        format.Items.Add(Checked("Su_bscript", () => SubBtn.IsChecked == true, () => Subscript_Click(this, NoArgs), "Ctrl+="));
        format.Items.Add(Checked("Su_perscript", () => SuperBtn.IsChecked == true, () => Superscript_Click(this, NoArgs), "Ctrl+Shift+="));
        format.Items.Add(new Separator());
        format.Items.Add(Item("_Grow font", () => StepFont(+1), "Ctrl+Shift+>"));
        format.Items.Add(Item("Shrin_k font", () => StepFont(-1), "Ctrl+Shift+<"));
        format.Items.Add(Item("Text _colour…", () => TextColorDrop_Click(_classicTextDrop, NoArgs)));
        format.Items.Add(Item("_Highlight colour…", () => HighlightDrop_Click(_classicHighlightDrop, NoArgs)));
        format.Items.Add(Item("Clea_r formatting", ClearFormatting, "Ctrl+Space"));
        format.Items.Add(new Separator());
        format.Items.Add(Sub("_Align",
            Checked("_Left", () => Align() == TextAlignment.Left, () => SetAlignment(TextAlignment.Left), "Ctrl+L"),
            Checked("_Center", () => Align() == TextAlignment.Center, () => SetAlignment(TextAlignment.Center), "Ctrl+E"),
            Checked("_Right", () => Align() == TextAlignment.Right, () => SetAlignment(TextAlignment.Right), "Ctrl+R"),
            Checked("_Justify", () => Align() == TextAlignment.Justify, () => SetAlignment(TextAlignment.Justify), "Ctrl+J")));
        var bulletItems = new List<object>();
        foreach (var (label, style) in new[] { ("● Solid circle", TextMarkerStyle.Disc), ("○ Hollow circle", TextMarkerStyle.Circle), ("■ Solid square", TextMarkerStyle.Square), ("□ Hollow square", TextMarkerStyle.Box) })
            bulletItems.Add(Checked(label, () => ListStyle() == style, () => SetListStyle(style)));
        bulletItems.Add(new Separator());
        bulletItems.Add(Enabled(Item("_None", () => { if (ListStyle() is TextMarkerStyle s) ToggleList(s); }), () => ListStyle() != null));
        format.Items.Add(Sub("B_ullets", bulletItems.ToArray()));
        var numberItems = new List<object>();
        foreach (var (label, style) in new[] { ("1, 2, 3", TextMarkerStyle.Decimal), ("a, b, c", TextMarkerStyle.LowerLatin), ("A, B, C", TextMarkerStyle.UpperLatin), ("i, ii, iii", TextMarkerStyle.LowerRoman), ("I, II, III", TextMarkerStyle.UpperRoman) })
            numberItems.Add(Checked(label, () => ListStyle() == style, () => SetListStyle(style)));
        format.Items.Add(Sub("_Numbering", numberItems.ToArray()));
        var spacingItems = new List<object>();
        foreach (var f in Dialogs.ParagraphDialog.Spacings)
        {
            var factor = f;
            spacingItems.Add(Checked(f.ToString("0.0#", CultureInfo.CurrentCulture), () => Spacing() is double d && Math.Abs(d - factor) < 0.05, () => SetLineSpacing(factor)));
        }
        spacingItems.Add(new Separator());
        spacingItems.Add(Checked("Add 10pt space after paragraphs",
            () => Editor.Selection.Start.Paragraph is Paragraph p && Safe(p.Margin.Bottom) > 1,
            () => SetSpaceAfter(Editor.Selection.Start.Paragraph is Paragraph p && Safe(p.Margin.Bottom) > 1 ? 0 : 10)));
        format.Items.Add(Sub("_Line spacing", spacingItems.ToArray()));
        format.Items.Add(Item("_Increase indent", () => ChangeIndent(+1)));
        format.Items.Add(Item("_Decrease indent", () => ChangeIndent(-1)));
        format.Items.Add(new Separator());
        format.Items.Add(Item("_Paragraph…", ShowParagraphDialog));
        menu.Items.Add(format);

        // ---- Help
        var help = Top("_Help");
        help.Items.Add(Item("_Keyboard shortcuts", () => OpenSettings("Keyboard shortcuts"), "", ""));
        help.Items.Add(Item("Draftly on _GitHub", () =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/adamglassner/draftly") { UseShellExecute = true }); } catch { }
        }));
        help.Items.Add(new Separator());
        help.Items.Add(Item("_About Draftly", () => OpenSettings("About"), "", ""));
        menu.Items.Add(help);

        foreach (MenuItem top in menu.Items)
            top.SubmenuOpened += (_, _) => { UpdateFormattingUi(); RefreshMenuStates(); };
        menu.LostKeyboardFocus += (_, e) => { if (e.NewFocus == null) Editor.Focus(); };
    }
}
