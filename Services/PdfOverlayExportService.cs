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
    /// Maps display font name → .ttf filename in the system Fonts directory.
    /// </summary>
    private static readonly Dictionary<string, string> SystemFontFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arial"] = "arial.ttf",
        ["Times New Roman"] = "times.ttf",
        ["Courier New"] = "cour.ttf",
        ["Verdana"] = "verdana.ttf",
        ["Georgia"] = "georgia.ttf",
        ["Calibri"] = "calibri.ttf",
        ["Cambria"] = "cambria.ttc",
        ["Segoe UI"] = "segoeui.ttf",
        ["Consolas"] = "consola.ttf",
        ["Tahoma"] = "tahoma.ttf",
        ["Trebuchet MS"] = "trebuc.ttf",
        ["Comic Sans MS"] = "comic.ttf",
        ["Palatino Linotype"] = "pala.ttf",
        ["Lucida Console"] = "lucon.ttf",
        ["Impact"] = "impact.ttf",
    };

    /// <summary>
    /// Returns the list of font names available for user text overlays.
    /// Only includes fonts actually present on the system.
    /// "Open Sans" (embedded) is always first.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableFonts()
    {
        var fonts = new List<string> { "Open Sans" };
        var fontsDir = GetSystemFontsDirectory();
        if (fontsDir is null)
            return fonts;

        foreach (var (name, file) in SystemFontFiles)
        {
            if (File.Exists(Path.Combine(fontsDir, file)))
                fonts.Add(name);
        }

        return fonts;
    }

    private static string? GetSystemFontsDirectory()
    {
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            return Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the original PDF and draws text overlays directly on top of existing pages.
    /// Preserves all original content (vector graphics, selectable text, images).
    /// For field edits, a white rectangle is drawn first to cover the original value.
    /// </summary>
    public static byte[] BuildPdfWithPreservedContent(
        byte[] originalPdfBytes,
        IReadOnlyDictionary<int, IReadOnlyList<PageTextOverlay>> pageOverlays,
        ILoggingService? logger = null)
    {
        EnsurePdfSharpFonts();
        logger?.LogInfo("Preserved-content export: {0} bytes input, {1} pages with overlays",
            originalPdfBytes.Length, pageOverlays.Count);

        using var srcStream = new MemoryStream(originalPdfBytes);
        var doc = PdfReader.Open(srcStream, PdfDocumentOpenMode.Modify);
        logger?.LogInfo("Opened PDF for modify: {0} pages", doc.PageCount);

        foreach (var (pageIndex, overlays) in pageOverlays)
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount)
            {
                logger?.LogWarning("Skipping invalid page index {0} (doc has {1} pages)", pageIndex, doc.PageCount);
                continue;
            }

            var page = doc.Pages[pageIndex];
            var wPt = page.Width.Point;
            var hPt = page.Height.Point;
            logger?.LogDebug("Page {0}: {1:F1}x{2:F1} pts, {3} overlays", pageIndex, wPt, hPt, overlays.Count);

            // Append draws on top of existing content
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            DrawOverlays(gfx, overlays, wPt, hPt, logger);
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        logger?.LogInfo("Preserved-content export complete: {0} bytes output", ms.Length);
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
        double sourceDpi = 96.0,
        ILoggingService? logger = null)
    {
        EnsurePdfSharpFonts();
        logger?.LogInfo("Raster export: {0} pages, source DPI {1}", pages.Count, sourceDpi);

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
            logger?.LogDebug("Page {0}: {1:F1}x{2:F1} pts, raster {3}x{4}, {5} overlays",
                draw.PageIndex, wPt, hPt, draw.Width, draw.Height, draw.TextOverlays.Count);

            using var gfx = XGraphics.FromPdfPage(page);

            if (draw.PngBytes.Length > 0)
            {
                using var img = XImage.FromStream(() => new MemoryStream(draw.PngBytes));
                gfx.DrawImage(img, 0, 0, page.Width, page.Height);
            }

            DrawOverlays(gfx, draw.TextOverlays, wPt, hPt, logger);
        }

        using var ms = new MemoryStream();
        doc.Save(ms);
        logger?.LogInfo("Raster export complete: {0} bytes output", ms.Length);
        return ms.ToArray();
    }

    private static void DrawOverlays(
        XGraphics gfx,
        IReadOnlyList<PageTextOverlay> overlays,
        double wPt,
        double hPt,
        ILoggingService? logger = null)
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

            var pdfFontName = string.IsNullOrEmpty(overlay.FontFamily) || overlay.FontFamily == "Open Sans"
                ? "OpenSans"
                : overlay.FontFamily;

            try
            {
                var font = new XFont(pdfFontName, scaledFontPts, XFontStyle.Regular);
                tf.DrawString(overlay.Text, font, XBrushes.Black,
                    new XRect(xPt, yPt, overlayWPt, overlayHPt));
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Failed to draw overlay text '{0}' with font '{1}': {2}",
                    overlay.Text?.Length > 20 ? overlay.Text[..20] + "..." : overlay.Text ?? "",
                    pdfFontName, ex.Message);
            }
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

            GlobalFontSettings.FontResolver ??= new SystemAwareFontResolver();
            _fontsInitialized = true;
        }
    }

    private sealed class SystemAwareFontResolver : IFontResolver
    {
        public string DefaultFontName => EmbeddedFontFace;

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            // "OpenSans" or unknown → embedded font
            if (string.Equals(familyName, "OpenSans", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(familyName, "Open Sans", StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo(EmbeddedFontFace);

            // Known system font → use its .ttf filename as the face name
            if (SystemFontFiles.TryGetValue(familyName, out var ttfFile))
            {
                var fontsDir = GetSystemFontsDirectory();
                if (fontsDir is not null && File.Exists(Path.Combine(fontsDir, ttfFile)))
                    return new FontResolverInfo(ttfFile);
            }

            // Fallback to embedded OpenSans
            return new FontResolverInfo(EmbeddedFontFace);
        }

        public byte[]? GetFont(string faceName)
        {
            // Embedded OpenSans
            if (faceName == EmbeddedFontFace)
            {
                using var stream = typeof(PdfOverlayExportService).Assembly
                    .GetManifestResourceStream("ipdfreely.OpenSansRegular.ttf");
                if (stream is null)
                    return null;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            // System font file
            var fontsDir = GetSystemFontsDirectory();
            if (fontsDir is not null)
            {
                var fullPath = Path.Combine(fontsDir, faceName);
                if (File.Exists(fullPath))
                    return File.ReadAllBytes(fullPath);
            }

            return null;
        }
    }
}
