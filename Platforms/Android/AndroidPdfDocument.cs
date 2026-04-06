#if ANDROID
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;

namespace ipdfreely.Services;

public sealed class AndroidPdfDocumentFactory : IPdfDocumentFactory
{
    public Task<IPdfDocument?> OpenFromFilePathAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.FromResult<IPdfDocument?>(null);

        try
        {
            var file = new Java.IO.File(path);
            var fd = ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
            if (fd is null)
                return Task.FromResult<IPdfDocument?>(null);

            var renderer = new PdfRenderer(fd);
            return Task.FromResult<IPdfDocument?>(new AndroidPdfDocument(renderer, fd));
        }
        catch
        {
            return Task.FromResult<IPdfDocument?>(null);
        }
    }
}

internal sealed class AndroidPdfDocument : IPdfDocument
{
    private readonly PdfRenderer _renderer;
    private readonly ParcelFileDescriptor _fd;
    private bool _disposed;

    public AndroidPdfDocument(PdfRenderer renderer, ParcelFileDescriptor fd)
    {
        _renderer = renderer;
        _fd = fd;
        PageCount = _renderer.PageCount;
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

        using var page = _renderer.OpenPage(pageIndex);
        var wPt = page.Width;
        var hPt = page.Height;
        if (wPt <= 0 || hPt <= 0)
            return null;

        var scale = (float)(maxPixelWidth / wPt);
        var bmpW = Math.Max(1, (int)Math.Round(wPt * scale));
        var bmpH = Math.Max(1, (int)Math.Round(hPt * scale));

        var bitmap = Bitmap.CreateBitmap(bmpW, bmpH, Bitmap.Config.Argb8888!);
        try
        {
            bitmap.EraseColor(Android.Graphics.Color.White);
            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

            using var ms = new MemoryStream();
            if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, ms))
                return null;

            return new PdfPageBitmap
            {
                PngBytes = ms.ToArray(),
                Width = bmpW,
                Height = bmpH
            };
        }
        finally
        {
            bitmap.Recycle();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _renderer.Dispose();
            _fd.Dispose();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
#endif
