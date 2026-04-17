using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using ipdfreely.Services;

namespace ipdfreely.Tests.Unit;

public class IntegrationTests
{
    // ── Cross-service logging ───────────────────────────────────

    [Fact]
    public void DetectionService_LogsThroughSharedLogger()
    {
        var logger = new LoggingService();
        var detection = new PdfContentDetectionService(logger);

        detection.Analyze("non_existent.pdf");

        var logs = logger.GetRecentLogs(10);
        logs.Should().Contain(l => l.Contains("[PDF]") && l.Contains("Content detection started"));
        logs.Should().Contain(l => l.Contains("[WARN]") && l.Contains("invalid file path"));
    }

    [Fact]
    public void ExportService_LogsThroughSharedLogger()
    {
        var logger = new LoggingService();

        logger.LogExportOperation("PDF", true, "out.pdf");
        logger.LogExportOperation("PDF", false);

        var logs = logger.GetRecentLogs(2);
        logs.Should().HaveCount(2);
        logs[0].Should().Contain("Success: True").And.Contain("out.pdf");
        logs[1].Should().Contain("Success: False");
    }

    [Fact]
    public void MultipleServices_ShareSingleLogStream()
    {
        var logger = new LoggingService();
        var detection = new PdfContentDetectionService(logger);

        logger.LogInfo("manual entry");
        detection.Analyze("missing.pdf");
        logger.LogExportOperation("PDF", false);

        var logs = logger.GetRecentLogs(20);
        logs.Should().Contain(l => l.Contains("manual entry"));
        logs.Should().Contain(l => l.Contains("Content detection started"));
        logs.Should().Contain(l => l.Contains("Format: PDF"));
    }

    // ── Performance logging format ──────────────────────────────

    [Theory]
    [InlineData(100, "100.00ms")]
    [InlineData(0.5, "0.50ms")]
    [InlineData(1234.56, "1234.56ms")]
    public void PerformanceLogging_FormatsDurationCorrectly(double ms, string expected)
    {
        var logger = new LoggingService();
        logger.LogPerformance("op", TimeSpan.FromMilliseconds(ms));

        logger.GetRecentLogs(1)[0].Should().Contain(expected);
    }

    // ── PDF operation log fields ────────────────────────────────

    [Fact]
    public void PdfOperationLogging_IncludesAllFields()
    {
        var logger = new LoggingService();

        logger.LogPdfOperation("Load PDF", 5, "test.pdf");
        logger.LogPdfOperation("Save PDF", 5);
        logger.LogPdfOperation("Delete Page", 3);

        var logs = logger.GetRecentLogs(3);
        logs[0].Should().Contain("Load PDF").And.Contain("test.pdf").And.Contain("Pages: 5");
        logs[1].Should().Contain("Save PDF").And.Contain("Pages: 5").And.NotContain("Path:");
        logs[2].Should().Contain("Delete Page").And.Contain("Pages: 3");
    }

    // ── User action logging ─────────────────────────────────────

    [Fact]
    public void UserActionLogging_TracksInteractions()
    {
        var logger = new LoggingService();

        logger.LogUserAction("Open PDF", "File", "test.pdf");
        logger.LogUserAction("Add text", "Page", 2);
        logger.LogUserAction("Save PDF");

        var logs = logger.GetRecentLogs(3);
        logs[0].Should().Contain("Open PDF").And.Contain("test.pdf");
        logs[1].Should().Contain("Add text").And.Contain("2");
        logs[2].Should().Contain("Save PDF").And.NotContain("Details:");
    }

    // ── Error logging with exceptions ───────────────────────────

    [Fact]
    public void ErrorLogging_IncludesExceptionTypeAndMessage()
    {
        var logger = new LoggingService();
        logger.LogError("with ex", new InvalidOperationException("bad state"));
        logger.LogError("without ex");

        var logs = logger.GetRecentLogs(2);
        logs[0].Should().Contain("InvalidOperationException").And.Contain("bad state");
        logs[1].Should().NotContain("Exception:");
    }

    // ── Concurrent multi-service writes ─────────────────────────

    [Fact]
    public async Task ConcurrentLogging_PreservesAllMessages()
    {
        var logger = new LoggingService();
        const int threadCount = 10;

        var tasks = Enumerable.Range(0, threadCount).Select(i =>
            Task.Run(() =>
            {
                logger.LogInfo($"c-info-{i}");
                logger.LogWarning($"c-warn-{i}");
                logger.LogError($"c-err-{i}");
            })).ToArray();

        await Task.WhenAll(tasks);

        var logs = logger.GetRecentLogs(100);
        // Banner + 3 messages per thread
        logs.Should().HaveCount(1 + threadCount * 3);

        for (int i = 0; i < threadCount; i++)
        {
            var idx = i;
            logs.Should().Contain(l => l.Contains($"c-info-{idx}"));
            logs.Should().Contain(l => l.Contains($"c-warn-{idx}"));
            logs.Should().Contain(l => l.Contains($"c-err-{idx}"));
        }
    }

    // ── Export operation details ─────────────────────────────────

    [Fact]
    public void ExportLogging_CapturesSuccessAndFailure()
    {
        var logger = new LoggingService();

        logger.LogExportOperation("PDF", true, "output.pdf");
        logger.LogExportOperation("PDF", false);
        logger.LogExportOperation("Image", true, "output.png");

        var logs = logger.GetRecentLogs(3);
        logs[0].Should().Contain("Success: True").And.Contain("output.pdf");
        logs[1].Should().Contain("Success: False").And.NotContain("Output:");
        logs[2].Should().Contain("Format: Image").And.Contain("output.png");
    }

    // ── MinimumLevel integration ────────────────────────────────

    [Fact]
    public void MinimumLevel_AffectsAllServicesUsingSharedLogger()
    {
        var logger = new LoggingService { MinimumLevel = LogLevel.Error };
        var detection = new PdfContentDetectionService(logger);

        detection.Analyze("missing.pdf");
        logger.LogInfo("should be filtered");

        var logs = logger.GetRecentLogs(20);
        // Only the startup banner (logged before MinimumLevel was set) should remain
        logs.Should().OnlyContain(l => l.Contains("logging started"));
    }

    [Fact]
    public void MinimumLevel_ErrorsStillPassThroughFromServices()
    {
        var logger = new LoggingService { MinimumLevel = LogLevel.Error };

        logger.LogInfo("filtered");
        logger.LogWarning("filtered");
        logger.LogError("survives");

        var logs = logger.GetRecentLogs(10);
        // Banner + 1 error entry
        logs.Should().HaveCount(2);
        logs.Should().Contain(l => l.Contains("survives"));
    }
}
