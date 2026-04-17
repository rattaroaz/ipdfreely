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

    [Fact]
    public async Task SavePdfAsync_EmptyBytes_EmptyFileName_StillReturnsFalse()
    {
        var service = new PdfExportService();
        var result = await service.SavePdfAsync(Array.Empty<byte>(), string.Empty);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SavePdfAsync_EmptyBytes_PreCancelledToken_ReturnsFalseWithoutThrowing()
    {
        // The empty-bytes guard runs before cancellation can affect anything;
        // contract: always return false, never leak an exception.
        var service = new PdfExportService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task<bool>> act = () => service.SavePdfAsync(Array.Empty<byte>(), "x.pdf", cts.Token);
        await act.Should().NotThrowAsync();
        (await service.SavePdfAsync(Array.Empty<byte>(), "x.pdf", cts.Token)).Should().BeFalse();
    }

    // ── Observability ───────────────────────────────────────────

    [Fact]
    public async Task SavePdfAsync_EmptyBytes_LogsSaveStartedBeforeWarning()
    {
        using var logger = new LoggingService();
        var service = new PdfExportService(logger);

        await service.SavePdfAsync(Array.Empty<byte>(), "x.pdf");

        // Ordering contract: the export operation announces itself, then the
        // empty-bytes guard logs the warning. This documents the current shape
        // of the guard path (early-return, no perf entry).
        var logs = logger.GetRecentLogs(20);
        var startedIdx = Array.FindIndex(logs, l => l.Contains("[EXPORT]") && l.Contains("Save started"));
        var warnIdx = Array.FindIndex(logs, l => l.Contains("[WARN]") && l.Contains("empty PDF bytes"));
        startedIdx.Should().BeGreaterOrEqualTo(0);
        warnIdx.Should().BeGreaterThan(startedIdx);
    }

    [Fact]
    public async Task SavePdfAsync_EmptyBytes_IncludesSuggestedFileNameInStartedEntry()
    {
        using var logger = new LoggingService();
        var service = new PdfExportService(logger);

        await service.SavePdfAsync(Array.Empty<byte>(), "my-export.pdf");

        logger.GetRecentLogs(20).Should().Contain(l =>
            l.Contains("[EXPORT]") && l.Contains("Save started") && l.Contains("my-export.pdf"));
    }
}
