using Microsoft.Extensions.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace ipdfreely.Services;

/// <summary>
/// Bridges <see cref="Microsoft.Extensions.Logging.ILogger"/> to <see cref="ILoggingService"/>
/// so MAUI / host / library logs share the same file and recent-log pipeline as app code.
/// </summary>
public sealed class LoggingServiceForwardingLoggerProvider : ILoggerProvider
{
    private readonly ILoggingService _logging;

    public LoggingServiceForwardingLoggerProvider(ILoggingService logging) =>
        _logging = logging ?? throw new ArgumentNullException(nameof(logging));

    public ILogger CreateLogger(string categoryName) =>
        new ForwardingLogger(categoryName, _logging);

    public void Dispose()
    {
    }

    private sealed class ForwardingLogger : ILogger
    {
        private readonly string _category;
        private readonly ILoggingService _logging;

        public ForwardingLogger(string category, ILoggingService logging)
        {
            _category = category ?? string.Empty;
            _logging = logging;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(MsLogLevel logLevel)
        {
            if (logLevel == MsLogLevel.None)
                return false;

            if (_logging is LoggingService svc)
                return IsMsLevelEnabledForLoggingService(logLevel, svc);

            return true;
        }

        private static bool IsMsLevelEnabledForLoggingService(MsLogLevel ms, LoggingService svc)
        {
            var min = svc.MinimumLevel;
            return ms switch
            {
                MsLogLevel.Trace or MsLogLevel.Debug => min <= LogLevel.Debug,
                MsLogLevel.Information => min <= LogLevel.Info,
                MsLogLevel.Warning => min <= LogLevel.Warning,
                MsLogLevel.Error or MsLogLevel.Critical => true,
                _ => false
            };
        }

        public void Log<TState>(MsLogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
                return;

            var prefix = string.IsNullOrEmpty(_category) ? string.Empty : $"[{_category}] ";
            var body = prefix + message;
            if (eventId.Id != 0)
                body += $" (EventId={eventId.Id})";

            switch (logLevel)
            {
                case MsLogLevel.Trace:
                case MsLogLevel.Debug:
                    _logging.LogDebug("{0}", body);
                    break;
                case MsLogLevel.Information:
                    _logging.LogInfo("{0}", body);
                    break;
                case MsLogLevel.Warning:
                    _logging.LogWarning("{0}", body);
                    break;
                case MsLogLevel.Error:
                case MsLogLevel.Critical:
                    _logging.LogError("{0}", exception, body);
                    break;
                default:
                    _logging.LogInfo("{0}", body);
                    break;
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
