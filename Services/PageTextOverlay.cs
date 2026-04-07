namespace ipdfreely.Services;

public sealed class PageTextOverlay
{
    public double RelX { get; init; }
    public double RelY { get; init; }
    public double RelW { get; init; }
    public double RelH { get; init; }
    public string Text { get; init; } = string.Empty;
    public double RelFontSize { get; init; }
}
