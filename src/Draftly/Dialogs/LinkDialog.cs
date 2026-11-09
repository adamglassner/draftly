using System.Windows;
using System.Windows.Controls;

namespace Draftly.Dialogs;

/// <summary>Insert or edit a link (Ctrl+K).</summary>
public sealed class LinkDialog : DialogBase
{
    public string LinkText { get; private set; }
    public string Address { get; private set; }
    public bool RemoveRequested { get; private set; }

    public LinkDialog(string text, string address, bool editing) : base(editing ? "Edit link" : "Insert link", 440)
    {
        var textBox = Field(text ?? "", 380);
        var addressBox = Field(address ?? "https://", 380);
        Body.Children.Add(Label("Text to display"));
        Body.Children.Add(textBox);
        var addressLabel = Label("Address");
        addressLabel.Margin = new Thickness(0, 12, 0, 6);
        Body.Children.Add(addressLabel);
        Body.Children.Add(addressBox);
        Body.Children.Add(Hint("A web address (https://…) or an email address. Ctrl+click a link to open it."));

        if (editing)
            AddButton("Remove link", onClick: () => { RemoveRequested = true; DialogResult = true; });
        AddButton("OK", primary: true, onClick: () =>
        {
            var a = addressBox.Text.Trim();
            if (a.Length > 0 && !a.Contains(':') && a.Contains('@') && !a.Contains(' ')) a = "mailto:" + a;
            else if (a.Length > 0 && !a.Contains(':')) a = "https://" + a;
            if (!Uri.TryCreate(a, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto" or "file"))
            {
                MessageDialog.Show(this, Title, "Enter a web address such as https://example.com, or an email address.", MessageDialog.Kind.Warning);
                return;
            }
            Address = a;
            LinkText = textBox.Text.Length > 0 ? textBox.Text : a.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? a[7..] : a;
            DialogResult = true;
        });
        AddButton("Cancel", cancel: true);
        Loaded += (_, _) =>
        {
            var target = string.IsNullOrEmpty(text) ? textBox : addressBox;
            target.Focus();
            target.SelectAll();
        };
    }
}
