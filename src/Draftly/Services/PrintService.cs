using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace Draftly.Services;

/// <summary>Turns the editor's document into printable pages: real paper size, margins, black text on white.</summary>
public static class PrintService
{
    public static DocumentPaginator CreatePaginator(FlowDocument source, PageLayout layout, Size? printableArea = null)
    {
        FlowDocument doc = null;
        DocumentService.WithBlackText(source, () => doc = DocumentService.Clone(source));
        doc.Foreground = Brushes.Black;
        doc.Background = Brushes.White;

        var size = new Size(layout.PaperWidth, layout.PaperHeight);
        doc.PageWidth = size.Width;
        doc.PageHeight = size.Height;
        doc.PagePadding = new Thickness(layout.MarginLeft, layout.MarginTop, layout.MarginRight, layout.MarginBottom);
        doc.ColumnWidth = size.Width;              // one column, full width
        doc.ColumnGap = 0;
        doc.IsOptimalParagraphEnabled = false;
        doc.IsHyphenationEnabled = false;

        var inner = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        inner.PageSize = size;
        return new NumberedPaginator(inner, layout);
    }

    /// <summary>Wraps a paginator to draw "Page N of M" in the bottom margin.</summary>
    private sealed class NumberedPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        private readonly PageLayout _layout;

        public NumberedPaginator(DocumentPaginator inner, PageLayout layout)
        {
            _inner = inner;
            _layout = layout;
            _inner.ComputePageCount();
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            var page = _inner.GetPage(pageNumber);
            if (!_layout.PageNumbers) return page;

            var visual = new ContainerVisual();
            visual.Children.Add(page.Visual);
            var footer = new DrawingVisual();
            using (var dc = footer.RenderOpen())
            {
                var text = new FormattedText($"Page {pageNumber + 1} of {PageCount}", CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12 * 96 / 72.0 * 0.8, Brushes.DimGray, 1.0);
                double y = _layout.PaperHeight - Math.Max(24, _layout.MarginBottom / 2) - text.Height / 2;
                dc.DrawText(text, new Point((_layout.PaperWidth - text.Width) / 2, y));
            }
            visual.Children.Add(footer);
            return new DocumentPage(visual, page.Size, page.BleedBox, page.ContentBox);
        }

        public override bool IsPageCountValid => _inner.IsPageCountValid;
        public override int PageCount => _inner.PageCount;
        public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _inner.Source;
    }

    // ------------------------------------------------------------------ preview

    /// <summary>Holds an in-memory XPS rendering of the document for the preview window.</summary>
    public sealed class Preview : IDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly Package _package;
        private readonly XpsDocument _xps;
        private readonly Uri _uri;
        public FixedDocumentSequence Document { get; }
        public int PageCount { get; }

        public Preview(DocumentPaginator paginator)
        {
            _uri = new Uri($"memorystream://draftly-preview-{Guid.NewGuid():N}.xps");
            _package = Package.Open(_stream, FileMode.Create, FileAccess.ReadWrite);
            PackageStore.AddPackage(_uri, _package);
            _xps = new XpsDocument(_package, CompressionOption.Fast, _uri.AbsoluteUri);
            var writer = XpsDocument.CreateXpsDocumentWriter(_xps);
            writer.Write(paginator);
            Document = _xps.GetFixedDocumentSequence();
            PageCount = paginator.PageCount;
        }

        public void Dispose()
        {
            try { _xps.Close(); } catch { }
            try { PackageStore.RemovePackage(_uri); } catch { }
            try { _package.Close(); } catch { }
            _stream.Dispose();
        }
    }
}
