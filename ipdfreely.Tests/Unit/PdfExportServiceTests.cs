using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using ipdfreely.Services;

namespace ipdfreely.Tests.Unit;

public class PdfExportServiceTests
{
    // ── Constructor ──────────────────────────────────────────────

    [Fact]
    public void Constructor_WithLogger_DoesNotThrow()
    {
        var logger = new LoggingService();
        var act = () => new PdfExportService(logger);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithoutLogger_DoesNotThrow()
    {
        var act = () => new PdfExportService();
        act.Should().NotThrow();
    }

    // ── Empty bytes guard ───────────────────────────────────────

    [Fact]
    public async Task SavePdfAsync_EmptyBytes_ReturnsFalse()
    {
        var service = new PdfExportService();
        var result = await service.SavePdfAsync(Array.Empty<byte>());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SavePdfAsync_EmptyBytes_WithLogger_LogsWarning()
    {
        var logger = new LoggingService();
        var service = new PdfExportService(logger);

        await service.SavePdfAsync(Array.Empty<byte>());

        logger.GetRecentLogs(10).Should().Contain(l =>
            l.Contains("[WARN]") && l.Contains("empty PDF bytes"));
    }

    [Fact]
    public async Task SavePdfAsync_EmptyBytes_WithCustomFileName_StillReturnsFalse()
    {
        var service = new PdfExportService();
        var result = await service.SavePdfAsync(Array.Empty<byte>(), "custom.pdf");
        result.Should().BeFalse();
    }
}
