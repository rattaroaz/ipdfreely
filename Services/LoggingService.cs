using System.Diagnostics;
using System.Globalization;
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
    /// <summary>Structured values from M.E.L <see cref="ILogger"/>; optional and appended as key=value (OriginalFormat keys skipped).</summary>
    void LogWithNamedState(LogLevel level, string message, Exception? exception, IReadOnlyList<KeyValuePair<string, object?>>? stateProperties);
    Task FlushAsync();
    string[] GetRecentLogs(int count = 50);
    string LogFilePath { get; }
}

public class LoggingService : ILoggingService, IDisposable, IAsyncDisposable
{
    private const int LogChannelCapacity = 10_000;

    private readonly string _logDirectory;
    private string _currentLogFilePath;
    private readonly object _recentLock = new();
    private readonly Queue<string> _recentLogs = new();
    private const int MaxRecentLogs = 100;
    private const int MaxLogFileSize = 10 * 1024 * 1024; // 10MB
    private const int MaxRetentionDays = 7;

    private readonly Channel<string> _logChannel;
    private readonly Channel<TaskCompletionSource> _flushChannel;
    private readonly Task _writerTask;
    private long _droppedLogLines;
    private StreamWriter? _fileWriter;
    private long _currentFileSize;
    private int _lifecycle; // 0: accepting; 1: channel completion started; 2: closed
    private int _rotationCounter;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public string LogFilePath => _currentLogFilePath;

    public LoggingService()
    {
        _logChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(LogChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _flushChannel = Channel.CreateUnbounded<TaskCompletionSource>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
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
        EnqueueLogEntry("INFO", $"=== ipdfreely logging started | Path: {_currentLogFilePath} | " +
            $"MinLevel: {MinimumLevel} | OS: {Environment.OSVersion} | " +
            $"PID: {Environment.ProcessId} ===", null);
    }

    // ── Background writer (log channel + unbounded flush queue) ─

    private async Task BackgroundWriterLoop()
    {
        var logR = _logChannel.Reader;
        var flushR = _flushChannel.Reader;
        try
        {
            for (;;)
            {
                TryEmitDroppedLineWarning();
                for (;;)
                {
                    if (!flushR.TryRead(out var tcs))
                        break;
                    _fileWriter?.Flush();
                    tcs.TrySetResult();
                }
                for (;;)
                {
                    if (!logR.TryRead(out var line))
                        break;
                    TryEmitDroppedLineWarning();
                    WriteToFile(line);
                }

                if (logR.Completion.IsCompleted && flushR.Completion.IsCompleted)
                    break;

                var moreLog = !logR.Completion.IsCompleted;
                var moreFlush = !flushR.Completion.IsCompleted;
                if (!moreLog && !moreFlush)
                    break;

                if (moreLog && moreFlush)
                {
                    var t1 = logR.WaitToReadAsync();
                    var t2 = flushR.WaitToReadAsync();
                    await Task.WhenAny(t1.AsTask(), t2.AsTask()).ConfigureAwait(false);
                }
                else if (moreLog)
                {
                    if (!await logR.WaitToReadAsync().ConfigureAwait(false))
                        break;
                }
                else
                {
                    if (!await flushR.WaitToReadAsync().ConfigureAwait(false))
                        break;
                }
            }
        }
        catch (ChannelClosedException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Background log writer failed: {ex.Message}");
        }
        finally
        {
            for (;;)
            {
                if (!flushR.TryRead(out var t3))
                    break;
                _fileWriter?.Flush();
                t3.TrySetResult();
            }
            _fileWriter?.Dispose();
            _fileWriter = null;
        }
    }

    private void TryEmitDroppedLineWarning()
    {
        var dropped = Interlocked.Exchange(ref _droppedLogLines, 0);
        if (dropped == 0)
            return;
        var lineBody = $"[Internal] log queue at capacity ({LogChannelCapacity} lines not accepted); {dropped} line(s) dropped. Reduce log rate or level.";
        var logEntry = BuildLogLineString("WARN", lineBody, null);
        lock (_recentLock)
        {
            _recentLogs.Enqueue(logEntry);
            while (_recentLogs.Count > MaxRecentLogs)
            {
                _recentLogs.Dequeue();
            }
        }
        Debug.WriteLine(logEntry);
        WriteToFile(logEntry);
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
                AutoFlush = true
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
            _fileWriter?.Flush();
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

    public void LogWithNamedState(LogLevel level, string message, Exception? exception,
        IReadOnlyList<KeyValuePair<string, object?>>? stateProperties)
    {
        if (ShouldSkipEnqueue()) return;

        if (level != LogLevel.Error)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    if (MinimumLevel > LogLevel.Debug) return;
                    break;
                case LogLevel.Info:
                    if (MinimumLevel > LogLevel.Info) return;
                    break;
                case LogLevel.Warning:
                    if (MinimumLevel > LogLevel.Warning) return;
                    break;
            }
        }

        var levelTag = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            _ => "INFO"
        };

