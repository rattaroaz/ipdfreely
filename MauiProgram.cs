using ipdfreely.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ipdfreely
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Centralised DI wiring (unit-testable on any TFM via AppServices.Register).
            // The LoggingService is the authoritative log pipeline:
            //   - app code uses ILoggingService directly,
            //   - host/framework ILogger traffic is forwarded into it via
            //     LoggingServiceForwardingLoggerProvider so every log line ends up in
            //     the same file + recent-log ring buffer.
            AppServices.Register(builder.Services);

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
