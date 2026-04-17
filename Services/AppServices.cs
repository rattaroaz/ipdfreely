using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ipdfreely.Services;

/// <summary>
/// Central DI registration for the app's services.
/// Extracted from <c>MauiProgram</c> so the wiring can be unit-tested
/// without building a full MAUI host. The per-platform <see cref="IPdfDocumentFactory"/>
/// selection mirrors <c>MauiProgram.CreateMauiApp</c>.
/// </summary>
public static class AppServices
{
    /// <summary>
    /// Registers the full application service graph (PDF factory, PDF services,
    /// logging service, and the <see cref="ILoggerProvider"/> that forwards
    /// host/framework logs to <see cref="ILoggingService"/>).
    /// </summary>
    public static IServiceCollection Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

#if WINDOWS
        services.AddSingleton<IPdfDocumentFactory, WindowsPdfDocumentFactory>();
#elif ANDROID
        services.AddSingleton<IPdfDocumentFactory, AndroidPdfDocumentFactory>();
#elif IOS || MACCATALYST
        services.AddSingleton<IPdfDocumentFactory, ApplePdfDocumentFactory>();
#else
        services.AddSingleton<IPdfDocumentFactory, NullPdfDocumentFactory>();
#endif

        services.AddSingleton<PdfContentDetectionService>();
        services.AddSingleton<PdfExportService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ILoggerProvider, LoggingServiceForwardingLoggerProvider>();

        return services;
    }
}
