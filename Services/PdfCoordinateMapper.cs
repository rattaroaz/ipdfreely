using Microsoft.Maui.Graphics;
using UglyToad.PdfPig.Core;

namespace ipdfreely.Services;

/// <summary>
/// Maps PDF user space (origin bottom-left, Y increasing upward) to MAUI-style overlay coordinates
/// normalized to the page (origin top-left, X and Y as fractions of page width/height).
/// </summary>
public static class PdfCoordinateMapper
{
    /// <summary>
    /// Converts a PDF-axis-aligned rectangle into relative overlay metrics (top-left based).
    /// </summary>
    public static (double RelX, double RelY, double RelW, double RelH) ToRelativeOverlay(
        PdfRectangle rect,
        double pageWidthPts,
        double pageHeightPts)
    {
        if (pageWidthPts <= 0 || pageHeightPts <= 0)
            return (0, 0, 0, 0);

        var left = rect.Left;
        var bottom = rect.Bottom;
        var w = rect.Width;
        var h = rect.Height;

        var relX = left / pageWidthPts;
        var topPdf = bottom + h;
        var relY = (pageHeightPts - topPdf) / pageHeightPts;
        var relW = w / pageWidthPts;
        var relH = h / pageHeightPts;

        return (Clamp01(relX), Clamp01(relY), Clamp01(relW), Clamp01(relH));
    }

    /// <summary>
    /// Converts normalized overlay coordinates into pixel positions for a view that preserves
    /// the same aspect ratio as the reference page size (proportional scaling).
    /// </summary>
    public static Point AdjustPositionForProportionalSize(
        double relX,
        double relY,
        double layoutWidth,
        double layoutHeight)
    {
        return new Point(relX * layoutWidth, relY * layoutHeight);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
