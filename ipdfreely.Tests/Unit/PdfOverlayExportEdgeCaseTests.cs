using FluentAssertions;
using ipdfreely.Services;
using ipdfreely.Tests;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace ipdfreely.Tests.Unit;

/// <summary>
/// Edge cases for raster overlay export: out-of-bounds coordinates,
/// large page counts, empty bitmaps, font resolver concurrency.
/// </summary>
public class PdfOverlayExportEdgeCaseTests
{
    // ── Overlay geometry ────────────────────────────────────────

    [Fact]
    public void OverlayOutsidePageBounds_DoesNotThrow()
    {
        var overlay = new PageTextOverlay
        {
            RelX = 1.5, RelY = 1.5, RelW = 0.5, RelH = 0.05,
            Text = "Outside", RelFontSize = 0.02
        };
        var pages = new[] { TestHelpers.CreateSinglePageDraw(overlays: new[] { overlay }) };

        var act = () => PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);
        act.Should().NotThrow();
    }

    [Fact]
    public void NegativeOverlayCoordinates_DoesNotThrow()
    {
        var overlay = new PageTextOverlay
        {
            RelX = -0.2, RelY = -0.2, RelW = 0.3, RelH = 0.05,
            Text = "Neg", RelFontSize = 0.02
        };
        var pages = new[] { TestHelpers.CreateSinglePageDraw(overlays: new[] { overlay }) };

        var act = () => PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);
        act.Should().NotThrow();
    }

    [Fact]
    public void ZeroSizedOverlay_DoesNotThrow()
    {
        var overlay = new PageTextOverlay
        {
            RelX = 0.5, RelY = 0.5, RelW = 0, RelH = 0,
            Text = "Dot", RelFontSize = 0.02
        };
        var pages = new[] { TestHelpers.CreateSinglePageDraw(overlays: new[] { overlay }) };

        var act = () => PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);
        act.Should().NotThrow();
    }

    [Fact]
    public void VeryLongOverlayText_ProducesValidPdf()
    {
        var overlay = new PageTextOverlay
        {
            RelX = 0.0, RelY = 0.0, RelW = 1.0, RelH = 0.9,
            Text = new string('A', 2000),
            RelFontSize = 0.02,
            FontFamily = "OpenSans"
        };
        var pages = new[] { TestHelpers.CreateSinglePageDraw(overlays: new[] { overlay }) };

        var bytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);
        bytes.Length.Should().BeGreaterThan(50);

        using var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.ReadOnly);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void FailingOverlayFont_LogsWarningButStillProducesValidPdf()
    {
        // Pass an overlay with a font family that doesn't resolve to a real face
        // AND exercise the try/catch path in DrawOverlays by truncating text logging.
        var longText = new string('x', 50);
        var overlay = new PageTextOverlay
        {
            RelX = 0.1, RelY = 0.1, RelW = 0.5, RelH = 0.05,
            Text = longText, RelFontSize = 0.02,
            FontFamily = "NonExistentFamily_XYZ"
        };
        using var logger = new LoggingService();
        var pages = new[] { TestHelpers.CreateSinglePageDraw(overlays: new[] { overlay }) };

        var bytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages, logger: logger);
        bytes.Length.Should().BeGreaterThan(50);

        // Either draws successfully (fallback to OpenSans) or logs the failure — both are acceptable.
        logger.GetRecentLogs(50).Should().NotBeEmpty();
    }

    // ── Bitmap / page shape ─────────────────────────────────────

    [Fact]
    public void EmptyPngBytes_SkipsImageDrawButPageCountIsCorrect()
    {
        var draw = new RasterFormDraw
        {
            PageIndex = 0,
            PngBytes = Array.Empty<byte>(),
            Width = 1, Height = 1,
            OriginalPageWidthPts = 612, OriginalPageHeightPts = 792
        };
        var bytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(new[] { draw });

        using var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.ReadOnly);
        reopened.PageCount.Should().Be(1);
        reopened.Pages[0].Width.Point.Should().BeApproximately(612, 0.5);
        reopened.Pages[0].Height.Point.Should().BeApproximately(792, 0.5);
    }

    [Fact]
    public void LargeNumberOfPages_AllWritten()
    {
        var pages = Enumerable.Range(0, 40)
            .Select(i => TestHelpers.CreateSinglePageDraw(pageIndex: i))
            .ToArray();

        var bytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);

        using var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.ReadOnly);
        reopened.PageCount.Should().Be(40);
    }

    [Fact]
    public void DuplicatePageIndex_ProducesOneEntryPerDrawCall()
    {
        // The service does not dedupe; each draw yields a page.
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(pageIndex: 0),
            TestHelpers.CreateSinglePageDraw(pageIndex: 0),
        };

        var bytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);

        using var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.ReadOnly);
        reopened.PageCount.Should().Be(2);
    }

    // ── Font resolver concurrency ───────────────────────────────

    [Fact]
    public void GetAvailableFonts_ConcurrentCalls_ProduceSameResult()
    {
        var results = new List<IReadOnlyList<string>>();
        Parallel.For(0, 10, _ =>
        {
            var fonts = PdfOverlayExportService.GetAvailableFonts();
            lock (results) results.Add(fonts);
        });

        results.Should().HaveCount(10);
        results[0].Should().NotBeEmpty();
        foreach (var r in results)
            r.Should().BeEquivalentTo(results[0]);
    }

    [Fact]
    public void ConcurrentExports_AllProduceValidPdfs()
    {
        // Exercises the font-init race condition path with parallel exports.
        byte[][] results = new byte[8][];

        Parallel.For(0, results.Length, i =>
        {
            var pages = new[]
            {
                TestHelpers.CreateSinglePageDraw(overlays: new[]
                {
                    new PageTextOverlay
                    {
                        RelX = 0.1, RelY = 0.1, RelW = 0.4, RelH = 0.05,
                        Text = $"thread-{i}", RelFontSize = 0.02
                    }
                })
            };
            results[i] = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);
        });

        foreach (var b in results)
        {
            b.Should().NotBeNull();
            b.Length.Should().BeGreaterThan(50);
            System.Text.Encoding.ASCII.GetString(b, 0, 5).Should().Be("%PDF-");
        }
    }
}
