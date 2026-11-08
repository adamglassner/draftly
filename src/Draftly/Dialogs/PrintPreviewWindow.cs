using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Draftly.Services;

namespace Draftly.Dialogs;

/// <summary>Print preview with a themed toolbar. Pages are shown as they will print (white paper).</summary>
public sealed class PrintPreviewWindow : Window
{
    private readonly DocumentViewer _viewer;
    private readonly PrintService.Preview _preview;
    private readonly TextBlock _pages;

    public event Action PrintRequested;
    public event Action PageSetupRequested;

    public PrintPreviewWindow(Window owner, PrintService.Preview preview, string title)
    {
        _preview = preview;
        Owner = owner;
        Title = $"Print preview - {title}";
        Style = (Style)Application.Current.FindResource("DraftlyWindow");
        Width = Math.Max(900, owner.ActualWidth * 0.85);
        Height = Math.Max(640, owner.ActualHeight * 0.9);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ThemeManager.Attach(this);

        var root = new DockPanel();
        var bar = new Border { Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.SetResourceReference(Border.BackgroundProperty, "RibbonBg");
        bar.SetResourceReference(Border.BorderBrushProperty, "Divider");
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // Close sits on the right and always stays visible; the other tools wrap if the window is narrow.
        var barDock = new DockPanel();
        bar.Child = barDock;
        var close = new Button { Content = "Close preview", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        barDock.Children.Add(close);
        var tools = new WrapPanel();
        barDock.Children.Add(tools);
        Button Tool(string glyph, string text, Action a, bool primary = false)
        {
            var b = new Button { Style = (Style)FindResource("RibbonSmall"), Tag = glyph, Content = text, Height = 36 };
            if (primary)
            {
                b = new Button { Style = (Style)FindResource("AccentButton"), Content = text, Margin = new Thickness(0, 0, 10, 0) };
            }
            b.Click += (_, _) => a();
            tools.Children.Add(b);
            return b;
        }
        Tool("", "Print…", () => { PrintRequested?.Invoke(); }, primary: true);
        Tool("", "Page setup", () => PageSetupRequested?.Invoke());
        Tool("", "Zoom out", () => _viewer.DecreaseZoom());
        Tool("", "Zoom in", () => _viewer.IncreaseZoom());
        Tool("", "One page", () => _viewer.FitToMaxPagesAcross(1));
        Tool("", "Two pages", () => _viewer.FitToMaxPagesAcross(2));
        Tool("", "Page width", () => _viewer.FitToWidth());
        _pages = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
        _pages.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        tools.Children.Add(_pages);

        _viewer = new DocumentViewer { Document = preview.Document, Template = ViewerTemplate() };
        _viewer.SetResourceReference(Control.BackgroundProperty, "CanvasBg");
        root.Children.Add(_viewer);
        Content = root;

        _pages.Text = preview.PageCount == 1 ? "1 page" : $"{preview.PageCount} pages";

        Loaded += (_, _) => { _viewer.FitToMaxPagesAcross(1); _viewer.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control) { PrintRequested?.Invoke(); e.Handled = true; }
        };
        Closed += (_, _) => _preview.Dispose();
    }

    /// <summary>A DocumentViewer without its built-in (light-only) toolbar and search box.</summary>
    private static ControlTemplate ViewerTemplate()
    {
        var t = new ControlTemplate(typeof(DocumentViewer));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var sv = new FrameworkElementFactory(typeof(ScrollViewer));
        sv.Name = "PART_ContentHost";
        sv.SetValue(ScrollViewer.CanContentScrollProperty, true);
        sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        sv.SetValue(UIElement.FocusableProperty, false);
        border.AppendChild(sv);
        t.VisualTree = border;
        return t;
    }
}
