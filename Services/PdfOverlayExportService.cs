using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace ipdfreely.Services;

public static class PdfOverlayExportService
{
    private const string EmbeddedFontFace = "OpenSansEmbedded#";

    private static readonly object FontInitLock = new();
    private static bool _fontsInitialized;

    /// <summary>
    /// Opens the original PDF and draws text overlays directly on top of existing pages.
    /// Preserves all original content (vector graphics, selectable text, images).
    /// For field edits, a white rectangle is drawn first to cover the original value.
    /// </summary>
    public static byte[] BuildPdfWithPreservedContent(
        byte[] originalPdfBytes,
        IReadOnlyDictionary<int, IReadOnlyList<PageTextOverlay>> pageOverlays)
    {
        EnsurePdfSharpFonts();

        using var srcStream = new MemoryStream(originalPdfBytes);
        var doc = PdfReader.Open(srcStream, PdfDocumentOpenMode.Modify);

        foreach (var (pageIndex, overlays) in pageOverlays)
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount)
                continue;

            var page = doc.Pages[pageIndex];
            var wPt = page.Width.Point;
            var hPt = page.Height.Point;

            // Append draws on top of existing content
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            DrawOverlays(gfx, overlays, wPt, hPt);
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a new PDF from full-page PNG rasters and optional overlay text per page.
    /// Uses the original page dimensions so that exported text size and position
    /// match the on-screen overlay exactly when viewed at the same page scale.
    /// Overlay positions use top-left normalized coordinates (0–1).
    /// </summary>
    public static byte[] BuildPdfFromRastersAndOverlays(
        IReadOnlyList<RasterFormDraw> pages,
        double sourceDpi = 96.0)
    {
        EnsurePdfSharpFonts();

        var doc = new PdfDocument();
        foreach (var draw in pages.OrderBy(p => p.PageIndex))
        {
            var page = doc.AddPage();

            var wPt = draw.OriginalPageWidthPts > 0
                ? draw.OriginalPageWidthPts
                : draw.Width * 72.0 / sourceDpi;
            var hPt = draw.OriginalPageHeightPts > 0
                ? draw.OriginalPageHeightPts
                : draw.Height * 72.0 / sourceDpi;

            page.Width = XUnit.FromPoint(wPt);
            page.Height = XUnit.FromPoint(hPt);

            using var gfx = XGraphics.FromPdfPage(page);

            if (draw.PngBytes.Length > 0)
            {
                using var img = XImage.FromStream(() => new MemoryStream(draw.PngBytes));
                gfx.DrawImage(img, 0, 0, page.Width, page.Height);
            }

            DrawOverlays(gfx, draw.TextOverlays, wPt, hPt);
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static void DrawOverlays(
        XGraphics gfx,
        IReadOnlyList<PageTextOverlay> overlays,
        double wPt,
        double hPt)
    {
        var tf = new XTextFormatter(gfx);

        foreach (var overlay in overlays)
        {
            var xPt = overlay.RelX * wPt;
            var yPt = overlay.RelY * hPt;
            var overlayWPt = overlay.RelW * wPt;
            var overlayHPt = overlay.RelH * hPt;

            var scaledFontPts = overlay.RelFontSize * hPt;
            if (scaledFontPts < 1.0)
                scaledFontPts = 1.0;

            var font = new XFont("OpenSans", scaledFontPts, XFontStyle.Regular);

            tf.DrawString(overlay.Text, font, XBrushes.Black,
                new XRect(xPt, yPt, overlayWPt, overlayHPt));
        }
    }

    private static void EnsurePdfSharpFonts()
    {
        if (_fontsInitialized)
            return;

        lock (FontInitLock)
        {
            if (_fontsInitialized)
                return;

            GlobalFontSettings.FontResolver ??= new EmbeddedOpenSansFontResolver();
            _fontsInitialized = true;
        }
    }

    private sealed class EmbeddedOpenSansFontResolver : IFontResolver
    {
        public string DefaultFontName => EmbeddedFontFace;

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new FontResolverInfo(EmbeddedFontFace);

        public byte[]? GetFont(string faceName)
        {
            if (faceName != EmbeddedFontFace)
                return null;

            using var stream = typeof(PdfOverlayExportService).Assembly.GetManifestResourceStream("ipdfreely.OpenSansRegular.ttf");
            if (stream is null)
                return null;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
