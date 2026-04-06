#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using PDFKit;
using UIKit;

namespace ipdfreely.Services;

public sealed class ApplePdfDocumentFactory : IPdfDocumentFactory
{
    public Task<IPdfDocument?> OpenFromFilePathAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.FromResult<IPdfDocument?>(null);

        try
        {
            var url = NSUrl.CreateFileUrl(path, null);
            var doc = new PdfDocument(url);
            if (doc.PageCount == 0)
            {
                doc.Dispose();
                return Task.FromResult<IPdfDocument?>(null);
            }

            return Task.FromResult<IPdfDocument?>(new ApplePdfDocument(doc));
        }
        catch
        {
            return Task.FromResult<IPdfDocument?>(null);
        }
    }
}

internal sealed class ApplePdfDocument : IPdfDocument
{
    private readonly PdfDocument _document;
    private bool _disposed;

    public ApplePdfDocument(PdfDocument document)
    {
        _document = document;
        PageCount = (int)(nint)document.PageCount;
    }

    public int PageCount { get; }

    public async Task<ImageSource?> GetPageAsync(int pageIndex, double maxPixelWidth, CancellationToken ct = default)
    {
        var bmp = await GetPageBitmapAsync(pageIndex, maxPixelWidth, ct).ConfigureAwait(false);
        return bmp?.ToImageSource();
    }

    public Task<PdfPageBitmap?> GetPageBitmapAsync(int pageIndex, double maxPixelWidth, CancellationToken ct = default) =>
        Task.Run(() => RenderPage(pageIndex, maxPixelWidth), ct);

    private PdfPageBitmap? RenderPage(int pageIndex, double maxPixelWidth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= PageCount || maxPixelWidth <= 0)
            return null;

        var page = _document.GetPage((nint)pageIndex);
        if (page is null)
            return null;

        var bounds = page.GetBoundsForBox(PdfDisplayBox.MediaBox);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var scale = maxPixelWidth / bounds.Width;
        var pixelW = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var pixelH = Math.Max(1, (int)Math.Round(bounds.Height * scale));

        var format = UIGraphicsImageRendererFormat.DefaultFormat;
        format.Opaque = true;
        format.Scale = 1f;

        var renderer = new UIGraphicsImageRenderer(new CGSize(pixelW, pixelH), format);
        using var image = renderer.CreateImage(ctx =>
        {
            ctx.SetFillColor(UIColor.White.CGColor);
            ctx.FillRect(new CGRect(0, 0, pixelW, pixelH));
            ctx.SaveState();
            ctx.ScaleCTM((nfloat)scale, (nfloat)scale);
            page.Draw(ctx, bounds);
            ctx.RestoreState();
        });

        using var data = image.AsPNG();
        if (data is null)
            return null;

        return new PdfPageBitmap
        {
            PngBytes = data.ToArray(),
            Width = pixelW,
            Height = pixelH
        };
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _document.Dispose();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
#endif
