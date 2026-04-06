using System;
using System.IO;
using System.Threading.Tasks;
using ipdfreely.Services;

namespace ipdfreely.Tests;

public class IntegrationTests
{
    private readonly LoggingService _logger;
    private readonly PdfContentDetectionService _detectionService;
    private readonly PdfExportService _exportService;

    public IntegrationTests()
    {
        _logger = new LoggingService();
        _detectionService = new PdfContentDetectionService(_logger);
        _exportService = new PdfExportService(_logger);
    }

    public async Task LoggingIntegration_ShouldLogMultipleServices()
    {
        // Arrange
        var testMessage = "Integration test message";
        
        // Act
        _logger.LogInfo(testMessage);
        _detectionService.Analyze("non_existent.pdf");
        await _exportService.SavePdfAsync(Array.Empty<byte>());
        
        // Assert
        var logs = _logger.GetRecentLogs(10);
        bool hasDetectionLog = false;
        bool hasExportLog = false;
        bool hasInfoLog = false;
        
        foreach (var log in logs)
        {
            if (log.Contains("Content detection started")) hasDetectionLog = true;
            if (log.Contains("Export started")) hasExportLog = true;
            if (log.Contains(testMessage)) hasInfoLog = true;
        }
        
        if (!hasDetectionLog)
            throw new Exception("Detection service logging not found");
        
        if (!hasExportLog)
            throw new Exception("Export service logging not found");
        
        if (!hasInfoLog)
            throw new Exception("Info logging not found");
    }

    public async Task PerformanceLogging_ShouldTrackOperationTimes()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Act
        _logger.LogPerformance("Test operation 1", TimeSpan.FromMilliseconds(100));
        _logger.LogPerformance("Test operation 2", TimeSpan.FromMilliseconds(200));
        _logger.LogPerformance("Test operation 3", TimeSpan.FromMilliseconds(50));
        
        // Assert
        var logs = _logger.GetRecentLogs(3);
        if (logs.Length != 3)
            throw new Exception("Expected 3 performance logs");
        
        bool hasOp1 = false, hasOp2 = false, hasOp3 = false;
        foreach (var log in logs)
        {
            if (log.Contains("Test operation 1") && log.Contains("100.00ms")) hasOp1 = true;
            if (log.Contains("Test operation 2") && log.Contains("200.00ms")) hasOp2 = true;
            if (log.Contains("Test operation 3") && log.Contains("50.00ms")) hasOp3 = true;
        }
        
