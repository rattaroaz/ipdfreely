using FluentAssertions;
using ipdfreely.Services;
using Microsoft.Extensions.Logging;
using Xunit;
using AppLogLevel = ipdfreely.Services.LogLevel;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace ipdfreely.Tests.Unit;

public class LoggingServiceForwardingLoggerProviderTests
{
    private static (LoggingService svc, LoggingServiceForwardingLoggerProvider provider, ILogger logger)
        Create(string category = "cat", AppLogLevel min = AppLogLevel.Debug)
    {
        var svc = new LoggingService { MinimumLevel = min };
        var provider = new LoggingServiceForwardingLoggerProvider(svc);
        // Override the default Information filter so Debug/Trace reach the provider.
        var factory = LoggerFactory.Create(b => b
            .SetMinimumLevel(MsLogLevel.Trace)
            .AddProvider(provider));
        return (svc, provider, factory.CreateLogger(category));
    }

    // ── Construction ────────────────────────────────────────────

    [Fact]
    public void Constructor_NullLoggingService_Throws()
    {
        var act = () => new LoggingServiceForwardingLoggerProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateLogger_ReturnsNonNullLogger()
    {
        using var logging = new LoggingService();
        using var provider = new LoggingServiceForwardingLoggerProvider(logging);
        provider.CreateLogger("cat").Should().NotBeNull();
    }

    [Fact]
    public void Dispose_IsIdempotentAndDoesNotThrow()
    {
        using var logging = new LoggingService();
        var provider = new LoggingServiceForwardingLoggerProvider(logging);

        var act = () => { provider.Dispose(); provider.Dispose(); };
        act.Should().NotThrow();
    }

    // ── IsEnabled behavior ──────────────────────────────────────

    [Fact]
    public void IsEnabled_None_IsAlwaysFalse()
    {
        using var logging = new LoggingService { MinimumLevel = AppLogLevel.Debug };
        using var provider = new LoggingServiceForwardingLoggerProvider(logging);
        var logger = provider.CreateLogger("x");

        logger.IsEnabled(MsLogLevel.None).Should().BeFalse();
    }

    [Theory]
    [InlineData(AppLogLevel.Debug, MsLogLevel.Trace, true)]
    [InlineData(AppLogLevel.Debug, MsLogLevel.Debug, true)]
    [InlineData(AppLogLevel.Info, MsLogLevel.Trace, false)]
    [InlineData(AppLogLevel.Info, MsLogLevel.Debug, false)]
    [InlineData(AppLogLevel.Info, MsLogLevel.Information, true)]
    [InlineData(AppLogLevel.Warning, MsLogLevel.Information, false)]
    [InlineData(AppLogLevel.Warning, MsLogLevel.Warning, true)]
    [InlineData(AppLogLevel.Error, MsLogLevel.Warning, false)]
    [InlineData(AppLogLevel.Error, MsLogLevel.Error, true)]
    [InlineData(AppLogLevel.Error, MsLogLevel.Critical, true)]
    public void IsEnabled_ReflectsLoggingServiceMinimumLevel(
        AppLogLevel minimum, MsLogLevel probe, bool expected)
    {
        using var logging = new LoggingService { MinimumLevel = minimum };
        using var provider = new LoggingServiceForwardingLoggerProvider(logging);
        var logger = provider.CreateLogger("cat");

        logger.IsEnabled(probe).Should().Be(expected);
    }

    // ── Forwarding: level mapping ───────────────────────────────

    [Fact]
    public async Task LogInformation_AppearsInRecentLogsWithCategoryPrefix()
    {
        var (svc, provider, logger) = Create("Test.Category", AppLogLevel.Info);
        using (svc) using (provider)
        {
            logger.LogInformation("hello from ILogger");
            await svc.FlushAsync();

            var recent = string.Join("\n", svc.GetRecentLogs(30));
            recent.Should().Contain("[Test.Category]").And.Contain("hello from ILogger");
        }
    }

    [Fact]
    public async Task LogError_WithException_IncludesTypeAndMessage()
    {
        var (svc, provider, logger) = Create("Err");
        using (svc) using (provider)
        {
            logger.LogError(new InvalidOperationException("boom"), "failed");
            await svc.FlushAsync();

            var recent = string.Join("\n", svc.GetRecentLogs(30));
            recent.Should().Contain("[Err]").And.Contain("failed")
                .And.Contain("InvalidOperationException").And.Contain("boom");
        }
    }

    [Fact]
    public async Task LogDebug_ForwardsAtDebugLevelTag()
    {
        var (svc, provider, logger) = Create("d", AppLogLevel.Debug);
        using (svc) using (provider)
        {
            logger.LogDebug("dbg msg");
            await svc.FlushAsync();

            string.Join("\n", svc.GetRecentLogs(30))
                .Should().Contain("[DEBUG]").And.Contain("dbg msg");
        }
    }

    [Fact]
    public async Task LogTrace_IsForwardedAsDebug()
    {
        var (svc, provider, logger) = Create("t", AppLogLevel.Debug);
        using (svc) using (provider)
        {
            logger.LogTrace("trc msg");
            await svc.FlushAsync();

            string.Join("\n", svc.GetRecentLogs(30))
                .Should().Contain("[DEBUG]").And.Contain("trc msg");
        }
    }

    [Fact]
    public async Task LogWarning_IsForwardedAtWarnTag()
    {
        var (svc, provider, logger) = Create("w");
        using (svc) using (provider)
        {
            logger.LogWarning("warn msg");
            await svc.FlushAsync();

            string.Join("\n", svc.GetRecentLogs(30))
                .Should().Contain("[WARN]").And.Contain("warn msg");
        }
    }

    [Fact]
    public async Task LogCritical_IsForwardedAtErrorTag()
    {
        var (svc, provider, logger) = Create("c");
        using (svc) using (provider)
        {
            logger.LogCritical("crit msg");
            await svc.FlushAsync();

            string.Join("\n", svc.GetRecentLogs(30))
                .Should().Contain("[ERROR]").And.Contain("crit msg");
        }
    }

    // ── Message payload shaping ─────────────────────────────────

    [Fact]
    public async Task Log_WithEventId_IncludesEventIdSuffix()
    {
        var (svc, provider, logger) = Create("e");
        using (svc) using (provider)
        {
            logger.Log(MsLogLevel.Information, new EventId(42, "Evt"), "payload",
                exception: null, formatter: (s, _) => s.ToString()!);
            await svc.FlushAsync();

            string.Join("\n", svc.GetRecentLogs(30))
                .Should().Contain("EventId=42").And.Contain("payload");
        }
    }

    [Fact]
    public async Task Log_WithEventIdZero_DoesNotAppendEventId()
    {
        var (svc, provider, logger) = Create("e");
        using (svc) using (provider)
        {
            logger.LogInformation("plain");
            await svc.FlushAsync();

            string.Join("\n", svc.GetRecentLogs(30))
                .Should().Contain("plain").And.NotContain("EventId=");
        }
    }

    [Fact]
    public async Task Log_EmptyCategory_OmitsCategoryBracket()
    {
        var (svc, provider, logger) = Create(category: "");
        using (svc) using (provider)
        {
            logger.LogInformation("bare");
            await svc.FlushAsync();

            var recent = string.Join("\n", svc.GetRecentLogs(30));
            recent.Should().Contain("bare");
            // Only the file path's "Logs" bracket and level brackets exist;
            // no empty category bracket should be emitted.
            recent.Should().NotContain("[] bare");
        }
    }

    [Fact]
    public async Task Log_EmptyMessageAndNoException_IsSkipped()
    {
        var (svc, provider, logger) = Create("s");
        using (svc) using (provider)
        {
            var countBefore = svc.GetRecentLogs(200).Length;
            logger.Log(MsLogLevel.Information, new EventId(0), string.Empty,
                exception: null, formatter: (s, _) => s);
            await svc.FlushAsync();

            svc.GetRecentLogs(200).Length.Should().Be(countBefore);
        }
    }

    [Fact]
    public async Task Log_BelowMinimumLevel_DropsMessage()
    {
        var (svc, provider, logger) = Create("lvl", AppLogLevel.Warning);
        using (svc) using (provider)
        {
            logger.LogInformation("should vanish");
            await svc.FlushAsync();

            string.Join("\n", svc.GetRecentLogs(50))
                .Should().NotContain("should vanish");
        }
    }

    [Fact]
    public async Task Log_WhenStateIsKeyValueList_AppendsStructuredProperties()
    {
        var (svc, provider, logger) = Create("struct");
        using (svc) using (provider)
        {
            var state = new List<KeyValuePair<string, object?>>
            {
                new("N", 7),
                new("OriginalFormat", "template {N}")
            };
            logger.Log(MsLogLevel.Information, new EventId(0), state, null,
                (s, ex) => "result");
            await svc.FlushAsync();

            var recent = string.Join("\n", svc.GetRecentLogs(20));
            recent.Should().Contain("N=7").And.Contain("result");
            recent.Should().NotContain("OriginalFormat");
        }
    }

    // ── Scope ───────────────────────────────────────────────────

    [Fact]
    public void BeginScope_ReturnsDisposable()
    {
        var (svc, provider, logger) = Create("s");
        using (svc) using (provider)
        {
            using var scope = logger.BeginScope("state");
            scope.Should().NotBeNull();
        }
    }

    [Fact]
    public void BeginScope_Dispose_IsIdempotent()
    {
        var (svc, provider, logger) = Create("s");
        using (svc) using (provider)
        {
            var scope = logger.BeginScope("state");
            var act = () => { scope!.Dispose(); scope.Dispose(); };
            act.Should().NotThrow();
        }
    }
}
