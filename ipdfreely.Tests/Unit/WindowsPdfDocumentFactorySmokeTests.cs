#if WINDOWS
using FluentAssertions;
using ipdfreely.Services;
using Xunit;

namespace ipdfreely.Tests.Unit;

/// <summary>
/// Smoke tests for the WinRT-based PDF factory (requires net10.0-windows).
/// </summary>
public sealed class WindowsPdfDocumentFactorySmokeTests
{
    [Fact]
    public async Task OpenFromFilePathAsync_EmptyPdf_ReturnsDocumentWithOnePage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ipdfreely_smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = TestHelpers.CreateEmptyPdf(tempDir);

        try
        {
            var factory = new WindowsPdfDocumentFactory();
            await using var doc = await factory.OpenFromFilePathAsync(path);
            doc.Should().NotBeNull();
            doc!.PageCount.Should().Be(1);
        }
        finally
        {
            TryDelete(path);
            TryDeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task GetPageBitmapAsync_FirstPage_ReturnsPngSignature()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ipdfreely_smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = TestHelpers.CreatePdfWithText(tempDir, "Smoke");

        try
        {
            var factory = new WindowsPdfDocumentFactory();
            await using var doc = await factory.OpenFromFilePathAsync(path);
            doc.Should().NotBeNull();

            var bmp = await doc!.GetPageBitmapAsync(0, 240.0);
            bmp.Should().NotBeNull();
            bmp!.PngBytes.Length.Should().BeGreaterThan(80);
            bmp.PngBytes[0].Should().Be(0x89);
            bmp.PngBytes[1].Should().Be(0x50);
            bmp.PngBytes[2].Should().Be(0x4E);
            bmp.PngBytes[3].Should().Be(0x47);
            bmp.Width.Should().BePositive();
            bmp.Height.Should().BePositive();
        }
        finally
        {
            TryDelete(path);
            TryDeleteDir(tempDir);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }
}
#endif