        var propSuffix = FormatStatePropertiesForLine(stateProperties);
        var lineBody = propSuffix is null ? message : message + propSuffix;
        EnqueueLogEntry(levelTag, lineBody, exception);
    }

    // ── Core log method ─────────────────────────────────────────

    private void Log(string level, string message, Exception? exception, params object[] args)
    {
        if (ShouldSkipEnqueue()) return;

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

        EnqueueLogEntry(level, formattedMessage, exception);
    }

    private bool ShouldSkipEnqueue() => Volatile.Read(ref _lifecycle) > 0;

    private void EnqueueLogEntry(string level, string lineBody, Exception? exception)
    {
        try
        {
            if (ShouldSkipEnqueue()) return;

            var logEntry = BuildLogLineString(level, lineBody, exception);
            lock (_recentLock)
            {
                _recentLogs.Enqueue(logEntry);
                while (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.Dequeue();
                }
            }

            Debug.WriteLine(logEntry);

            if (!_logChannel.Writer.TryWrite(logEntry))
            {
                Interlocked.Increment(ref _droppedLogLines);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Logging failed: {ex.Message}");
        }
    }

    private static string BuildLogLineString(string level, string lineBody, Exception? exception)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var logEntry = $"[{timestamp}] [{level}] [T:{threadId}] {lineBody}";
        if (exception != null)
        {
            logEntry += FormatExceptionChain(exception);
        }
        return logEntry;
    }

    private static string? FormatStatePropertiesForLine(IReadOnlyList<KeyValuePair<string, object?>>? stateProperties)
    {
        if (stateProperties is null || stateProperties.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var kv in stateProperties)
        {
            if (string.IsNullOrEmpty(kv.Key)
                || kv.Key.Equals("{OriginalFormat}", StringComparison.Ordinal)
                || kv.Key.Equals("OriginalFormat", StringComparison.Ordinal))
            {
                continue;
            }
            var v = FormatStateValue(kv.Value);
            parts.Add($"{kv.Key}={v}");
        }
        if (parts.Count == 0)
            return null;
        return " | " + string.Join(", ", parts);
    }

    private static string FormatStateValue(object? value) =>
        value switch
        {
            null => "null",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "null",
            IConvertible c => c.ToString(CultureInfo.InvariantCulture) ?? "null",
            _ => value.ToString() ?? "null"
        };

    private static string FormatExceptionChain(Exception ex)
    {
        var result = $" | Exception: {ex.GetType().Name}: {ex.Message}";
        if (!string.IsNullOrEmpty(ex.StackTrace))
            result += Environment.NewLine + ex.StackTrace;

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
        if (Volatile.Read(ref _lifecycle) > 0) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _flushChannel.Writer.WriteAsync(tcs);
        }
        catch (ChannelClosedException)
        {
            return;
        }
        await Task.WhenAny(tcs.Task, Task.Delay(2000)).ConfigureAwait(false);
    }

    public string[] GetRecentLogs(int count = 50)
    {
        lock (_recentLock)
        {
            return _recentLogs.TakeLast(count).ToArray();
        }
    }

    // ── Dispose ─────────────────────────────────────────────────

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _lifecycle, 1, 0) != 0)
            return;

        try
        {
            var shutdown = BuildLogLineString("INFO", "=== ipdfreely logging shutting down ===", null);
            lock (_recentLock)
            {
                _recentLogs.Enqueue(shutdown);
                while (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.Dequeue();
                }
            }
            Debug.WriteLine(shutdown);
            if (!_logChannel.Writer.TryWrite(shutdown))
            {
                Interlocked.Increment(ref _droppedLogLines);
            }

            _logChannel.Writer.Complete();
            _flushChannel.Writer.Complete();
            try
            {
                await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
        finally
        {
            Volatile.Write(ref _lifecycle, 2);
        }
    }
}
