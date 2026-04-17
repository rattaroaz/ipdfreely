using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using ipdfreely.Services;

namespace ipdfreely.Tests.Unit;

public class LoggingServiceTests
{
    // Constructor logs a startup banner, so GetRecentLogs always starts with 1 entry.
    private const int BannerCount = 1;

    private static LoggingService CreateService(LogLevel level = LogLevel.Debug)
    {
        var svc = new LoggingService();
        svc.MinimumLevel = level;
        return svc;
    }

    // ── Startup banner ──────────────────────────────────────────

    [Fact]
    public void Constructor_LogsStartupBanner()
    {
        var svc = CreateService();
        var logs = svc.GetRecentLogs(5);
        logs.Should().Contain(l => l.Contains("=== ipdfreely logging started"));
    }

    [Fact]
    public void StartupBanner_ContainsPlatformAndPid()
    {
        var svc = CreateService();
        var banner = svc.GetRecentLogs(1)[0];
        banner.Should().Contain("PID:").And.Contain("OS:");
    }

    // ── LogFilePath accessor ────────────────────────────────────

    [Fact]
    public void LogFilePath_ContainsProcessId()
    {
        var svc = CreateService();
        svc.LogFilePath.Should().Contain(Environment.ProcessId.ToString());
    }

    [Fact]
    public void LogFilePath_EndsWithLogExtension()
    {
        var svc = CreateService();
        svc.LogFilePath.Should().EndWith(".log");
    }

    // ── Entry format ────────────────────────────────────────────

