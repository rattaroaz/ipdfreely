namespace ipdfreely.Services;

public interface IPdfDocumentFactory
{
    Task<IPdfDocument?> OpenFromFilePathAsync(string path, CancellationToken ct = default);
}
