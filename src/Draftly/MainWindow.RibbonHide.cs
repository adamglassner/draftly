using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Draftly.Services;

namespace Draftly;

/// <summary>
/// "Always show toolbar" / "Automatically hide toolbar" for the ribbon, like Windows 11 Paint.
/// When hidden, only the Home/View tab names stay; clicking one drops the ribbon down over the page.
/// It hides again after a command, a click on the page, Esc, or switching to another window.
/// </summary>
public partial class MainWindow
{
    private bool _ribbonInPopup;

    private bool RibbonAutoHide => S.AutoHideRibbon && !IsClassic;

    /// <summary>Puts the ribbon in the window or in the drop-down, per the setting. Called from ApplySettings.</summary>
    private void ApplyRibbonDisplay()
    {
        bool hide = RibbonAutoHide;
        RibbonOptionsBtn.Content = hide ? "" : "";   // chevron down / up, as in Paint
        if (hide == _ribbonInPopup) return;

        if (hide)
        {
            RootGrid.Children.Remove(RibbonBorder);
            RibbonPopup.Child = RibbonBorder;
            RibbonBorder.Visibility = Visibility.Visible;
        }
        else
        {
            RibbonPopup.IsOpen = false;
            RibbonPopup.Child = null;
            Grid.SetRow(RibbonBorder, 1);
            RootGrid.Children.Insert(RootGrid.Children.IndexOf(RibbonPopup) + 1, RibbonBorder);
            RibbonBorder.Visibility = IsClassic ? Visibility.Collapsed : Visibility.Visible;
        }
        _ribbonInPopup = hide;
        SizeRibbonPopup();
    }

    private void InitRibbonHide()
    {
        RibbonPopup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !(e.OriginalSource is ComboBox { IsDropDownOpen: true }))
            {
                HideRibbonPopup();
                e.Handled = true;
            }
        };
        // A click anywhere else in the window (the page, the status bar...) hides it.
        PreviewMouseDown += (_, e) =>
        {
            if (!RibbonPopup.IsOpen || e.OriginalSource is not DependencyObject d) return;
            if (IsWithin(d, RibbonBorder)) return;                         // a click on the ribbon itself, or its font lists
            if (IsWithin(d, TabHome) || IsWithin(d, TabView)) return;      // Tab_Click decides
            RibbonPopup.IsOpen = false;
        };
        Deactivated += (_, _) => RibbonPopup.IsOpen = false;
        LocationChanged += (_, _) => RibbonPopup.IsOpen = false;
        SizeChanged += (_, _) => SizeRibbonPopup();

        // Using a command hides it again (unless the command opened a menu or colour picker: then the next click does).
        RibbonBorder.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => HideRibbonAfterCommand()), true);
        RibbonBorder.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) =>
        {
            if (e.OriginalSource is ComboBox { IsDropDownOpen: false } && RibbonPopup.IsOpen) HideRibbonAfterCommand();
        }), true);
    }

    private void SizeRibbonPopup()
    {
        RibbonBorder.Width = _ribbonInPopup && TabRow.ActualWidth > 0 ? Math.Max(200, TabRow.ActualWidth - 16) : double.NaN;
    }

    private void HideRibbonAfterCommand()
    {
        if (!RibbonPopup.IsOpen) return;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!RibbonPopup.IsOpen || OtherPopupOpen() || RibbonBorder.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox or ComboBox) return;
            RibbonPopup.IsOpen = false;
        }));
    }

    /// <summary>Is a menu, list or colour picker (opened from the ribbon) showing?</summary>
    private bool OtherPopupOpen()
    {
        var ribbonSource = PresentationSource.FromVisual(RibbonBorder);
        foreach (PresentationSource src in PresentationSource.CurrentSources)
            if (src is HwndSource h && !ReferenceEquals(src, ribbonSource) && h.RootVisual?.GetType().Name == "PopupRoot" &&
                h.RootVisual is UIElement { IsVisible: true })
                return true;
        return false;
    }

    /// <summary>Like IsInside, but also crosses from a drop-down list back to the control that opened it.</summary>
    private static bool IsWithin(DependencyObject d, DependencyObject ancestor)
    {
        for (int guard = 0; d != null && guard < 500; guard++)
        {
            if (d == ancestor) return true;
            var visual = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : null;
            d = visual == null || visual.GetType().Name == "PopupRoot"
                ? LogicalTreeHelper.GetParent(d) ?? (d as FrameworkElement)?.TemplatedParent
                : visual;
        }
        return false;
    }

    private void HideRibbonPopup()
    {
        RibbonPopup.IsOpen = false;
        Editor.Focus();
    }

    /// <summary>With the toolbar hidden, clicking a tab shows the ribbon; clicking the showing tab again hides it.</summary>
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (!_ribbonInPopup) return;
        if (RibbonPopup.IsOpen && ReferenceEquals(sender, _popupTab)) { HideRibbonPopup(); return; }
        _popupTab = sender;
        SizeRibbonPopup();
        RibbonPopup.IsOpen = true;
    }

    private object _popupTab;

    /// <summary>Double-clicking a tab hides or shows the toolbar, as in Office.</summary>
    private void Tab_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        SetRibbonAutoHide(!S.AutoHideRibbon);
        e.Handled = true;
    }

    private void SetRibbonAutoHide(bool hide)
    {
        RibbonPopup.IsOpen = false;
        if (S.AutoHideRibbon == hide) return;
        S.AutoHideRibbon = hide;
        SettingsService.Commit();
        Editor.Focus();
    }

    /// <summary>The chevron at the top right: Paint's two toolbar choices.</summary>
    private void RibbonOptions_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RibbonOptionsBtn, Placement = PlacementMode.Bottom };
        MenuItem Option(string text, bool hide)
        {
            var mi = new MenuItem { Header = text, IsCheckable = true, IsChecked = S.AutoHideRibbon == hide };
            mi.Click += (_, _) => SetRibbonAutoHide(hide);
            return mi;
        }
        menu.Items.Add(Option("Always show toolbar", false));
        menu.Items.Add(Option("Automatically hide toolbar", true));
        menu.IsOpen = true;
    }
}
