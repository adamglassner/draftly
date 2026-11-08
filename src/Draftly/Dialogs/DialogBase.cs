using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Draftly.Services;

namespace Draftly.Dialogs;

/// <summary>Themed dialog shell: content area on top, right-aligned button row at the bottom.</summary>
public class DialogBase : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(24, 20, 24, 8) };
    protected readonly StackPanel ButtonRow = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly Border _footer;

    public DialogBase(string title, double minWidth = 380)
    {
        Title = title;
        Style = (Style)Application.Current.FindResource("DialogWindow");
        MinWidth = minWidth;
        ThemeManager.Attach(this);
        Owner = Application.Current.MainWindow is { IsLoaded: true } mw && mw != this ? mw : null;
        WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        var root = new DockPanel();
        _footer = new Border
        {
            Padding = new Thickness(24, 14, 24, 16),
            Child = ButtonRow,
        };
        _footer.SetResourceReference(Border.BackgroundProperty, "WindowBg");
        _footer.SetResourceReference(Border.BorderBrushProperty, "Divider");
        _footer.BorderThickness = new Thickness(0, 1, 0, 0);
        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(_footer);
        root.Children.Add(Body);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            // DialogResult can only be set on a window opened with ShowDialog.
            try { DialogResult = false; } catch (InvalidOperationException) { Close(); }
        };
    }

    protected Button AddButton(string text, bool primary = false, bool cancel = false, Action onClick = null)
    {
        var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), IsDefault = primary, IsCancel = cancel };
        if (primary) b.Style = (Style)FindResource("AccentButton");
        b.Click += (_, _) =>
        {
            if (onClick != null) onClick();
            else DialogResult = primary;
        };
        ButtonRow.Children.Add(b);
        return b;
    }

    protected void HideFooter() => _footer.Visibility = Visibility.Collapsed;

    // ---------- small builders used by all dialogs ----------

    protected static TextBlock Label(string text, double bottom = 6) =>
        new() { Text = text, Margin = new Thickness(0, 0, 0, bottom), TextWrapping = TextWrapping.Wrap };

    protected static TextBlock Hint(string text) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource("Hint"), Margin = new Thickness(0, 4, 0, 8) };

    protected static TextBlock Header(string text) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource("SectionHeader") };

    /// <summary>A label + control on one row.</summary>
    protected static Grid Row(string label, UIElement control, double labelWidth = 150, double bottom = 10)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, bottom) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        g.Children.Add(tb);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    protected static ComboBox Combo(IEnumerable<object> items, int selected = 0, double width = 200)
    {
        var c = new ComboBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var i in items) c.Items.Add(i);
        if (c.Items.Count > 0) c.SelectedIndex = Math.Clamp(selected, 0, c.Items.Count - 1);
        return c;
    }

    protected static TextBox Field(string text, double width = 120) =>
        new() { Text = text, Width = width, HorizontalAlignment = HorizontalAlignment.Left };
}