    [Fact]
    public void LogEntry_ContainsTimestamp_Level_ThreadId()
    {
        var svc = CreateService();
        svc.LogInfo("format check");

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().MatchRegex(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[INFO\] \[T:\d+\] format check$");
    }

    [Theory]
    [InlineData("DEBUG")]
    [InlineData("INFO")]
    [InlineData("WARN")]
    [InlineData("ERROR")]
    public void LogEntry_IncludesCorrectLevelTag(string level)
    {
        var svc = CreateService();
        switch (level)
        {
            case "DEBUG": svc.LogDebug("lvl"); break;
            case "INFO":  svc.LogInfo("lvl"); break;
            case "WARN":  svc.LogWarning("lvl"); break;
            case "ERROR": svc.LogError("lvl"); break;
        }

        svc.GetRecentLogs(1)[0].Should().Contain($"[{level}]");
    }

    // ── Exception formatting ────────────────────────────────────

    [Fact]
    public void LogError_WithException_IncludesTypeAndMessage()
    {
        var svc = CreateService();
        svc.LogError("boom", new InvalidOperationException("test fault"));

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("InvalidOperationException").And.Contain("test fault");
    }

    [Fact]
    public void LogError_WithoutException_OmitsExceptionBlock()
    {
        var svc = CreateService();
        svc.LogError("clean error");

        svc.GetRecentLogs(1)[0].Should().NotContain("Exception:");
    }

    [Fact]
    public void LogError_WithInnerException_IncludesChain()
    {
        var svc = CreateService();
        var inner = new ArgumentException("bad arg");
        var outer = new InvalidOperationException("wrapper", inner);
        svc.LogError("nested", outer);

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("InvalidOperationException").And.Contain("wrapper");
        entry.Should().Contain("Inner: ArgumentException").And.Contain("bad arg");
    }

    [Fact]
    public void LogError_WithDeepChain_CapsAt5InnerExceptions()
    {
        // Build chain: leaf <- level-0 <- level-1 <- ... <- level-7 (outermost)
        Exception ex = new Exception("leaf");
        for (int i = 0; i < 8; i++)
            ex = new Exception($"level-{i}", ex);

        var svc = CreateService();
        svc.LogError("deep", ex);

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("level-7"); // outer exception
        entry.Should().Contain("level-2"); // 5th inner (depth 5)
        // level-1 is at depth 6, beyond the cap of 5 inner exceptions
        entry.Should().NotContain("level-1");
        entry.Should().NotContain("leaf");
    }

    // ── Safe String.Format ──────────────────────────────────────

    [Fact]
    public void LogInfo_FormatsParametersCorrectly()
    {
        var svc = CreateService();
        svc.LogInfo("File {0} has {1} pages", "test.pdf", 5);

        svc.GetRecentLogs(1)[0].Should().Contain("File test.pdf has 5 pages");
    }

    [Fact]
    public void LogInfo_WithNullParameter_DoesNotThrow()
    {
        var svc = CreateService();
        var act = () => svc.LogInfo("val={0}", (object?)null);
        act.Should().NotThrow();
    }

    [Fact]
    public void LogInfo_MismatchedFormatArgs_FallsBackGracefully()
    {
        var svc = CreateService();
        // {0} and {1} but only 1 arg → FormatException internally
        svc.LogInfo("a={0} b={1}", "only-one");

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("FORMAT_ERROR").And.Contain("1 args");
    }

    // ── Domain-specific log methods ─────────────────────────────

    [Fact]
    public void LogUserAction_FormatsActionAndDetails()
    {
        var svc = CreateService();
        svc.LogUserAction("Open PDF", "file", "a.pdf");

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("[USER]").And.Contain("Action: Open PDF").And.Contain("file, a.pdf");
    }

    [Fact]
    public void LogUserAction_WithoutDetails_OmitsDetailsSection()
    {
        var svc = CreateService();
        svc.LogUserAction("Save");

        svc.GetRecentLogs(1)[0].Should().Contain("Action: Save").And.NotContain("Details:");
    }

    [Fact]
    public void LogPerformance_FormatsDurationInMilliseconds()
    {
        var svc = CreateService();
        svc.LogPerformance("Render", TimeSpan.FromMilliseconds(42.5));

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("[PERF]").And.Contain("42.50ms");
    }

    [Fact]
    public void LogPdfOperation_IncludesPageCountAndFileName()
    {
        var svc = CreateService();
        svc.LogPdfOperation("Load", 3, "/some/dir/file.pdf");

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("[PDF]").And.Contain("Pages: 3").And.Contain("file.pdf");
    }

    [Fact]
    public void LogPdfOperation_WithNulls_OmitsOptionalFields()
    {
        var svc = CreateService();
        svc.LogPdfOperation("Scan");

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("Operation: Scan").And.NotContain("Pages:").And.NotContain("Path:");
    }

    [Fact]
    public void LogExportOperation_IncludesFormatSuccessAndOutput()
    {
        var svc = CreateService();
        svc.LogExportOperation("PDF", true, "out.pdf");

        var entry = svc.GetRecentLogs(1)[0];
        entry.Should().Contain("[EXPORT]").And.Contain("Success: True").And.Contain("out.pdf");
    }

    // ── MinimumLevel filtering ──────────────────────────────────

    [Theory]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public void MinimumLevel_FiltersDebugWhenAboveDebug(LogLevel min)
    {
        var svc = CreateService(min);
        svc.LogDebug("should vanish");
        svc.GetRecentLogs(10).Should().NotContain(l => l.Contains("should vanish"));
    }

    [Theory]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public void MinimumLevel_FiltersInfoWhenAboveInfo(LogLevel min)
    {
        var svc = CreateService(min);
        svc.LogInfo("info gone");
        svc.GetRecentLogs(10).Should().NotContain(l => l.Contains("info gone"));
    }

    [Fact]
    public void MinimumLevel_FiltersWarningWhenError()
    {
        var svc = CreateService(LogLevel.Error);
        svc.LogWarning("warn gone");
        svc.GetRecentLogs(10).Should().NotContain(l => l.Contains("warn gone"));
    }

    [Theory]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public void MinimumLevel_ErrorAlwaysPasses(LogLevel min)
    {
        var svc = CreateService(min);
        svc.LogError("always visible");
        svc.GetRecentLogs(10).Should().Contain(l => l.Contains("always visible"));
    }

    [Fact]
    public void MinimumLevel_FiltersDomainMethodsToo()
    {
        var svc = CreateService(LogLevel.Error);
        svc.LogUserAction("gone");
        svc.LogPerformance("gone", TimeSpan.Zero);
        svc.LogPdfOperation("gone");
        svc.LogExportOperation("gone", false);
        // Only startup banner should remain (logged before MinimumLevel was set)
        var logs = svc.GetRecentLogs(10);
        logs.Should().OnlyContain(l => l.Contains("logging started"));
    }

    [Fact]
    public void MinimumLevel_ExportOperationFiltered()
    {
        var svc = CreateService(LogLevel.Error);
        svc.LogExportOperation("PDF", true, "out.pdf");
        svc.GetRecentLogs(10).Should().NotContain(l => l.Contains("out.pdf"));
    }

    // ── Ring buffer ─────────────────────────────────────────────

    [Fact]
    public void RingBuffer_CapsAt100Entries()
    {
        var svc = CreateService();
        // Banner is entry 0; add 120 more to overflow the 100-entry buffer
        for (int i = 0; i < 120; i++)
            svc.LogInfo($"msg {i}");

        var logs = svc.GetRecentLogs(200);
        logs.Should().HaveCount(100);
        // banner + msg 0..19 evicted (21 entries), so oldest kept is msg 20
        logs[0].Should().Contain("msg 20");
        logs[99].Should().Contain("msg 119");
    }

    [Fact]
    public void GetRecentLogs_ReturnsRequestedCountOrLess()
    {
        var svc = CreateService();
        svc.LogInfo("a");
        svc.LogInfo("b");

        svc.GetRecentLogs(1).Should().HaveCount(1).And.Contain(l => l.Contains("b"));
        // Banner + a + b = 3 entries
        svc.GetRecentLogs(10).Should().HaveCount(BannerCount + 2);
    }

    [Fact]
    public void GetRecentLogs_MaintainsInsertionOrder()
    {
        var svc = CreateService();
        svc.LogInfo("first");
        svc.LogWarning("second");
        svc.LogError("third");

        var logs = svc.GetRecentLogs(3);
        logs[0].Should().Contain("first");
        logs[1].Should().Contain("second");
        logs[2].Should().Contain("third");
    }

    // ── Flush ───────────────────────────────────────────────────

    [Fact]
    public async Task FlushAsync_CompletesWithoutError()
    {
        var svc = CreateService();
        svc.LogInfo("pre-flush");
        await svc.FlushAsync();
    }

    // ── Dispose ─────────────────────────────────────────────────

    [Fact]
    public void Dispose_LogsShutdownMessage()
    {
        var svc = CreateService();
        svc.Dispose();

        var logs = svc.GetRecentLogs(10);
        logs.Should().Contain(l => l.Contains("logging shutting down"));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var svc = CreateService();
        var act = () => { svc.Dispose(); svc.Dispose(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void AfterDispose_LogCallsAreIgnored()
    {
        var svc = CreateService();
        svc.Dispose();

        var countBefore = svc.GetRecentLogs(100).Length;
        svc.LogInfo("after dispose");
        svc.GetRecentLogs(100).Length.Should().Be(countBefore);
    }

    // ── Concurrency ─────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentWrites_AllMessagesRecorded()
    {
        var svc = CreateService();
        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(() =>
            {
                svc.LogInfo($"thread-{i}-a");
                svc.LogWarning($"thread-{i}-b");
            })).ToArray();

        await Task.WhenAll(tasks);

        var logs = svc.GetRecentLogs(100);
        // Banner + 20 thread messages
        logs.Should().HaveCount(BannerCount + 20);
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            logs.Should().Contain(l => l.Contains($"thread-{idx}-a"));
            logs.Should().Contain(l => l.Contains($"thread-{idx}-b"));
        }
    }
}
