using FluentAssertions;
using ipdfreely.Services;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace ipdfreely.Tests.Unit;

/// <summary>
/// "Golden" style checks: re-open exported PDFs and assert stable structure (page geometry, page count),
/// without brittle full-byte comparisons across PdfSharp versions.
/// </summary>
public sealed class PdfOverlayExportSemanticGoldenTests
{
    [Fact]
    public void RasterExport_SinglePage_ReopenedMatchesOriginalPageDimensions()
    {
        const double wPts = 612;
        const double hPts = 792;
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(pageIndex: 0, widthPts: wPts, heightPts: hPts)
        };

        var bytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);

        using var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.ReadOnly);
        reopened.PageCount.Should().Be(1);
        reopened.Pages[0].Width.Point.Should().BeApproximately(wPts, 0.5);
        reopened.Pages[0].Height.Point.Should().BeApproximately(hPts, 0.5);
    }

    [Fact]
    public void RasterExport_WithOverlay_ReopenedStillSinglePage()
    {
        var pages = new[]
        {
            TestHelpers.CreateSinglePageDraw(overlays: new[]
            {
                new PageTextOverlay
                {
                    RelX = 0.1, RelY = 0.1, RelW = 0.4, RelH = 0.05,
                    Text = "Golden", RelFontSize = 0.02, FontFamily = "OpenSans"
                }
            })
        };

        var bytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(pages);

        using var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.ReadOnly);
        reopened.PageCount.Should().Be(1);
        reopened.Pages[0].Width.Point.Should().BeGreaterThan(100);
        reopened.Pages[0].Height.Point.Should().BeGreaterThan(100);
    }

    [Fact]
    public void PreservedContentExport_ReopenedPreservesPageCount()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ipdfreely_golden_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var path = TestHelpers.CreateMultiPagePdf(temp, pageCount: 3);
        try
        {
            var originalBytes = File.ReadAllBytes(path);
            var overlays = new Dictionary<int, IReadOnlyList<PageTextOverlay>>
            {
                [0] = new[]
                {
                    new PageTextOverlay
                    {
                        RelX = 0.1, RelY = 0.1, RelW = 0.3, RelH = 0.04,
                        Text = "A", RelFontSize = 0.015, FontFamily = "OpenSans"
                    }
                }
            };

            var exported = PdfOverlayExportService.BuildPdfWithPreservedContent(originalBytes, overlays);

            using var reopened = PdfReader.Open(new MemoryStream(exported), PdfDocumentOpenMode.ReadOnly);
            reopened.PageCount.Should().Be(3);
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                if (Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
