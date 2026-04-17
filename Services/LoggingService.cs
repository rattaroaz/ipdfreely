using System.Diagnostics;
using System.Threading.Channels;

namespace ipdfreely.Services;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

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
    string LogFilePath { get; }
}

public class LoggingService : ILoggingService, IDisposable
{
    private readonly string _logDirectory;
    private string _currentLogFilePath;
    private readonly object _recentLock = new();
    private readonly Queue<string> _recentLogs = new();
    private const int MaxRecentLogs = 100;
    private const int MaxLogFileSize = 10 * 1024 * 1024; // 10MB
    private const int MaxRetentionDays = 7;

    private readonly Channel<string> _writeChannel;
    private readonly Task _writerTask;
    private StreamWriter? _fileWriter;
    private long _currentFileSize;
    private bool _disposed;
    private int _rotationCounter;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public string LogFilePath => _currentLogFilePath;

    public LoggingService()
    {
        _writeChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

        try
        {
            var appDataDir = FileSystem.AppDataDirectory;
            _logDirectory = Path.Combine(appDataDir, "Logs");
        }
        catch
        {
            _logDirectory = Path.Combine(Path.GetTempPath(), "ipdfreely_tests", "Logs");
        }

        Directory.CreateDirectory(_logDirectory);
        _currentLogFilePath = Path.Combine(_logDirectory,
            $"ipdfreely_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.log");
        CleanOldLogFiles();

        _writerTask = Task.Run(BackgroundWriterLoop);

        LogStartupBanner();
    }

    private void LogStartupBanner()
    {
        Log("INFO", $"=== ipdfreely logging started | Path: {_currentLogFilePath} | " +
            $"MinLevel: {MinimumLevel} | OS: {Environment.OSVersion} | " +
            $"PID: {Environment.ProcessId} ===", null);
    }

    // ── Background writer ───────────────────────────────────────

    private async Task BackgroundWriterLoop()
    {
        var reader = _writeChannel.Reader;
        try
        {
            await foreach (var entry in reader.ReadAllAsync())
            {
                if (ReferenceEquals(entry, FlushSentinel))
                {
                    _fileWriter?.Flush();
                    SignalFlush();
                    continue;
                }

                WriteToFile(entry);
            }
        }
        catch (ChannelClosedException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Background log writer failed: {ex.Message}");
        }
        finally
        {
            _fileWriter?.Dispose();
            _fileWriter = null;
        }
    }

    private void SignalFlush()
    {
        TaskCompletionSource? tcs;
        lock (_recentLock)
        {
            tcs = _pendingFlush;
            _pendingFlush = null;
        }
        tcs?.TrySetResult();
    }

    // ── File management ─────────────────────────────────────────

