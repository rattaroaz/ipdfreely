using FluentAssertions;
using ipdfreely.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ipdfreely.Tests.Unit;

public class LoggingServiceForwardingLoggerProviderTests
{
    [Fact]
    public async Task ILogger_LogInformation_AppearsInLoggingServiceRecentLogs()
    {
        using var logging = new LoggingService();
        logging.MinimumLevel = ipdfreely.Services.LogLevel.Info;
        var provider = new LoggingServiceForwardingLoggerProvider(logging);
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        var logger = factory.CreateLogger("Test.Category");

        logger.LogInformation("hello from ILogger");

        await logging.FlushAsync();
        var recent = string.Join(Environment.NewLine, logging.GetRecentLogs(30));
        recent.Should().Contain("[Test.Category]").And.Contain("hello from ILogger");
    }

    [Fact]
    public async Task ILogger_LogError_IncludesExceptionInRecentLogs()
    {
        using var logging = new LoggingService();
        var provider = new LoggingServiceForwardingLoggerProvider(logging);
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        var logger = factory.CreateLogger("Err");

        logger.LogError(new InvalidOperationException("boom"), "failed");

        await logging.FlushAsync();
        var recent = string.Join(Environment.NewLine, logging.GetRecentLogs(30));
        recent.Should().Contain("[Err]").And.Contain("failed").And.Contain("InvalidOperationException");
    }
}
