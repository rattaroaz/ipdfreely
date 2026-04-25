using System.Collections;
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

            var props = ExtractStateProperties(state);
            _logging.LogWithNamedState(FromMsLogLevel(logLevel), body, exception, props);
        }
    }

    private static IReadOnlyList<KeyValuePair<string, object?>>? ExtractStateProperties<TState>(TState state)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> ro1)
        {
            return ro1;
        }
        if (state is IReadOnlyList<KeyValuePair<string, object>> ro2)
        {
            var list = new List<KeyValuePair<string, object?>>(ro2.Count);
            foreach (var kv in ro2)
            {
                list.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
            }
            return list;
        }
        if (state is IEnumerable<KeyValuePair<string, object?>> e0)
        {
            return e0 is List<KeyValuePair<string, object?>> l0 ? l0 : e0.ToList();
        }
        if (state is IEnumerable enumerable && state is not string)
        {
            var pairs = new List<KeyValuePair<string, object?>>();
            foreach (var o in enumerable)
            {
                if (o is KeyValuePair<string, object?> a1)
                {
                    pairs.Add(a1);
                }
                else if (o is KeyValuePair<string, object> a2)
                {
                    pairs.Add(new KeyValuePair<string, object?>(a2.Key, a2.Value));
                }
            }
            return pairs.Count == 0 ? null : pairs;
        }
        return null;
    }

    private static LogLevel FromMsLogLevel(MsLogLevel l) => l switch
    {
        MsLogLevel.Trace or MsLogLevel.Debug => LogLevel.Debug,
        MsLogLevel.Information => LogLevel.Info,
        MsLogLevel.Warning => LogLevel.Warning,
        MsLogLevel.Error or MsLogLevel.Critical => LogLevel.Error,
        _ => LogLevel.Info
    };

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