        if (!hasOp1 || !hasOp2 || !hasOp3)
            throw new Exception("Performance logs missing expected operations");
    }

    public void PdfOperationLogging_ShouldTrackPdfOperations()
    {
        // Act
        _logger.LogPdfOperation("Load PDF", 5, "test.pdf");
        _logger.LogPdfOperation("Save PDF", 5);
        _logger.LogPdfOperation("Delete Page", 3);
        
        // Assert
        var logs = _logger.GetRecentLogs(3);
        if (logs.Length != 3)
            throw new Exception("Expected 3 PDF operation logs");
        
        bool hasLoad = false, hasSave = false, hasDelete = false;
        foreach (var log in logs)
        {
            if (log.Contains("Load PDF") && log.Contains("test.pdf")) hasLoad = true;
            if (log.Contains("Save PDF") && log.Contains("Pages: 5")) hasSave = true;
            if (log.Contains("Delete Page") && log.Contains("Pages: 3")) hasDelete = true;
        }
        
        if (!hasLoad || !hasSave || !hasDelete)
            throw new Exception("PDF operation logs missing expected operations");
    }

    public void UserActionLogging_ShouldTrackUserInteractions()
    {
        // Act
        _logger.LogUserAction("Open PDF", "File", "test.pdf");
        _logger.LogUserAction("Add text", "Page", 2);
        _logger.LogUserAction("Delete page", "Index", 1);
        _logger.LogUserAction("Save PDF");
        
        // Assert
        var logs = _logger.GetRecentLogs(4);
        if (logs.Length != 4)
            throw new Exception("Expected 4 user action logs");
        
        bool hasOpen = false, hasAddText = false, hasDelete = false, hasSave = false;
        foreach (var log in logs)
        {
            if (log.Contains("Open PDF") && log.Contains("test.pdf")) hasOpen = true;
            if (log.Contains("Add text") && log.Contains("Page: 2")) hasAddText = true;
            if (log.Contains("Delete page") && log.Contains("Index: 1")) hasDelete = true;
            if (log.Contains("Save PDF")) hasSave = true;
        }
        
        if (!hasOpen || !hasAddText || !hasDelete || !hasSave)
            throw new Exception("User action logs missing expected operations");
    }

    public void ErrorLogging_ShouldHandleExceptions()
    {
        // Arrange
        var testException = new InvalidOperationException("Test exception message");
        
        // Act
        _logger.LogError("Test error with exception", testException);
        _logger.LogError("Test error without exception");
        
        // Assert
        var logs = _logger.GetRecentLogs(2);
        if (logs.Length != 2)
            throw new Exception("Expected 2 error logs");
        
        bool hasException = false, hasNoException = false;
        foreach (var log in logs)
        {
            if (log.Contains("Test error with exception") && log.Contains("InvalidOperationException")) 
                hasException = true;
            if (log.Contains("Test error without exception") && !log.Contains("InvalidOperationException")) 
                hasNoException = true;
        }
        
        if (!hasException || !hasNoException)
            throw new Exception("Error logs missing expected exception information");
    }

    public async Task ConcurrentLogging_ShouldHandleMultipleThreads()
    {
        // Arrange
        var tasks = new Task[10];
        
        // Act
        for (int i = 0; i < tasks.Length; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                _logger.LogInfo($"Concurrent message {index}");
                _logger.LogWarning($"Concurrent warning {index}");
                _logger.LogError($"Concurrent error {index}");
            });
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var logs = _logger.GetRecentLogs(30); // 10 threads * 3 messages each
        if (logs.Length < 30)
            throw new Exception($"Expected at least 30 logs, got {logs.Length}");
        
        // Verify we have messages from all threads
        var messageCount = 0;
        for (int i = 0; i < 10; i++)
        {
            foreach (var log in logs)
            {
                if (log.Contains($"Concurrent message {i}"))
                {
                    messageCount++;
                    break;
                }
            }
        }
        
        if (messageCount != 10)
            throw new Exception($"Expected messages from all 10 threads, got {messageCount}");
    }

    public void ExportOperationLogging_ShouldTrackExportDetails()
    {
        // Act
        _logger.LogExportOperation("PDF", true, "output.pdf");
        _logger.LogExportOperation("PDF", false);
        _logger.LogExportOperation("Image", true, "output.png");
        
        // Assert
        var logs = _logger.GetRecentLogs(3);
        if (logs.Length != 3)
            throw new Exception("Expected 3 export operation logs");
        
        bool hasPdfSuccess = false, hasPdfFailure = false, hasImageSuccess = false;
        foreach (var log in logs)
        {
            if (log.Contains("Format: PDF") && log.Contains("Success: True") && log.Contains("output.pdf")) 
                hasPdfSuccess = true;
            if (log.Contains("Format: PDF") && log.Contains("Success: False")) 
                hasPdfFailure = true;
            if (log.Contains("Format: Image") && log.Contains("Success: True")) 
                hasImageSuccess = true;
        }
        
        if (!hasPdfSuccess || !hasPdfFailure || !hasImageSuccess)
            throw new Exception("Export operation logs missing expected details");
    }

    public async Task RunAllIntegrationTests()
    {
        Console.WriteLine("Running Integration Tests...");
        
        try
        {
            await LoggingIntegration_ShouldLogMultipleServices();
            Console.WriteLine("✓ Logging integration test passed");
            
            await PerformanceLogging_ShouldTrackOperationTimes();
            Console.WriteLine("✓ Performance logging test passed");
            
            PdfOperationLogging_ShouldTrackPdfOperations();
            Console.WriteLine("✓ PDF operation logging test passed");
            
            UserActionLogging_ShouldTrackUserInteractions();
            Console.WriteLine("✓ User action logging test passed");
            
            ErrorLogging_ShouldHandleExceptions();
            Console.WriteLine("✓ Error logging test passed");
            
            await ConcurrentLogging_ShouldHandleMultipleThreads();
            Console.WriteLine("✓ Concurrent logging test passed");
            
            ExportOperationLogging_ShouldTrackExportDetails();
            Console.WriteLine("✓ Export operation logging test passed");
            
            Console.WriteLine("All Integration tests passed! ✓");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Test failed: {ex.Message}");
            throw;
        }
    }
}
