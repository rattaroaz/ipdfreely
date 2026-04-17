using System.IO.Compression;
using ipdfreely.Services;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace ipdfreely.Tests;

/// <summary>
/// Shared utilities for test PDF and PNG generation.
/// </summary>
internal static class TestHelpers
{
    private static readonly object FontLock = new();

    /// <summary>
    /// Ensures PdfSharpCore's font resolver is initialized for tests.
    /// </summary>
    public static void EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is not null)
            return;

        lock (FontLock)
        {
            if (GlobalFontSettings.FontResolver is not null)
                return;

            // Trigger the export service's lazy init
            try
            {
                PdfOverlayExportService.GetAvailableFonts();
                // Force a real PDF build to fully init the resolver
                var doc = new PdfDocument();
                doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(doc.Pages[0]);
                gfx.DrawString(" ", new XFont("OpenSans", 8, XFontStyle.Regular), XBrushes.Black, new XPoint(0, 10));
                using var ms = new MemoryStream();
                doc.Save(ms);
            }
            catch
            {
                // Swallow — the resolver should be set by now
            }
        }
    }

    /// <summary>
    /// Creates a minimal valid 1x1 white PNG with correct CRC checksums.
    /// </summary>
    public static byte[] CreateMinimalPng()
    {
        using var ms = new MemoryStream();
        // PNG signature
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        WriteChunk(ms, "IHDR", new byte[]
        {
            0, 0, 0, 1, // width
            0, 0, 0, 1, // height
            8,          // bit depth
            2,          // color type (RGB)
            0, 0, 0     // compression, filter, interlace
        });

        byte[] rawScanline = { 0x00, 0xFF, 0xFF, 0xFF };
        byte[] idatData;
        using (var zlibMs = new MemoryStream())
        {
            zlibMs.WriteByte(0x78);
            zlibMs.WriteByte(0x01);
            using (var deflate = new DeflateStream(zlibMs, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(rawScanline);
            uint adler = Adler32(rawScanline);
            var adlerBytes = BitConverter.GetBytes(adler);
            if (BitConverter.IsLittleEndian) Array.Reverse(adlerBytes);
            zlibMs.Write(adlerBytes);
            idatData = zlibMs.ToArray();
        }
        WriteChunk(ms, "IDAT", idatData);
        WriteChunk(ms, "IEND", Array.Empty<byte>());

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a single-page raster draw suitable for export tests.
    /// </summary>
    public static RasterFormDraw CreateSinglePageDraw(
        int pageIndex = 0,
        double widthPts = 612,
        double heightPts = 792,
        IReadOnlyList<PageTextOverlay>? overlays = null)
    {
        return new RasterFormDraw
        {
            PageIndex = pageIndex,
            PngBytes = CreateMinimalPng(),
            Width = 1,
            Height = 1,
            OriginalPageWidthPts = widthPts,
            OriginalPageHeightPts = heightPts,
            TextOverlays = overlays ?? Array.Empty<PageTextOverlay>()
        };
    }

    /// <summary>
    /// Creates a test PDF on disk with the given text lines and returns the path.
    /// </summary>
    public static string CreatePdfWithText(string tempDir, params string[] lines)
    {
        EnsureFontResolver();
        var path = Path.Combine(tempDir, $"test_{Guid.NewGuid():N}.pdf");
        var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("OpenSans", 12, XFontStyle.Regular);
        var y = 50.0;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(50, y));
            y += 20;
        }

        using var fs = File.Create(path);
        doc.Save(fs);
        return path;
    }

    /// <summary>
    /// Creates a multi-page test PDF on disk.
    /// </summary>
    public static string CreateMultiPagePdf(string tempDir, int pageCount = 2)
    {
        EnsureFontResolver();
        var path = Path.Combine(tempDir, $"multi_{Guid.NewGuid():N}.pdf");
        var doc = new PdfDocument();

        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("OpenSans", 12, XFontStyle.Regular);
            gfx.DrawString($"Page {i + 1} content", font, XBrushes.Black, new XPoint(50, 50));
        }

        using var fs = File.Create(path);
        doc.Save(fs);
        return path;
    }

    /// <summary>
    /// Creates an empty single-page PDF on disk.
    /// </summary>
    public static string CreateEmptyPdf(string tempDir)
    {
        EnsureFontResolver();
        var path = Path.Combine(tempDir, $"empty_{Guid.NewGuid():N}.pdf");
        var doc = new PdfDocument();
        doc.AddPage();
        using var fs = File.Create(path);
        doc.Save(fs);
        return path;
    }

    #region PNG internals

    private static void WriteChunk(MemoryStream ms, string type, byte[] data)
    {
        var length = BitConverter.GetBytes(data.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(length);
        ms.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes);
        ms.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        var crc = Crc32(crcInput);
        var crcBytes = BitConverter.GetBytes(crc);
        if (BitConverter.IsLittleEndian) Array.Reverse(crcBytes);
        ms.Write(crcBytes);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320 * (crc & 1));
        }
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var val in data)
        {
            a = (a + val) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    #endregion
}
