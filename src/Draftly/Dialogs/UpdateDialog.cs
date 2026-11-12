using System.Windows;
using System.Windows.Controls;
using Draftly.Services;

namespace Draftly.Dialogs;

/// <summary>"A new version of Draftly is available" with the release notes.</summary>
public sealed class UpdateDialog : DialogBase
{
    public enum Choice { Later, Update, Skip }
    public Choice Result { get; private set; } = Choice.Later;

    public UpdateDialog(UpdateService.UpdateInfo info) : base("Update Draftly", 500)
    {
        var heading = new TextBlock
        {
            Text = $"Draftly {info.Display} is available",
            FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4),
        };
        Body.Children.Add(heading);
        Body.Children.Add(Hint($"You have version {UpdateService.CurrentDisplay}. Draftly closes while it updates and then opens again, with your tabs."));

        var notes = UpdateService.ShortNotes(info.Notes);
        if (notes.Length > 0)
        {
            var label = Label("What's new");
            label.Margin = new Thickness(0, 14, 0, 6);
            Body.Children.Add(label);
            var text = new TextBlock { Text = notes, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
            var box = new Border
            {
                Child = new ScrollViewer { Content = text, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8) },
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            };
            box.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
            box.SetResourceReference(Border.BackgroundProperty, "ControlBg");
            Body.Children.Add(box);
        }

        AddButton("Skip this version", onClick: () => { Result = Choice.Skip; DialogResult = true; });
        AddButton("Update now", primary: true, onClick: () => { Result = Choice.Update; DialogResult = true; });
        AddButton("Later", cancel: true);
    }
}
