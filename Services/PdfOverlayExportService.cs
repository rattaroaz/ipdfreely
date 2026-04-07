using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace ipdfreely.Services;

public static class PdfOverlayExportService
{
    private const string EmbeddedFontFace = "OpenSansEmbedded#";

    private static readonly object FontInitLock = new();
    private static bool _fontsInitialized;

    /// <summary>
    /// Builds a new PDF from full-page PNG rasters and optional overlay text per page.
    /// Raster dimensions are interpreted at <paramref name="sourceDpi"/> to size each PDF page in points.
    /// Overlay positions use top-left normalized coordinates (0–1) matching <see cref="PdfCoordinateMapper"/>.
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
            var wPt = draw.Width * 72.0 / sourceDpi;
            var hPt = draw.Height * 72.0 / sourceDpi;
            page.Width = XUnit.FromPoint(wPt);
            page.Height = XUnit.FromPoint(hPt);

            using var gfx = XGraphics.FromPdfPage(page);

            if (draw.PngBytes.Length > 0)
            {
                using var img = XImage.FromStream(() => new MemoryStream(draw.PngBytes));
                gfx.DrawImage(img, 0, 0, page.Width, page.Height);
            }

            foreach (var overlay in draw.TextOverlays)
            {
                var xPt = overlay.RelX * wPt;
                var yPt = overlay.RelY * hPt;
                var overlayWPt = overlay.RelW * wPt;
                var overlayHPt = overlay.RelH * hPt;
                
                // The font size in PDF points is proportional to the page height in PDF points
                var scaledFontPts = overlay.RelFontSize * hPt;
                
                var font = new XFont("OpenSans", scaledFontPts, XFontStyle.Regular);
                
                // Adjust Y slightly down because TopLeft in PdfSharp still puts the baseline near the top,
                // but actually TopLeft with a Rect aligns the top of the text to the top of the rect.
                gfx.DrawString(overlay.Text, font, XBrushes.Black, new XRect(xPt, yPt, overlayWPt, overlayHPt), XStringFormats.TopLeft);
            }
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
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
