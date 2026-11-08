using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Draftly.Dialogs;

/// <summary>Resize a picture by percentage, keeping or freeing the aspect ratio.</summary>
public sealed class PictureSizeDialog : DialogBase
{
    public double NewWidth { get; private set; }
    public double NewHeight { get; private set; }

    public PictureSizeDialog(double width, double height, double naturalWidth, double naturalHeight) : base("Resize picture", 380)
    {
        var w = Field(Math.Round(width / naturalWidth * 100).ToString(CultureInfo.CurrentCulture), 90);
        var h = Field(Math.Round(height / naturalHeight * 100).ToString(CultureInfo.CurrentCulture), 90);
        var keep = new CheckBox { Content = "Keep proportions", IsChecked = true };
        bool syncing = false;
        w.TextChanged += (_, _) => { if (keep.IsChecked == true && !syncing) { syncing = true; h.Text = w.Text; syncing = false; } };
        h.TextChanged += (_, _) => { if (keep.IsChecked == true && !syncing) { syncing = true; w.Text = h.Text; syncing = false; } };

        Body.Children.Add(Row("Width (%)", w, 120));
        Body.Children.Add(Row("Height (%)", h, 120));
        Body.Children.Add(keep);
        Body.Children.Add(Hint($"Original size: {naturalWidth:0} × {naturalHeight:0} px"));

        AddButton("OK", primary: true, onClick: () =>
        {
            if (!double.TryParse(w.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var pw) || pw < 1 || pw > 1000 ||
                !double.TryParse(h.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var ph) || ph < 1 || ph > 1000)
            {
                MessageDialog.Show(this, "Resize picture", "Enter a percentage between 1 and 1000.", MessageDialog.Kind.Warning);
                return;
            }
            NewWidth = naturalWidth * pw / 100;
            NewHeight = naturalHeight * ph / 100;
            DialogResult = true;
        });
        AddButton("Cancel", cancel: true);
    }
}
