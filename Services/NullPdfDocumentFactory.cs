namespace ipdfreely.Services;

public sealed class NullPdfDocumentFactory : IPdfDocumentFactory
{
    public Task<IPdfDocument?> OpenFromFilePathAsync(string path, CancellationToken ct = default) =>
        Task.FromResult<IPdfDocument?>(null);
}
