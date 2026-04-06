#if WINDOWS
using Microsoft.Maui.Platform;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;
#endif

namespace ipdfreely.Services;

public sealed class PdfExportService
{
    public async Task<bool> SavePdfAsync(byte[] pdfBytes, string suggestedFileName = "export.pdf",
        CancellationToken ct = default)
    {
        if (pdfBytes.Length == 0)
            return false;

#if WINDOWS
        var window = Application.Current?.Windows?.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
        if (window is null)
            return false;

        var hwnd = WindowNative.GetWindowHandle(window);
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName);
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });

        var file = await picker.PickSaveFileAsync().AsTask(ct);
        if (file is null)
            return false;

        await FileIO.WriteBytesAsync(file, pdfBytes).AsTask(ct);
        return true;
#else
        var path = Path.Combine(FileSystem.CacheDirectory, Path.GetFileName(suggestedFileName));
        await File.WriteAllBytesAsync(path, pdfBytes, ct).ConfigureAwait(false);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Save PDF",
            File = new ShareFile(path)
        }).ConfigureAwait(false);
        return true;
#endif
    }
}
