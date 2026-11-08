using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Draftly.Dialogs;

/// <summary>Themed replacement for MessageBox.</summary>
public sealed class MessageDialog : DialogBase
{
    public enum Kind { Info, Warning, Error, Question }

    public bool Yes { get; private set; }

    private MessageDialog(Window owner, string title, string message, Kind kind, bool yesNo) : base(title, 420)
    {
        if (owner != null && owner.IsLoaded) Owner = owner;
        MaxWidth = 560;

        var row = new DockPanel();
        var icon = new TextBlock
        {
            Style = (Style)FindResource("Icon"),
            FontSize = 28,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 16, 0),
            Text = kind switch
            {
                Kind.Error => "",
                Kind.Warning => "",
                Kind.Question => "",
                _ => "",
            },
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, kind == Kind.Error ? "DangerText" : "Accent");
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, Margin = new Thickness(0, 0, 0, 12) });
        Body.Children.Add(row);

        if (yesNo)
        {
            AddButton("Yes", primary: true, onClick: () => { Yes = true; DialogResult = true; });
            AddButton("No", cancel: true, onClick: () => { Yes = false; DialogResult = false; });
        }
        else AddButton("OK", primary: true);
    }

    public static void Show(Window owner, string title, string message, Kind kind = Kind.Info)
    {
        new MessageDialog(owner, title, message, kind, false).ShowDialog();
    }

    public static bool Ask(Window owner, string title, string message)
    {
        var d = new MessageDialog(owner, title, message, Kind.Question, true);
        d.ShowDialog();
        return d.Yes;
    }
}