    private void CleanOldLogFiles()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "ipdfreely_*.log");
            var cutoffDate = DateTime.Now.AddDays(-MaxRetentionDays);

            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate || fileInfo.Length > MaxLogFileSize * 2)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clean old log files: {ex.Message}");
        }
    }

    private void EnsureWriter()
    {
        if (_fileWriter is not null) return;

        try
        {
            _fileWriter = new StreamWriter(_currentLogFilePath, append: true)
            {
                AutoFlush = false
            };
            _currentFileSize = new FileInfo(_currentLogFilePath).Exists
                ? new FileInfo(_currentLogFilePath).Length
                : 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open log file: {ex.Message}");
        }
    }

    private void RotateIfNeeded()
    {
        if (_currentFileSize < MaxLogFileSize) return;

        try
        {
            _fileWriter?.Dispose();
            _fileWriter = null;

            _rotationCounter++;
            _currentLogFilePath = Path.Combine(_logDirectory,
                $"ipdfreely_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}_{_rotationCounter}.log");
            _currentFileSize = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Log rotation failed: {ex.Message}");
        }
    }

    private void WriteToFile(string logEntry)
    {
        try
        {
            RotateIfNeeded();
            EnsureWriter();

            if (_fileWriter is null) return;

            _fileWriter.WriteLine(logEntry);
            _currentFileSize += logEntry.Length + Environment.NewLine.Length;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write to log file: {ex.Message}");
            // Discard broken writer so EnsureWriter reopens next time
            try { _fileWriter?.Dispose(); } catch { }
            _fileWriter = null;
        }
    }

    // ── Public log methods ──────────────────────────────────────

    public void LogDebug(string message, params object[] args)
    {
        if (MinimumLevel > LogLevel.Debug) return;
        Log("DEBUG", message, null, args);
    }

    public void LogInfo(string message, params object[] args)
    {
        if (MinimumLevel > LogLevel.Info) return;
        Log("INFO", message, null, args);
    }

    public void LogWarning(string message, params object[] args)
    {
        if (MinimumLevel > LogLevel.Warning) return;
        Log("WARN", message, null, args);
    }

    public void LogError(string message, Exception? exception = null, params object[] args)
    {
        Log("ERROR", message, exception, args);
    }

    public void LogUserAction(string action, params object[] details)
    {
        if (MinimumLevel > LogLevel.Info) return;
        var detailsStr = details.Length > 0 ? $" | Details: {string.Join(", ", details)}" : "";
        Log("USER", $"Action: {action}{detailsStr}", null);
    }

    public void LogPerformance(string operation, TimeSpan duration, params object[] details)
    {
        if (MinimumLevel > LogLevel.Info) return;
        var detailsStr = details.Length > 0 ? $" | Details: {string.Join(", ", details)}" : "";
        Log("PERF", $"Operation: {operation} | Duration: {duration.TotalMilliseconds:F2}ms{detailsStr}", null);
    }

    public void LogPdfOperation(string operation, int? pageCount = null, string? filePath = null)
    {
        if (MinimumLevel > LogLevel.Info) return;
        var pageInfo = pageCount.HasValue ? $" | Pages: {pageCount.Value}" : "";
        var pathInfo = !string.IsNullOrEmpty(filePath) ? $" | Path: {Path.GetFileName(filePath)}" : "";
        Log("PDF", $"Operation: {operation}{pageInfo}{pathInfo}", null);
    }

    public void LogExportOperation(string format, bool success, string? outputPath = null)
    {
        if (MinimumLevel > LogLevel.Info) return;
        var pathInfo = !string.IsNullOrEmpty(outputPath) ? $" | Output: {Path.GetFileName(outputPath)}" : "";
        Log("EXPORT", $"Format: {format} | Success: {success}{pathInfo}", null);
    }

    // ── Core log method ─────────────────────────────────────────

    private void Log(string level, string message, Exception? exception, params object[] args)
    {
        if (_disposed) return;

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var threadId = Thread.CurrentThread.ManagedThreadId;

            string formattedMessage;
            if (args.Length > 0)
            {
                try
                {
                    formattedMessage = string.Format(message, args);
                }
                catch (FormatException)
                {
                    formattedMessage = $"{message} [FORMAT_ERROR: {args.Length} args]";
                }
            }
            else
            {
                formattedMessage = message;
            }

            var logEntry = $"[{timestamp}] [{level}] [T:{threadId}] {formattedMessage}";

            if (exception != null)
            {
                logEntry += FormatExceptionChain(exception);
            }

            // Add to recent logs (in-memory ring buffer)
            lock (_recentLock)
            {
                _recentLogs.Enqueue(logEntry);
                while (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.Dequeue();
                }
            }

            // Write to debug output
            Debug.WriteLine(logEntry);

            // Enqueue for background file writer
            _writeChannel.Writer.TryWrite(logEntry);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Logging failed: {ex.Message}");
        }
    }

    private static string FormatExceptionChain(Exception ex)
    {
        var result = $" | Exception: {ex.GetType().Name}: {ex.Message}";
        if (!string.IsNullOrEmpty(ex.StackTrace))
            result += Environment.NewLine + ex.StackTrace;

        // Walk inner exceptions (cap at 5 to prevent runaway chains)
        var inner = ex.InnerException;
        var depth = 0;
        while (inner is not null && depth < 5)
        {
            result += $"{Environment.NewLine}  --- Inner: {inner.GetType().Name}: {inner.Message}";
            if (!string.IsNullOrEmpty(inner.StackTrace))
                result += Environment.NewLine + inner.StackTrace;
            inner = inner.InnerException;
            depth++;
        }

        return result;
    }

    // ── Flush & query ───────────────────────────────────────────

    public async Task FlushAsync()
    {
        if (_disposed) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_recentLock)
        {
            _pendingFlush = tcs;
        }

        _writeChannel.Writer.TryWrite(FlushSentinel);

        await Task.WhenAny(tcs.Task, Task.Delay(500));
    }

    private static readonly string FlushSentinel = new('\x00', 1);
    private TaskCompletionSource? _pendingFlush;

    public string[] GetRecentLogs(int count = 50)
    {
        lock (_recentLock)
        {
            return _recentLogs.TakeLast(count).ToArray();
        }
    }

    // ── Dispose ─────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;

        // Log shutdown before setting flag (Log checks _disposed)
        Log("INFO", "=== ipdfreely logging shutting down ===", null);

        _disposed = true;

        // Complete the channel so the writer loop exits
        _writeChannel.Writer.TryComplete();

        // Give the writer a moment to drain
        _writerTask.Wait(TimeSpan.FromSeconds(2));
    }
}
