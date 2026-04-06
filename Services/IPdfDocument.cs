namespace ipdfreely.Services;

public interface IPdfDocument : IAsyncDisposable
{
    int PageCount { get; }

    Task<ImageSource?> GetPageAsync(int pageIndex, double maxPixelWidth, CancellationToken ct = default);

    Task<PdfPageBitmap?> GetPageBitmapAsync(int pageIndex, double maxPixelWidth, CancellationToken ct = default);
}
