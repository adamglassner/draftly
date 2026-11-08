using System.Windows;
using System.Windows.Controls;

namespace Draftly.Dialogs;

public enum PasteKind { Formatted, PlainText, Picture }

public sealed class PasteSpecialDialog : DialogBase
{
    public PasteKind Result { get; private set; }

    public PasteSpecialDialog() : base("Paste special", 420)
    {
        bool rtf = false, text = false, image = false;
        try
        {
            var data = Clipboard.GetDataObject();
            rtf = data?.GetDataPresent(DataFormats.Rtf) == true;
            text = data?.GetDataPresent(DataFormats.UnicodeText) == true || data?.GetDataPresent(DataFormats.Text) == true;
            image = Clipboard.ContainsImage();
        }
        catch { /* clipboard busy: show the empty state */ }

        Body.Children.Add(Label("Paste the clipboard contents as:"));
        var list = new ListBox { Width = 360, Height = 150 };
        void Add(string label, PasteKind k, bool ok)
        {
            if (!ok) return;
            list.Items.Add(new ListBoxItem { Content = label, Tag = k });
        }
        Add("Formatted text (RTF)", PasteKind.Formatted, rtf);
        Add("Unformatted text", PasteKind.PlainText, text);
        Add("Picture", PasteKind.Picture, image);
        Body.Children.Add(list);

        var hint = Hint("");
        Body.Children.Add(hint);
        list.SelectionChanged += (_, _) =>
        {
            hint.Text = (list.SelectedItem as ListBoxItem)?.Tag switch
            {
                PasteKind.Formatted => "Keeps fonts, colours and paragraph formatting.",
                PasteKind.PlainText => "Inserts text only and matches the formatting where you paste it.",
                PasteKind.Picture => "Inserts the clipboard image as a picture.",
                _ => "",
            };
        };

        var ok = AddButton("Paste", primary: true, onClick: () =>
        {
            if (list.SelectedItem is ListBoxItem li) { Result = (PasteKind)li.Tag; DialogResult = true; }
        });
        AddButton("Cancel", cancel: true);

        if (list.Items.Count == 0)
        {
            Body.Children.Add(Hint("The clipboard doesn't contain anything Draftly can paste."));
            ok.IsEnabled = false;
        }
        else list.SelectedIndex = 0;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is ListBoxItem li) { Result = (PasteKind)li.Tag; DialogResult = true; } };
    }
}
