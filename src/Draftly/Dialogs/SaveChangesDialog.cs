using System.Windows;
using System.Windows.Controls;

namespace Draftly.Dialogs;

public enum SaveChoice { Save, DontSave, Cancel }

/// <summary>"Do you want to save changes?" with Save / Don't Save / Cancel.</summary>
public sealed class SaveChangesDialog : DialogBase
{
    public SaveChoice Choice { get; private set; } = SaveChoice.Cancel;

    public SaveChangesDialog(string docName) : base("Draftly", 420)
    {
        Body.Children.Add(new TextBlock
        {
            Text = $"Do you want to save changes to {docName}?",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            Margin = new Thickness(0, 0, 0, 6),
        });
        Body.Children.Add(Hint("If you don't save, your changes will be lost."));

        AddButton("Save", primary: true, onClick: () => { Choice = SaveChoice.Save; DialogResult = true; });
        AddButton("Don't Save", onClick: () => { Choice = SaveChoice.DontSave; DialogResult = true; });
        AddButton("Cancel", cancel: true, onClick: () => { Choice = SaveChoice.Cancel; DialogResult = false; });

        // Access keys for keyboard users: S / N / Esc
        ((Button)ButtonRow.Children[0]).Content = "_Save";
        ((Button)ButtonRow.Children[1]).Content = "Do_n't Save";
    }

    public static SaveChoice Ask(Window owner, string docName)
    {
        var d = new SaveChangesDialog(docName);
        if (owner != null && owner.IsLoaded) d.Owner = owner;
        d.ShowDialog();
        return d.Choice;
    }
}
