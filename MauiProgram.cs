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

#if WINDOWS
            builder.Services.AddSingleton<IPdfDocumentFactory, WindowsPdfDocumentFactory>();
#elif ANDROID
            builder.Services.AddSingleton<IPdfDocumentFactory, AndroidPdfDocumentFactory>();
#elif IOS || MACCATALYST
            builder.Services.AddSingleton<IPdfDocumentFactory, ApplePdfDocumentFactory>();
#else
            builder.Services.AddSingleton<IPdfDocumentFactory, NullPdfDocumentFactory>();
#endif

            builder.Services.AddSingleton<PdfContentDetectionService>();
            builder.Services.AddSingleton<PdfExportService>();
            builder.Services.AddSingleton<ILoggingService, LoggingService>();
            builder.Services.AddSingleton<ILoggerProvider, LoggingServiceForwardingLoggerProvider>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
