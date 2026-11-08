using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Draftly.Dialogs;

public enum ObjectKind { Table, Symbol, HorizontalLine, PageBreak, FileContents }

/// <summary>
/// "Insert object". Draftly can't embed live OLE objects (an editable Excel sheet, for example) — WPF's
/// editor has no OLE container — so this offers the useful things people actually insert instead.
/// </summary>
public sealed class InsertObjectDialog : DialogBase
{
    public ObjectKind Kind { get; private set; }
    public int Rows { get; private set; } = 3;
    public int Columns { get; private set; } = 3;
    public string Symbol { get; private set; }

    private static readonly string[] Symbols =
    {
        "©", "®", "™", "§", "¶", "†", "‡", "•", "…", "–", "—", "‘", "’", "“", "”", "«", "»",
        "°", "±", "×", "÷", "≈", "≠", "≤", "≥", "∞", "√", "∑", "π", "µ", "Ω", "Δ", "α", "β",
        "½", "¼", "¾", "⅓", "⅔", "¹", "²", "³", "€", "£", "¥", "¢", "₹", "₩", "←", "→", "↑", "↓",
        "↔", "⇒", "✓", "✗", "★", "☆", "♥", "♦", "♣", "♠", "☺", "☼", "♪", "✉", "☎", "✂", "✎", "⌘",
    };

    private readonly ListBox _kinds = new() { Width = 200, Height = 300 };
    private readonly ContentControl _detail = new() { Width = 360, Margin = new Thickness(20, 0, 0, 0) };
    private TextBox _rows, _cols;
    private string _pickedSymbol;

    public InsertObjectDialog() : base("Insert object", 600)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_kinds);
        Grid.SetColumn(_detail, 1);
        grid.Children.Add(_detail);
        Body.Children.Add(grid);
        Body.Children.Add(Hint("Draftly can't embed live objects from other programs (such as an editable spreadsheet). To include one, paste it as a picture."));

        foreach (var (label, kind) in new[]
                 {
                     ("Table", ObjectKind.Table), ("Symbol", ObjectKind.Symbol),
                     ("Horizontal line", ObjectKind.HorizontalLine), ("Page break", ObjectKind.PageBreak),
                     ("Text from a file…", ObjectKind.FileContents),
                 })
            _kinds.Items.Add(new ListBoxItem { Content = label, Tag = kind });

        _kinds.SelectionChanged += (_, _) => ShowDetail();
        _kinds.SelectedIndex = 0;
        _kinds.MouseDoubleClick += (_, _) => Ok();

        AddButton("Insert", primary: true, onClick: Ok);
        AddButton("Cancel", cancel: true);
    }

    private ObjectKind Selected => (ObjectKind)((ListBoxItem)_kinds.SelectedItem).Tag;

    private void ShowDetail()
    {
        var p = new StackPanel();
        switch (Selected)
        {
            case ObjectKind.Table:
                p.Children.Add(Header("Table size"));
                p.Children.Add(Row("Columns", _cols = Field(Columns.ToString(), 80), 100));
                p.Children.Add(Row("Rows", _rows = Field(Rows.ToString(), 80), 100));
                p.Children.Add(Hint("Inserts a table with thin borders. Press Tab to move between cells."));
                break;
            case ObjectKind.Symbol:
                p.Children.Add(Header("Choose a symbol"));
                var wrap = new WrapPanel { Width = 360 };
                foreach (var s in Symbols)
                {
                    var b = new Button
                    {
                        Content = s, Style = (Style)FindResource("IconButton"),
                        FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"), FontSize = 17,
                        Width = 38, Height = 38, Focusable = true, ToolTip = $"U+{char.ConvertToUtf32(s, 0):X4}",
                    };
                    b.Click += (_, _) => { _pickedSymbol = s; Ok(); };
                    wrap.Children.Add(b);
                }
                p.Children.Add(wrap);
                break;
            case ObjectKind.HorizontalLine:
                p.Children.Add(Header("Horizontal line"));
                p.Children.Add(Hint("Inserts a thin line across the page. It's saved as a paragraph border, so it also appears in WordPad and Word."));
                break;
            case ObjectKind.PageBreak:
                p.Children.Add(Header("Page break"));
                p.Children.Add(Hint("Text after the cursor starts on a new page when printed. Shortcut: Ctrl+Enter."));
                break;
            case ObjectKind.FileContents:
                p.Children.Add(Header("Text from a file"));
                p.Children.Add(Hint("Inserts the contents of another RTF, TXT or DOCX file at the cursor."));
                break;
        }
        _detail.Content = p;
    }

    private void Ok()
    {
        if (_kinds.SelectedItem == null) return;
        Kind = Selected;
        if (Kind == ObjectKind.Table)
        {
            if (!int.TryParse(_cols.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var c) || c < 1 || c > 20 ||
                !int.TryParse(_rows.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var r) || r < 1 || r > 200)
            {
                MessageDialog.Show(this, "Insert table", "Use 1–20 columns and 1–200 rows.", MessageDialog.Kind.Warning);
                return;
            }
            Columns = c; Rows = r;
        }
        if (Kind == ObjectKind.Symbol)
        {
            if (_pickedSymbol == null) { MessageDialog.Show(this, "Insert symbol", "Click a symbol to insert it."); return; }
            Symbol = _pickedSymbol;
        }
        DialogResult = true;
    }
}
