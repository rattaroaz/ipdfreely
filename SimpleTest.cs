using System;
using System.Threading.Tasks;
using ipdfreely.Services;

namespace ipdfreely.Tests;

public class SimpleTest
{
    public static async Task RunBasicTests()
    {
        Console.WriteLine("Running Basic Tests...");
        Console.WriteLine("=======================");

        try
        {
            // Test 1: Logging Service Basic Functionality
            Console.WriteLine("Test 1: Logging Service");
            var logger = new LoggingService();
            
            logger.LogInfo("Test info message");
            logger.LogWarning("Test warning message");
            logger.LogError("Test error message");
            
            var logs = logger.GetRecentLogs(3);
            if (logs.Length != 3)
            {
                throw new Exception($"Expected 3 logs, got {logs.Length}");
            }
            
            Console.WriteLine("✓ Logging service basic functionality works");
            
            // Test 2: PDF Detection Service
            Console.WriteLine("Test 2: PDF Detection Service");
            var detectionService = new PdfContentDetectionService(logger);
            
            var result = detectionService.Analyze("non_existent.pdf");
            if (result == null)
            {
                throw new Exception("PDF detection service returned null");
            }
            
            Console.WriteLine("✓ PDF detection service works");
            
            // Test 3: PDF Export Service
            Console.WriteLine("Test 3: PDF Export Service");
            var exportService = new PdfExportService(logger);
            
            var exportResult = await exportService.SavePdfAsync(Array.Empty<byte>());
            if (exportResult) // Should return false for empty bytes
            {
                throw new Exception("Export service should return false for empty bytes");
            }
            
            Console.WriteLine("✓ PDF export service works");
            
            // Test 4: Performance Logging
            Console.WriteLine("Test 4: Performance Logging");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            logger.LogPerformance("Test operation", TimeSpan.FromMilliseconds(123.45));
            
            stopwatch.Stop();
            var perfLogs = logger.GetRecentLogs(1);
            if (!perfLogs[0].Contains("123.45ms"))
            {
                throw new Exception("Performance logging failed");
            }
            
            Console.WriteLine("✓ Performance logging works");
            
            // Test 5: User Action Logging
            Console.WriteLine("Test 5: User Action Logging");
            logger.LogUserAction("Test action", "param1", "param2");
            
            var actionLogs = logger.GetRecentLogs(1);
            if (!actionLogs[0].Contains("Test action") || !actionLogs[0].Contains("param1"))
            {
                throw new Exception("User action logging failed");
            }
            
            Console.WriteLine("✓ User action logging works");
            
            Console.WriteLine();
            Console.WriteLine("🎉 All basic tests passed! 🎉");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            throw;
        }
    }
    
    public static void RunQuickPerformanceTest()
    {
        Console.WriteLine("Quick Performance Test");
        Console.WriteLine("======================");
        
        var logger = new LoggingService();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Log 1000 messages
        for (int i = 0; i < 1000; i++)
        {
            logger.LogInfo($"Performance test message {i}");
        }
        
        stopwatch.Stop();
        
        Console.WriteLine($"✓ 1000 messages logged in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"✓ {(1000.0 / stopwatch.Elapsed.TotalSeconds):F0} messages/second");
        
        // Test retrieval
        stopwatch.Restart();
        var logs = logger.GetRecentLogs(100);
        stopwatch.Stop();
        
        Console.WriteLine($"✓ Retrieved 100 logs in {stopwatch.ElapsedMilliseconds}ms");
    }
}
