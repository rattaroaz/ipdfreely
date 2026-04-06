using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace LoggingTest;

public interface ILoggingService
{
    void LogInfo(string message, params object[] args);
    void LogError(string message, Exception? exception = null, params object[] args);
    void LogUserAction(string action, params object[] details);
    void LogPerformance(string operation, TimeSpan duration, params object[] details);
    string[] GetRecentLogs(int count = 50);
}

public class SimpleLoggingService : ILoggingService
{
    private readonly ConcurrentQueue<string> _recentLogs = new();
    private const int MaxRecentLogs = 100;

    public void LogInfo(string message, params object[] args)
    {
        var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
        var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [INFO] {formattedMessage}";
        AddLog(logEntry);
    }

    public void LogWarning(string message, params object[] args)
    {
        var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
        var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [WARN] {formattedMessage}";
        AddLog(logEntry);
    }

    public void LogError(string message, Exception? exception = null, params object[] args)
    {
        var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
        var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] {formattedMessage}";
        if (exception != null)
        {
            logEntry += $" | Exception: {exception.GetType().Name}: {exception.Message}";
        }
        AddLog(logEntry);
    }

    public void LogUserAction(string action, params object[] details)
    {
        var detailsStr = details.Length > 0 ? $" | Details: {string.Join(", ", details)}" : "";
        var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [USER] Action: {action}{detailsStr}";
        AddLog(logEntry);
    }

    public void LogPerformance(string operation, TimeSpan duration, params object[] details)
    {
        var detailsStr = details.Length > 0 ? $" | Details: {string.Join(", ", details)}" : "";
        var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [PERF] Operation: {operation} | Duration: {duration.TotalMilliseconds:F2}ms{detailsStr}";
        AddLog(logEntry);
    }

    public string[] GetRecentLogs(int count = 50)
    {
        return _recentLogs.TakeLast(count).ToArray();
    }

    private void AddLog(string logEntry)
    {
        _recentLogs.Enqueue(logEntry);
        while (_recentLogs.Count > MaxRecentLogs)
        {
            _recentLogs.TryDequeue(out _);
        }
        Console.WriteLine(logEntry);
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("    Logging Service Test");
        Console.WriteLine("========================================");
        Console.WriteLine();

        var logger = new SimpleLoggingService();

        try
        {
            // Test 1: Basic logging
            Console.WriteLine("Test 1: Basic Logging");
            logger.LogInfo("Application started");
            logger.LogWarning("This is a warning");
            logger.LogError("This is an error");
            
            var logs = logger.GetRecentLogs(3);
            if (logs.Length != 3)
            {
                throw new Exception($"Expected 3 logs, got {logs.Length}");
            }
            Console.WriteLine("✓ Basic logging works");
            Console.WriteLine();

            // Test 2: User action logging
            Console.WriteLine("Test 2: User Action Logging");
            logger.LogUserAction("Open PDF", "File", "test.pdf");
            logger.LogUserAction("Add text", "Page", 2);
            
            var actionLogs = logger.GetRecentLogs(2);
            if (!actionLogs[0].Contains("Open PDF") || !actionLogs[1].Contains("Add text"))
            {
                throw new Exception("User action logging failed");
            }
            Console.WriteLine("✓ User action logging works");
            Console.WriteLine();

            // Test 3: Performance logging
            Console.WriteLine("Test 3: Performance Logging");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(100);
            stopwatch.Stop();
            
            logger.LogPerformance("Test operation", stopwatch.Elapsed, "Iterations", 1000);
            
            var perfLogs = logger.GetRecentLogs(1);
            if (!perfLogs[0].Contains("100.00ms"))
            {
                throw new Exception("Performance logging failed");
            }
            Console.WriteLine("✓ Performance logging works");
            Console.WriteLine();

            // Test 4: Concurrent logging
            Console.WriteLine("Test 4: Concurrent Logging");
            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        logger.LogInfo($"Concurrent message {index}-{j}");
                    }
                });
            }
            await Task.WhenAll(tasks);
            
            var concurrentLogs = logger.GetRecentLogs(100);
            if (concurrentLogs.Length < 100)
            {
                throw new Exception($"Expected at least 100 concurrent logs, got {concurrentLogs.Length}");
            }
            Console.WriteLine("✓ Concurrent logging works");
            Console.WriteLine();

            // Test 5: Error logging with exception
            Console.WriteLine("Test 5: Error Logging with Exception");
            var testException = new InvalidOperationException("Test exception");
            logger.LogError("Test error with exception", testException);
            
            var errorLogs = logger.GetRecentLogs(1);
            if (!errorLogs[0].Contains("InvalidOperationException"))
            {
                throw new Exception("Exception logging failed");
            }
            Console.WriteLine("✓ Error logging with exception works");
            Console.WriteLine();

            Console.WriteLine("🎉 All logging tests passed! 🎉");
            Console.WriteLine();
            Console.WriteLine($"Total logs generated: {logger.GetRecentLogs(1000).Length}");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
