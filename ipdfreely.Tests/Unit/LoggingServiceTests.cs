using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using ipdfreely.Services;

namespace ipdfreely.Tests.Unit;

public class LoggingServiceTests
{
    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Arrange & Act
        var service = new LoggingService();
        
        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void LogInfo_ShouldNotThrow()
    {
        // Arrange
        var service = new LoggingService();
        
        // Act
        var act1 = () => service.LogInfo("Test info message");
        var act2 = () => service.LogInfo("Test info with parameter: {0}", "parameter");
        
        // Assert
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void LogError_ShouldHandleException()
    {
        // Arrange
        var service = new LoggingService();
        var exception = new InvalidOperationException("Test exception");
        
        // Act
        var act1 = () => service.LogError("Test error message", exception);
        var act2 = () => service.LogError("Test error without exception");
        
        // Assert
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void LogUserAction_ShouldLogActionWithDetails()
    {
        // Arrange
        var service = new LoggingService();
        
        // Act
        var act1 = () => service.LogUserAction("Test action");
        var act2 = () => service.LogUserAction("Test action with details", "detail1", "detail2");
        
        // Assert
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void LogPerformance_ShouldLogOperationWithDuration()
    {
        // Arrange
        var service = new LoggingService();
        var duration = TimeSpan.FromMilliseconds(123.45);
        
        // Act
        var act1 = () => service.LogPerformance("Test operation", duration);
        var act2 = () => service.LogPerformance("Test operation with details", duration, "detail1", "detail2");
        
        // Assert
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void LogPdfOperation_ShouldLogPdfOperations()
    {
        // Arrange
        var service = new LoggingService();
        
        // Act
        var act1 = () => service.LogPdfOperation("Load PDF");
        var act2 = () => service.LogPdfOperation("Load PDF", 5);
        var act3 = () => service.LogPdfOperation("Load PDF", 5, "test.pdf");
        
        // Assert
        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
    }

    [Fact]
    public void LogExportOperation_ShouldLogExportOperations()
    {
        // Arrange
        var service = new LoggingService();
        
        // Act
        var act1 = () => service.LogExportOperation("PDF", true);
        var act2 = () => service.LogExportOperation("PDF", false, "output.pdf");
        
        // Assert
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public async Task FlushAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var service = new LoggingService();
        service.LogInfo("Test message before flush");
        
        // Act
        var act = async () => await service.FlushAsync();
        
        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void GetRecentLogs_ShouldReturnRecentLogEntries()
    {
        // Arrange
        var service = new LoggingService();
        service.LogInfo("Test message 1");
        service.LogInfo("Test message 2");
        service.LogInfo("Test message 3");
        
        // Act
        var recentLogs = service.GetRecentLogs(2);
        
        // Assert
        recentLogs.Should().NotBeNull();
        recentLogs.Should().HaveCount(2);
        recentLogs.Should().Contain(log => log.Contains("Test message 2"));
        recentLogs.Should().Contain(log => log.Contains("Test message 3"));
    }

    [Fact]
    public void GetRecentLogs_WithDefaultCount_ShouldReturnDefaultNumberOfLogs()
    {
        // Arrange
        var service = new LoggingService();
        for (int i = 0; i < 60; i++)
        {
            service.LogInfo($"Test message {i}");
        }
        
        // Act
        var recentLogs = service.GetRecentLogs();
        
        // Assert
        recentLogs.Should().NotBeNull();
        recentLogs.Should().HaveCount(50); // Default count
    }

    [Fact]
    public void MultipleLogCalls_ShouldMaintainOrder()
    {
        // Arrange
        var service = new LoggingService();
        
        // Act
        service.LogInfo("First message");
        service.LogWarning("Second message");
        service.LogError("Third message");
        
        // Assert
        var logs = service.GetRecentLogs(3);
        logs.Should().HaveCount(3);
        logs[0].Should().Contain("First message");
        logs[1].Should().Contain("Second message");
        logs[2].Should().Contain("Third message");
    }

    [Fact]
    public void LogWithNullParameters_ShouldHandleGracefully()
    {
        // Arrange
        var service = new LoggingService();
        
        // Act
        var act1 = () => service.LogInfo("Test with null parameter: {0}", (object?)null);
        var act2 = () => service.LogError("Test error", null);
        
        // Assert
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }
}
