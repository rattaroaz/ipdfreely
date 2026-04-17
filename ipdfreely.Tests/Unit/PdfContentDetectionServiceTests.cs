using System;
using System.IO;
using Xunit;
using FluentAssertions;
using ipdfreely.Services;
using ipdfreely.Tests;

namespace ipdfreely.Tests.Unit;

public class PdfContentDetectionServiceTests : IDisposable
{
    private readonly PdfContentDetectionService _service;
    private readonly string _tempDir;

    public PdfContentDetectionServiceTests()
    {
        _service = new PdfContentDetectionService();
        _tempDir = Path.Combine(Path.GetTempPath(), "ipdfreely_detect_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void AssertAllCollectionsEmpty(PdfContentDetectionResult r)
    {
        r.Should().NotBeNull();
        r.AcroFormFields.Should().BeEmpty();
        r.WidgetFields.Should().BeEmpty();
        r.VisualHeuristicFields.Should().BeEmpty();
        r.EmbeddedTextLines.Should().BeEmpty();
    }

    // ── Invalid input paths ─────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Analyze_NullOrWhitespacePath_ReturnsEmptyResult(string? path)
    {
        AssertAllCollectionsEmpty(_service.Analyze(path!));
    }

    [Fact]
    public void Analyze_NonExistentFile_ReturnsEmptyResult()
    {
        AssertAllCollectionsEmpty(_service.Analyze("/no/such/file.pdf"));
    }

    [Fact]
    public void Analyze_InvalidFileContent_ReturnsEmptyResult()
    {
        var path = Path.Combine(_tempDir, "notapdf.pdf");
        File.WriteAllText(path, "This is not a PDF");

        AssertAllCollectionsEmpty(_service.Analyze(path));
    }

    // ── Valid PDFs: text detection ───────────────────────────────

    [Fact]
    public void Analyze_PdfWithText_DetectsAllLines()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Hello World", "Second line");

        var result = _service.Analyze(path);

        result.EmbeddedTextLines.Should().NotBeEmpty();
        result.EmbeddedTextLines.Should().Contain(t => t.LineText.Contains("Hello World"));
        result.EmbeddedTextLines.Should().Contain(t => t.LineText.Contains("Second line"));
    }

    [Fact]
    public void Analyze_PdfWithText_AllLinesOnPage0()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Content");

        var result = _service.Analyze(path);

        result.EmbeddedTextLines.Should().NotBeEmpty()
            .And.OnlyContain(t => t.PageIndex == 0);
    }

    [Fact]
    public void Analyze_PdfWithText_BoundsHavePositiveWidthAndHeight()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Bounded text");

        var result = _service.Analyze(path);

        result.EmbeddedTextLines.Should().NotBeEmpty();
        foreach (var line in result.EmbeddedTextLines)
        {
            line.Bounds.Width.Should().BeGreaterThan(0);
            line.Bounds.Height.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Analyze_PdfWithText_LineTextIsNonEmpty()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Non-empty");

        var result = _service.Analyze(path);

        result.EmbeddedTextLines.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.LineText));
    }

    // ── Empty and multi-page ────────────────────────────────────

    [Fact]
    public void Analyze_EmptyPdf_ReturnsEmptyCollections()
    {
        var path = TestHelpers.CreateEmptyPdf(_tempDir);

        var result = _service.Analyze(path);

        result.Should().NotBeNull();
        result.AcroFormFields.Should().BeEmpty();
        result.EmbeddedTextLines.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_MultiPagePdf_DetectsTextOnBothPages()
    {
        var path = TestHelpers.CreateMultiPagePdf(_tempDir, pageCount: 2);

        var result = _service.Analyze(path);

        result.EmbeddedTextLines.Should().NotBeEmpty();
        result.EmbeddedTextLines.Should().Contain(t => t.PageIndex == 0);
        result.EmbeddedTextLines.Should().Contain(t => t.PageIndex == 1);
    }

    [Fact]
    public void Analyze_ThreePagePdf_DetectsAllThreePages()
    {
        var path = TestHelpers.CreateMultiPagePdf(_tempDir, pageCount: 3);

        var result = _service.Analyze(path);

        var pageIndices = result.EmbeddedTextLines.Select(t => t.PageIndex).Distinct().ToList();
        pageIndices.Should().Contain(new[] { 0, 1, 2 });
    }

    // ── Logger integration ──────────────────────────────────────

    [Fact]
    public void Analyze_WithLogger_LogsStartAndCompletion()
    {
        var logger = new LoggingService();
        var service = new PdfContentDetectionService(logger);
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Logged");

        service.Analyze(path);

        var logs = logger.GetRecentLogs(20);
        logs.Should().Contain(l => l.Contains("Content detection started"));
        logs.Should().Contain(l => l.Contains("opened PDF"));
    }

    [Fact]
    public void Analyze_WithLogger_InvalidPath_LogsWarning()
    {
        var logger = new LoggingService();
        var service = new PdfContentDetectionService(logger);

        service.Analyze("bogus.pdf");

        logger.GetRecentLogs(10).Should().Contain(l =>
            l.Contains("[WARN]") && l.Contains("invalid file path"));
    }

    [Fact]
    public void Analyze_WithoutLogger_StillWorksNormally()
    {
        var service = new PdfContentDetectionService(logger: null);
        var path = TestHelpers.CreatePdfWithText(_tempDir, "No logger");

        var act = () => service.Analyze(path);

        act.Should().NotThrow();
    }

    // ── Idempotency ─────────────────────────────────────────────

    [Fact]
    public void Analyze_SameFile_ReturnsConsistentResults()
    {
        var path = TestHelpers.CreatePdfWithText(_tempDir, "Consistent");

        var r1 = _service.Analyze(path);
        var r2 = _service.Analyze(path);

        r1.EmbeddedTextLines.Count.Should().Be(r2.EmbeddedTextLines.Count);
        r1.AcroFormFields.Count.Should().Be(r2.AcroFormFields.Count);
    }
}
