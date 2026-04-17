using FluentAssertions;
using ipdfreely.Services;
using ipdfreely.Tests;
using PdfSharpCore.Pdf.IO;
using UglyToad.PdfPig;
using Xunit;

namespace ipdfreely.Tests.Unit;

/// <summary>
/// Roundtrip tests for <see cref="PdfOverlayExportService.BuildPdfWithPreservedContent"/>.
/// These reopen the produced PDF and verify structural invariants
/// (page count, original text preservation, overlay index validation).
/// </summary>
public sealed class PdfOverlayExportPreservedContentTests : IDisposable
{
    private readonly string _tempDir;

    public PdfOverlayExportPreservedContentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "ipdfreely_preserve_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Structural invariants ──────────────────────────────────

    [Fact]
    public void NoOverlays_PreservesPageCount()
    {
        var path = TestHelpers.CreateMultiPagePdf(_tempDir, pageCount: 3);
        var bytes = File.ReadAllBytes(path);

        var result = PdfOverlayExportService.BuildPdfWithPreservedContent(
            bytes, new Dictionary<int, IReadOnlyList<PageTextOverlay>>());

        using var doc = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(3);
    }

    [Fact]
    public void WithOverlays_PreservesOriginalEmbeddedText()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "PreserveMe", "Line2");
        var bytes = File.ReadAllBytes(path);

        var overlays = new Dictionary<int, IReadOnlyList<PageTextOverlay>>
        {
            [0] = new[]
            {
                new PageTextOverlay
                {
                    RelX = 0.5, RelY = 0.5, RelW = 0.2, RelH = 0.05,
                    Text = "Extra", RelFontSize = 0.02, FontFamily = "OpenSans"
                }
            }
        };

        var result = PdfOverlayExportService.BuildPdfWithPreservedContent(bytes, overlays);

        using var pig = PdfDocument.Open(result);
        var extracted = string.Join(" ", pig.GetPages()
            .SelectMany(p => p.GetWords())
            .Select(w => w.Text));
        extracted.Should().Contain("PreserveMe");
        extracted.Should().Contain("Line2");
    }

    [Fact]
    public void MultiPageWithPerPageOverlays_PreservesPageCountAndGeometry()
    {
        var path = TestHelpers.CreateMultiPagePdf(_tempDir, pageCount: 3);
        var bytes = File.ReadAllBytes(path);

        var overlays = new Dictionary<int, IReadOnlyList<PageTextOverlay>>
        {
            [0] = new[] { Overlay("A") },
            [1] = new[] { Overlay("B") },
            [2] = new[] { Overlay("C") }
        };

        var result = PdfOverlayExportService.BuildPdfWithPreservedContent(bytes, overlays);

        using var doc = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(3);
        foreach (var page in doc.Pages)
        {
            page.Width.Point.Should().BeGreaterThan(100);
            page.Height.Point.Should().BeGreaterThan(100);
        }
    }

    // ── Invalid indices ─────────────────────────────────────────

    [Fact]
    public void OutOfRangePageIndex_LogsWarningAndSkips()
    {
        var path = TestHelpers.CreateMultiPagePdf(_tempDir, pageCount: 2);
        var bytes = File.ReadAllBytes(path);
        using var logger = new LoggingService();

        var overlays = new Dictionary<int, IReadOnlyList<PageTextOverlay>>
        {
            [99] = new[] { Overlay("x") }
        };

        var act = () => PdfOverlayExportService.BuildPdfWithPreservedContent(bytes, overlays, logger);
        act.Should().NotThrow();

        logger.GetRecentLogs(50).Should()
            .Contain(l => l.Contains("Skipping invalid page index 99"));
    }

    [Fact]
    public void NegativePageIndex_LogsWarningAndSkips()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Hi");
        var bytes = File.ReadAllBytes(path);
        using var logger = new LoggingService();

        var overlays = new Dictionary<int, IReadOnlyList<PageTextOverlay>>
        {
            [-1] = new[] { Overlay("x") }
        };

        var act = () => PdfOverlayExportService.BuildPdfWithPreservedContent(bytes, overlays, logger);
        act.Should().NotThrow();

        logger.GetRecentLogs(50).Should()
            .Contain(l => l.Contains("Skipping invalid page index -1"));
    }

    // ── Invalid bytes ───────────────────────────────────────────

    [Fact]
    public void InvalidPdfBytes_Throws()
    {
        var act = () => PdfOverlayExportService.BuildPdfWithPreservedContent(
            new byte[] { 1, 2, 3, 4 },
            new Dictionary<int, IReadOnlyList<PageTextOverlay>>());

        act.Should().Throw<Exception>();
    }

    // ── Logger integration ──────────────────────────────────────

    [Fact]
    public void WithLogger_EmitsStartAndCompleteEntries()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Logged");
        var bytes = File.ReadAllBytes(path);
        using var logger = new LoggingService();

        PdfOverlayExportService.BuildPdfWithPreservedContent(
            bytes,
            new Dictionary<int, IReadOnlyList<PageTextOverlay>> { [0] = new[] { Overlay("o") } },
            logger);

        var logs = logger.GetRecentLogs(50);
        logs.Should().Contain(l => l.Contains("Preserved-content export"));
        logs.Should().Contain(l => l.Contains("export complete"));
    }

    private static PageTextOverlay Overlay(string text) => new()
    {
        RelX = 0.1, RelY = 0.1, RelW = 0.3, RelH = 0.05,
        Text = text, RelFontSize = 0.02, FontFamily = "OpenSans"
    };
}
