using UglyToad.PdfPig.Core;

namespace ipdfreely.Services;

public enum DetectedFieldKind
{
    Unknown,
    Text,
    Checkbox,
    ComboBox,
    ListBox,
    PushButton,
    RadioGroup,
    Signature,
    NonTerminal,
    VisualUnderline,
    VisualSquare,
    WidgetAnnotation
}

public sealed class DetectedTextRegion
{
    public int PageIndex { get; init; }

    public string LineText { get; init; } = string.Empty;

    public PdfRectangle Bounds { get; init; }
}

public sealed class DetectedFormField
{
    public int PageIndex { get; init; }

    public string Name { get; init; } = string.Empty;

    public DetectedFieldKind Kind { get; init; }

    public PdfRectangle Bounds { get; init; }

    public string? Value { get; init; }

    public string Source { get; init; } = string.Empty;
}

public sealed class PdfContentDetectionResult
{
    public IReadOnlyList<DetectedFormField> AcroFormFields { get; init; } = Array.Empty<DetectedFormField>();

    public IReadOnlyList<DetectedFormField> WidgetFields { get; init; } = Array.Empty<DetectedFormField>();

    public IReadOnlyList<DetectedFormField> VisualHeuristicFields { get; init; } = Array.Empty<DetectedFormField>();

    public IReadOnlyList<DetectedTextRegion> EmbeddedTextLines { get; init; } = Array.Empty<DetectedTextRegion>();
}

public sealed class RasterFormDraw
{
    public int PageIndex { get; init; }

    public byte[] PngBytes { get; init; } = Array.Empty<byte>();

    public int Width { get; init; }

    public int Height { get; init; }

    public IReadOnlyList<PageTextOverlay> TextOverlays { get; init; } = Array.Empty<PageTextOverlay>();
}
