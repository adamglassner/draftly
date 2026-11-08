using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Draftly.Services;

namespace Draftly.Dialogs;

public sealed class FindOptions
{
    public string Text = "";
    public bool MatchCase, WholeWord, WrapAround = true, Up;
}

/// <summary>Modeless Find / Replace window. The editor does the actual searching.</summary>
public sealed class FindReplaceWindow : DialogBase
{
    public interface IHost
    {
        bool FindNext(FindOptions o, bool quiet = false);
        bool ReplaceCurrent(FindOptions o, string replacement);
        int ReplaceAll(FindOptions o, string replacement);
        void ShowStatus(string text);
    }

    private static string _lastFind = "", _lastReplace = "";
    private static bool _matchCase, _wholeWord, _wrap = true;

    private readonly IHost _host;
    private readonly TextBox _find, _replace;
    private readonly CheckBox _case, _word, _wrapBox;
    private readonly RadioButton _up, _down;
    private readonly StackPanel _replaceRow;
    private readonly Button _replaceBtn, _replaceAllBtn;
    private readonly TextBlock _status;
    private bool _replaceMode;

    public FindReplaceWindow(IHost host, Window owner, bool replace, string initial) : base(replace ? "Replace" : "Find", 520)
    {
        _host = host;
        Owner = owner;
        HideFooter();
        WindowStartupLocation = WindowStartupLocation.Manual;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Body.Children.Add(grid);

        var fields = new StackPanel { Width = 300 };
        grid.Children.Add(fields);

        fields.Children.Add(Label("Find what:", 4));
        _find = new TextBox { Text = string.IsNullOrEmpty(initial) ? _lastFind : initial, Margin = new Thickness(0, 0, 0, 10) };
        fields.Children.Add(_find);

        _replaceRow = new StackPanel();
        _replaceRow.Children.Add(Label("Replace with:", 4));
        _replace = new TextBox { Text = _lastReplace, Margin = new Thickness(0, 0, 0, 10) };
        _replaceRow.Children.Add(_replace);
        fields.Children.Add(_replaceRow);

        _case = new CheckBox { Content = "Match case", IsChecked = _matchCase };
        _word = new CheckBox { Content = "Match whole word only", IsChecked = _wholeWord };
        _wrapBox = new CheckBox { Content = "Wrap around", IsChecked = _wrap };
        fields.Children.Add(_case);
        fields.Children.Add(_word);
        fields.Children.Add(_wrapBox);
        var dir = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        dir.Children.Add(new TextBlock { Text = "Direction:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        _up = new RadioButton { Content = "Up", GroupName = "dir", Margin = new Thickness(0, 0, 14, 0) };
        _down = new RadioButton { Content = "Down", GroupName = "dir", IsChecked = true };
        dir.Children.Add(_up);
        dir.Children.Add(_down);
        fields.Children.Add(dir);

        _status = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 10, 0, 0), MinHeight = 18 };
        fields.Children.Add(_status);

        var buttons = new StackPanel { Width = 140 };
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        Button B(string text, Action a, bool primary = false)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
            if (primary) { b.Style = (Style)FindResource("AccentButton"); b.IsDefault = true; }
            b.Click += (_, _) => a();
            buttons.Children.Add(b);
            return b;
        }
        B("Find next", () => DoFind(), primary: true);
        _replaceBtn = B("Replace", DoReplace);
        _replaceAllBtn = B("Replace all", DoReplaceAll);
        B("Close", Close);

        SetMode(replace);
        _find.TextChanged += (_, _) => _status.Text = "";
        Loaded += (_, _) =>
        {
            // Park the window at the top-right of the editor so it doesn't cover the text being searched.
            Left = owner.Left + owner.ActualWidth - ActualWidth - 40;
            Top = owner.Top + 170;
            _find.Focus();
            _find.SelectAll();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F3) { DoFind(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; }
        };
    }

    public void SetMode(bool replace, string text = null)
    {
        _replaceMode = replace;
        Title = replace ? "Replace" : "Find";
        _replaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        _replaceBtn.Visibility = _replaceAllBtn.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(text)) _find.Text = text;
        _find.Focus();
        _find.SelectAll();
    }

    private FindOptions Options(bool? forceUp = null)
    {
        _lastFind = _find.Text;
        _lastReplace = _replace.Text;
        _matchCase = _case.IsChecked == true;
        _wholeWord = _word.IsChecked == true;
        _wrap = _wrapBox.IsChecked == true;
        return new FindOptions
        {
            Text = _find.Text, MatchCase = _matchCase, WholeWord = _wholeWord, WrapAround = _wrap,
            Up = forceUp ?? _up.IsChecked == true,
        };
    }

    /// <summary>Last search, used by F3 in the main window.</summary>
    public static FindOptions LastOptions => string.IsNullOrEmpty(_lastFind) ? null :
        new FindOptions { Text = _lastFind, MatchCase = _matchCase, WholeWord = _wholeWord, WrapAround = _wrap };

    private void DoFind(bool up = false)
    {
        if (string.IsNullOrEmpty(_find.Text)) return;
        var o = Options(up ? true : null);
        _status.Text = _host.FindNext(o, quiet: true) ? "" : $"Can't find \"{o.Text}\".";
    }

    private void DoReplace()
    {
        if (string.IsNullOrEmpty(_find.Text)) return;
        var o = Options();
        _host.ReplaceCurrent(o, _replace.Text);
        _status.Text = _host.FindNext(o, quiet: true) ? "" : "No more matches.";
    }

    private void DoReplaceAll()
    {
        if (string.IsNullOrEmpty(_find.Text)) return;
        int n = _host.ReplaceAll(Options(), _replace.Text);
        _status.Text = n == 0 ? $"Can't find \"{_find.Text}\"." : $"Replaced {n} occurrence{(n == 1 ? "" : "s")}.";
    }
}
