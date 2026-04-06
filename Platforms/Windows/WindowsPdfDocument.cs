#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ipdfreely.Services;

public sealed class WindowsPdfDocumentFactory : IPdfDocumentFactory
{
    public async Task<IPdfDocument?> OpenFromFilePathAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(bytes.AsBuffer()).AsTask(ct);
            ras.Seek(0);

            var doc = await PdfDocument.LoadFromStreamAsync(ras).AsTask(ct);
            if (doc is null)
            {
                ras.Dispose();
                return null;
            }

            if (doc.IsPasswordProtected)
            {
                ras.Dispose();
                return null;
            }

            return new WindowsPdfDocument(doc, ras);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class WindowsPdfDocument : IPdfDocument
{
    private readonly PdfDocument _document;
    private readonly IRandomAccessStream _sourceStream;
    private bool _disposed;

    public WindowsPdfDocument(PdfDocument document, IRandomAccessStream sourceStream)
    {
        _document = document;
        _sourceStream = sourceStream;
        PageCount = (int)document.PageCount;
    }

    public int PageCount { get; }

    public async Task<ImageSource?> GetPageAsync(int pageIndex, double maxPixelWidth, CancellationToken ct = default)
    {
        var bmp = await GetPageBitmapAsync(pageIndex, maxPixelWidth, ct).ConfigureAwait(false);
        return bmp?.ToImageSource();
    }

    public async Task<PdfPageBitmap?> GetPageBitmapAsync(int pageIndex, double maxPixelWidth, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= PageCount || maxPixelWidth <= 0)
            return null;

        using var page = _document.GetPage((uint)pageIndex);
        var size = page.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return null;

        var scale = maxPixelWidth / size.Width;
        var destW = (uint)Math.Clamp(Math.Round(size.Width * scale), 1, 16384);
        var destH = (uint)Math.Clamp(Math.Round(size.Height * scale), 1, 16384);

        var options = new PdfPageRenderOptions
        {
            DestinationWidth = destW,
            DestinationHeight = destH
        };

        using var ras = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(ras, options).AsTask(ct);

        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(ct);
        var software = await decoder.GetSoftwareBitmapAsync().AsTask(ct);

        using var outRas = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outRas).AsTask(ct);
        encoder.SetSoftwareBitmap(software);
        await encoder.FlushAsync().AsTask(ct);

        outRas.Seek(0);
        using var ms = new MemoryStream();
        await outRas.AsStreamForRead().CopyToAsync(ms, ct).ConfigureAwait(false);

        return new PdfPageBitmap
        {
            PngBytes = ms.ToArray(),
            Width = (int)destW,
            Height = (int)destH
        };
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _sourceStream.Dispose();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
#endif
