namespace ipdfreely.Services;

public sealed class PdfPageBitmap
{
    public required byte[] PngBytes { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ImageSource ToImageSource() =>
        ImageSource.FromStream(() => new MemoryStream(PngBytes));
}
