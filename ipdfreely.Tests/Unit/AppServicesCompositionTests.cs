using FluentAssertions;
using ipdfreely.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ipdfreely.Tests.Unit;

/// <summary>
/// Exercises the DI graph that <c>MauiProgram.CreateMauiApp()</c> builds,
/// without needing a MAUI host. Mirrors the real registration by calling
/// <see cref="AppServices.Register(IServiceCollection)"/>.
/// </summary>
public class AppServicesCompositionTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        AppServices.Register(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    // ── Contract ────────────────────────────────────────────────

    [Fact]
    public void Register_NullCollection_Throws()
    {
        var act = () => AppServices.Register(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();
        AppServices.Register(services).Should().BeSameAs(services);
    }

    // ── Core registrations ──────────────────────────────────────

    [Fact]
    public void PdfDocumentFactory_IsRegistered()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<IPdfDocumentFactory>().Should().NotBeNull();
    }

    [Fact]
    public void PdfDocumentFactory_HeadlessTfm_ResolvesToNullFactory()
    {
        // On net10.0 without a platform identifier this must be the safe
        // "no-op" factory so the app degrades gracefully, not crashes.
#if !WINDOWS && !ANDROID && !IOS && !MACCATALYST
        using var sp = BuildProvider();
        sp.GetRequiredService<IPdfDocumentFactory>()
            .Should().BeOfType<NullPdfDocumentFactory>();
#endif
    }

    [Fact]
    public void PdfDocumentFactory_WindowsTfm_ResolvesToWindowsFactory()
    {
#if WINDOWS
        using var sp = BuildProvider();
        sp.GetRequiredService<IPdfDocumentFactory>()
            .Should().BeOfType<WindowsPdfDocumentFactory>();
#endif
    }

    [Fact]
    public void PdfContentDetectionService_Resolves()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<PdfContentDetectionService>().Should().NotBeNull();
    }

    [Fact]
    public void PdfExportService_Resolves()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<PdfExportService>().Should().NotBeNull();
    }

    [Fact]
    public void LoggingService_Resolves_AsConcreteLoggingService()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<ILoggingService>().Should().BeOfType<LoggingService>();
    }

    [Fact]
    public void LoggerProvider_Resolves_AsForwardingProvider()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<ILoggerProvider>()
            .Should().BeOfType<LoggingServiceForwardingLoggerProvider>();
    }

    // ── Lifetimes ───────────────────────────────────────────────

    [Fact]
    public void LoggingService_IsSingleton_SharedAcrossResolutions()
    {
        using var sp = BuildProvider();
        var a = sp.GetRequiredService<ILoggingService>();
        var b = sp.GetRequiredService<ILoggingService>();
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void PdfDocumentFactory_IsSingleton_SharedAcrossResolutions()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<IPdfDocumentFactory>()
            .Should().BeSameAs(sp.GetRequiredService<IPdfDocumentFactory>());
    }

    [Fact]
    public void PdfContentDetectionService_IsSingleton_SharedAcrossResolutions()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<PdfContentDetectionService>()
            .Should().BeSameAs(sp.GetRequiredService<PdfContentDetectionService>());
    }

    // ── End-to-end: log lines traverse both paths ───────────────

    [Fact]
    public async Task ILogger_FromContainer_ForwardsToSharedLoggingService()
    {
        using var sp = BuildProvider();
        var logging = sp.GetRequiredService<ILoggingService>();

        // The ILoggerProvider registered by AppServices must bridge into the
        // same LoggingService instance.
        var provider = sp.GetRequiredService<ILoggerProvider>();
        using var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            b.AddProvider(provider);
        });
        var logger = factory.CreateLogger("Composition.Test");

        logger.LogInformation("composed pipeline {0}", "ok");
        await logging.FlushAsync();

        var recent = string.Join("\n", logging.GetRecentLogs(20));
        recent.Should().Contain("[Composition.Test]").And.Contain("composed pipeline ok");
    }
}
