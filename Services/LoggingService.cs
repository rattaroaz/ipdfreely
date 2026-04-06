using System.Diagnostics;
using System.Text;

namespace ipdfreely.Services;

public interface ILoggingService
{
    void LogDebug(string message, params object[] args);
    void LogInfo(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(string message, Exception? exception = null, params object[] args);
    void LogUserAction(string action, params object[] details);
    void LogPerformance(string operation, TimeSpan duration, params object[] details);
    void LogPdfOperation(string operation, int? pageCount = null, string? filePath = null);
    void LogExportOperation(string format, bool success, string? outputPath = null);
    Task FlushAsync();
    string[] GetRecentLogs(int count = 50);
}

public class LoggingService : ILoggingService
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private readonly Queue<string> _recentLogs = new();
    private const int MaxRecentLogs = 100;
    private const int MaxLogFileSize = 10 * 1024 * 1024; // 10MB

    public LoggingService()
    {
        try
        {
            var appDataDir = FileSystem.AppDataDirectory;
            _logDirectory = Path.Combine(appDataDir, "Logs");
            Directory.CreateDirectory(_logDirectory);
            
            var logFileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            _logFilePath = Path.Combine(_logDirectory, logFileName);
        }
        catch (Exception ex)
        {
            // Fallback for test environments where MAUI FileSystem is not initialized
            Console.WriteLine($"Warning: Could not initialize log directory using MAUI FileSystem: {ex.Message}");
            
            _logDirectory = Path.Combine(Path.GetTempPath(), "ipdfreely_tests", "Logs");
            Directory.CreateDirectory(_logDirectory);
            
            var logFileName = $"test_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            _logFilePath = Path.Combine(_logDirectory, logFileName);
        }
    }

    private void EnsureLogDirectory()
    {
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // Clean old log files (keep last 7 days)
        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "ipdfreely_*.log");
            var cutoffDate = DateTime.Now.AddDays(-7);
            
            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate || fileInfo.Length > MaxLogFileSize * 2)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clean old log files: {ex.Message}");
        }
    }

    public void LogDebug(string message, params object[] args)
    {
        Log("DEBUG", message, null, args);
    }

    public void LogInfo(string message, params object[] args)
    {
        Log("INFO", message, null, args);
    }

    public void LogWarning(string message, params object[] args)
    {
        Log("WARN", message, null, args);
    }

    public void LogError(string message, Exception? exception = null, params object[] args)
    {
        Log("ERROR", message, exception, args);
    }

    public void LogUserAction(string action, params object[] details)
    {
        var detailsStr = details.Length > 0 ? $" | Details: {string.Join(", ", details)}" : "";
        Log("USER", $"Action: {action}{detailsStr}", null);
    }

    public void LogPerformance(string operation, TimeSpan duration, params object[] details)
    {
        var detailsStr = details.Length > 0 ? $" | Details: {string.Join(", ", details)}" : "";
        Log("PERF", $"Operation: {operation} | Duration: {duration.TotalMilliseconds:F2}ms{detailsStr}", null);
    }

    public void LogPdfOperation(string operation, int? pageCount = null, string? filePath = null)
    {
        var pageInfo = pageCount.HasValue ? $" | Pages: {pageCount.Value}" : "";
        var pathInfo = !string.IsNullOrEmpty(filePath) ? $" | Path: {Path.GetFileName(filePath)}" : "";
        Log("PDF", $"Operation: {operation}{pageInfo}{pathInfo}", null);
    }

    public void LogExportOperation(string format, bool success, string? outputPath = null)
    {
        var pathInfo = !string.IsNullOrEmpty(outputPath) ? $" | Output: {Path.GetFileName(outputPath)}" : "";
        Log("EXPORT", $"Format: {format} | Success: {success}{pathInfo}", null);
    }

    private void Log(string level, string message, Exception? exception, params object[] args)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
            
            var logEntry = $"[{timestamp}] [{level}] [T:{threadId}] {formattedMessage}";
            
            if (exception != null)
            {
                logEntry += $" | Exception: {exception.GetType().Name}: {exception.Message}";
                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    logEntry += Environment.NewLine + exception.StackTrace;
                }
            }

            // Add to recent logs
            lock (_lock)
            {
                _recentLogs.Enqueue(logEntry);
                while (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.Dequeue();
                }
            }

            // Write to console
            Debug.WriteLine(logEntry);
            
            // Write to file asynchronously
            Task.Run(() => WriteToFile(logEntry));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Logging failed: {ex.Message}");
        }
    }

    private void WriteToFile(string logEntry)
    {
        try
        {
            lock (_lock)
            {
                // Check file size and rotate if necessary
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length > MaxLogFileSize)
                    {
                        var rotatedPath = Path.Combine(_logDirectory, $"ipdfreely_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                        File.Move(_logFilePath, rotatedPath);
                    }
                }

                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write to log file: {ex.Message}");
        }
    }

    public async Task FlushAsync()
    {
        // Ensure all pending writes are completed
        await Task.Delay(100);
    }

    public string[] GetRecentLogs(int count = 50)
    {
        lock (_lock)
        {
            return _recentLogs.TakeLast(count).ToArray();
        }
    }
}
