#if WINDOWS
using Microsoft.Maui.Platform;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;
#endif

namespace ipdfreely.Services;

public sealed class PdfExportService
{
    private readonly ILoggingService? _logger;
    
    public PdfExportService(ILoggingService? logger = null)
    {
        _logger = logger;
    }
    
    public async Task<bool> SavePdfAsync(byte[] pdfBytes, string suggestedFileName = "export.pdf",
        CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger?.LogExportOperation("Save started", true, suggestedFileName);
        
        if (pdfBytes.Length == 0)
        {
            _logger?.LogWarning("Export failed: empty PDF bytes");
            return false;
        }
        
        _logger?.LogInfo("Exporting PDF: {0} bytes, suggested name: {1}", pdfBytes.Length, suggestedFileName);

        try
        {
#if WINDOWS
        var window = Application.Current?.Windows?.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
        if (window is null)
        {
            _logger?.LogError("Export failed: could not get window handle");
            return false;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName);
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });

        var file = await picker.PickSaveFileAsync().AsTask(ct);
        if (file is null)
        {
            _logger?.LogInfo("Export cancelled by user");
            return false;
        }

        await FileIO.WriteBytesAsync(file, pdfBytes).AsTask(ct);
        _logger?.LogExportOperation("File save picker", true, file.Path);
        return true;
#else
        var path = Path.Combine(FileSystem.CacheDirectory, Path.GetFileName(suggestedFileName));
        await File.WriteAllBytesAsync(path, pdfBytes, ct).ConfigureAwait(false);
        
        _logger?.LogInfo("Export: saved to cache, starting share dialog");
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Save PDF",
            File = new ShareFile(path)
        }).ConfigureAwait(false);
        
        _logger?.LogExportOperation("Share dialog", true, path);
        return true;
#endif
        }
        catch (Exception ex)
        {
            _logger?.LogError("Export failed", ex);
            return false;
        }
        finally
        {
            stopwatch.Stop();
            _logger?.LogPerformance("Export PDF", stopwatch.Elapsed, "Size", pdfBytes.Length);
        }
    }
}
