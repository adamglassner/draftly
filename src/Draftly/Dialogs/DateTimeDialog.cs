using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Draftly.Dialogs;

public sealed class DateTimeDialog : DialogBase
{
    private static int _lastIndex;
    public string Result { get; private set; }

    public DateTimeDialog() : base("Date and time", 420)
    {
        var now = DateTime.Now;
        var c = CultureInfo.CurrentCulture;
        var formats = new[]
        {
            now.ToString("d", c), now.ToString("D", c),
            now.ToString("MMMM d, yyyy", c), now.ToString("dddd, MMMM d, yyyy", c),
            now.ToString("d MMMM yyyy", c), now.ToString("yyyy-MM-dd", c),
            now.ToString("MM/dd/yy", c), now.ToString("MMM d, yyyy", c),
            now.ToString("t", c), now.ToString("T", c), now.ToString("HH:mm", c),
            now.ToString("g", c), now.ToString("G", c), now.ToString("yyyy-MM-dd HH:mm", c),
        }.Distinct().ToList();

        Body.Children.Add(Label("Available formats:"));
        var list = new ListBox { Height = 320, Width = 360 };
        foreach (var f in formats) list.Items.Add(f);
        list.SelectedIndex = Math.Min(_lastIndex, formats.Count - 1);
        list.MouseDoubleClick += (_, _) => Ok(list);
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ok(list); };
        Body.Children.Add(list);
        Loaded += (_, _) => { list.Focus(); (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as ListBoxItem)?.Focus(); };

        AddButton("Insert", primary: true, onClick: () => Ok(list));
        AddButton("Cancel", cancel: true);
    }

    private void Ok(ListBox list)
    {
        if (list.SelectedItem is not string s) return;
        _lastIndex = list.SelectedIndex;
        Result = s;
        DialogResult = true;
    }
}
