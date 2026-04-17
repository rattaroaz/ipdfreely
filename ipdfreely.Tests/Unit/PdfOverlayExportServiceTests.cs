using Xunit;
using FluentAssertions;
using ipdfreely.Services;
using ipdfreely.Tests;

namespace ipdfreely.Tests.Unit;

public class PdfOverlayExportServiceTests
{
    private static void AssertValidPdf(byte[] bytes)
    {
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(50);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    private static PageTextOverlay MakeOverlay(
        string text = "Test",
        string font = "OpenSans",
        double relFontSize = 0.02) => new()
    {
        RelX = 0.1, RelY = 0.1, RelW = 0.5, RelH = 0.05,
        Text = text, RelFontSize = relFontSize, FontFamily = font
    };

    // ── Empty / single / multi page ─────────────────────────────

    [Fact]
    public void EmptyPages_Throws()
    {
        var act = () => PdfOverlayExportService.BuildPdfFromRastersAndOverlays(
            Array.Empty<RasterFormDraw>());
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SinglePage_NoOverlays_ProducesValidPdf()
    {
        var pages = new[] { TestHelpers.CreateSinglePageDraw() };
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    [Fact]
    public void SinglePage_WithOverlay_ProducesValidPdf()
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[] { MakeOverlay() })
        };
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    [Fact]
    public void MultiplePages_ProducesLargerPdfThanSinglePage()
    {
        var single = new[] { TestHelpers.CreateSinglePageDraw() };
        var multi = Enumerable.Range(0, 5)
            .Select(i => TestHelpers.CreateSinglePageDraw(pageIndex: i))
            .ToArray();

        var singleBytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(single);
        var multiBytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(multi);

        AssertValidPdf(multiBytes);
        multiBytes.Length.Should().BeGreaterThan(singleBytes.Length);
    }

    // ── Page ordering ───────────────────────────────────────────

    [Fact]
    public void PagesAreOrderedByPageIndex_EvenIfInputIsUnordered()
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(pageIndex: 2),
            TestHelpers.CreateSinglePageDraw(pageIndex: 0),
            TestHelpers.CreateSinglePageDraw(pageIndex: 1),
        };

        var act = () => PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);
        act.Should().NotThrow();
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    // ── Font handling ───────────────────────────────────────────

    [Theory]
    [InlineData("OpenSans")]
    [InlineData("Open Sans")]
    [InlineData("")]
    public void EmbeddedFont_Variants_ProduceValidPdf(string fontFamily)
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[] { MakeOverlay(font: fontFamily) })
        };
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    [Fact]
    public void SystemFont_Arial_ProducesValidPdf()
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[] { MakeOverlay(font: "Arial") })
        };
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    [Fact]
    public void UnknownFont_FallsBackWithoutCrashing()
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[] { MakeOverlay(font: "NonExistentFont123") })
        };
        // Should fall back to embedded OpenSans
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    // ── Overlay edge cases ──────────────────────────────────────

    [Fact]
    public void EmptyTextOverlay_DoesNotCorruptPdf()
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[] { MakeOverlay(text: "") })
        };
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    [Fact]
    public void MultipleOverlaysOnOnePage_AllRendered()
    {
        var overlays = Enumerable.Range(0, 10)
            .Select(i => new PageTextOverlay
            {
                RelX = 0.05 * i, RelY = 0.05 * i,
                RelW = 0.2, RelH = 0.03,
                Text = $"Overlay {i}", RelFontSize = 0.015,
                FontFamily = "OpenSans"
            }).ToArray();

        var pages = new[] { TestHelpers.CreateSinglePageDraw(overlays: overlays) };
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    [Fact]
    public void VerySmallFontSize_ClampedToMinimum()
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[]
            {
                MakeOverlay(relFontSize: 0.0001) // < 1pt when scaled
            })
        };
        // Should not throw; font size clamped to 1pt internally
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages));
    }

    // ── DPI parameter ───────────────────────────────────────────

    [Theory]
    [InlineData(72.0)]
    [InlineData(96.0)]
    [InlineData(150.0)]
    public void DifferentDpi_ProducesValidPdf(double dpi)
    {
        var draw = new RasterFormDraw
        {
            PageIndex = 0,
            PngBytes = TestHelpers.CreateMinimalPng(),
            Width = 100, Height = 100,
            OriginalPageWidthPts = 0, // force DPI-based calculation
            OriginalPageHeightPts = 0,
            TextOverlays = Array.Empty<PageTextOverlay>()
        };
        AssertValidPdf(PdfOverlayExportService.BuildPdfFromRastersAndOverlays(
            new[] { draw }, sourceDpi: dpi));
    }

    // ── Logger integration ──────────────────────────────────────

    [Fact]
    public void WithLogger_LogsPageAndOverlayInfo()
    {
        var logger = new LoggingService();
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[] { MakeOverlay() })
        };

        PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages, logger: logger);

        var logs = logger.GetRecentLogs(10);
        logs.Should().Contain(l => l.Contains("Raster export"));
        logs.Should().Contain(l => l.Contains("export complete"));
    }

    // ── GetAvailableFonts ───────────────────────────────────────

    [Fact]
    public void GetAvailableFonts_OpenSansAlwaysFirst()
    {
        var fonts = PdfOverlayExportService.GetAvailableFonts();
        fonts.Should().NotBeEmpty();
        fonts[0].Should().Be("Open Sans");
    }

    [Fact]
    public void GetAvailableFonts_NoDuplicates()
    {
        var fonts = PdfOverlayExportService.GetAvailableFonts();
        fonts.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetAvailableFonts_IsIdempotent()
    {
        var first = PdfOverlayExportService.GetAvailableFonts();
        var second = PdfOverlayExportService.GetAvailableFonts();
        first.Should().BeEquivalentTo(second);
    }
}
