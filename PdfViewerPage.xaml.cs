using ipdfreely.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace ipdfreely;

public partial class PdfViewerPage : ContentPage
{
    private IPdfDocumentFactory? _docFactory;
    private PdfContentDetectionService? _detection;
    private PdfExportService? _export;
    private ILoggingService? _logger;

    private IPdfDocument? _document;
    private string? _filePath;
    private PdfContentDetectionResult? _detectionResult;
    private bool _fieldsVisible = true;
    private bool _placementMode;
    private double _pinchScale = 1.0;
    private double _pinchBaseScale = 1.0;

    private readonly List<PageHost> _pages = new();
    private readonly List<UserPlacedText> _userTexts = new();

    public PdfViewerPage()
    {
        InitializeComponent();
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        ZoomHost.GestureRecognizers.Add(pinch);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_docFactory is not null)
            return;

        if (Handler?.MauiContext is not { } ctx)
            return;

        try
        {
            _docFactory = ctx.Services.GetRequiredService<IPdfDocumentFactory>();
            _detection = ctx.Services.GetRequiredService<PdfContentDetectionService>();
            _export = ctx.Services.GetRequiredService<PdfExportService>();
            _logger = ctx.Services.GetRequiredService<ILoggingService>();
            _logger.LogInfo("PdfViewerPage services initialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize services: {ex.Message}");
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchBaseScale = Math.Clamp(ZoomHost.Scale, 0.5, 4.0);
                break;
            case GestureStatus.Running:
                var next = Math.Clamp(_pinchBaseScale * e.Scale, 0.5, 4.0);
                ZoomHost.Scale = next;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _pinchScale = Math.Clamp(ZoomHost.Scale, 0.5, 4.0);
                ZoomHost.Scale = _pinchScale;
                break;
        }
    }

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        _logger?.LogUserAction("Open PDF clicked");
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        await EnsureServicesAsync();
        if (_docFactory is null)
        {
            _logger?.LogError("Document factory not available for open operation");
            return;
        }

        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open PDF",
                FileTypes = FilePickerFileType.Pdf
            });

            if (pick is null)
            {
                _logger?.LogInfo("User cancelled file picker");
                return;
            }

            _logger?.LogInfo("File selected: {0}", pick.FullPath);
            await LoadPdfAsync(pick.FullPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to open PDF", ex);
        }
        finally
        {
            stopwatch.Stop();
            _logger?.LogPerformance("Open PDF operation", stopwatch.Elapsed);
        }
    }

    private async Task EnsureServicesAsync()
    {
        if (_docFactory is not null)
            return;

        await Task.Yield();
        if (Handler?.MauiContext is { } ctx)
        {
            _docFactory = ctx.Services.GetRequiredService<IPdfDocumentFactory>();
            _detection = ctx.Services.GetRequiredService<PdfContentDetectionService>();
            _export = ctx.Services.GetRequiredService<PdfExportService>();
        }
    }

    private async Task LoadPdfAsync(string path)
    {
        if (_docFactory is null)
        {
            _logger?.LogError("Cannot load PDF: document factory is null");
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger?.LogPdfOperation("Load PDF started", null, path);

        try
        {
            await DisposeDocumentAsync();

            _filePath = path;
            StatusLabel.Text = "Loading…";

            _document = await _docFactory.OpenFromFilePathAsync(path);
            if (_document is null)
            {
                _logger?.LogError("Failed to open PDF document: factory returned null");
                StatusLabel.Text = "Could not open PDF.";
                return;
            }

            _logger?.LogInfo("PDF document opened successfully, pages: {0}", _document.PageCount);

            _detectionResult = _detection?.Analyze(path) ?? new PdfContentDetectionResult();

            var pagePts = ReadPageSizesPts(path);
            await BuildUiAsync(pagePts);
            StatusLabel.Text = System.IO.Path.GetFileName(path);
            
            _logger?.LogPdfOperation("Load PDF completed", _document.PageCount, path);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to load PDF", ex);
            StatusLabel.Text = $"Error: {ex.Message}";
            await DisposeDocumentAsync();
        }
        finally
        {
            stopwatch.Stop();
            _logger?.LogPerformance("Load PDF", stopwatch.Elapsed, "Pages", _document?.PageCount ?? 0);
        }
    }

    private static List<(double W, double H)> ReadPageSizesPts(string path)
    {
        var list = new List<(double W, double H)>();
        try
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            foreach (var p in doc.GetPages())
                list.Add((p.Width, p.Height));
        }
        catch
        {
            // If PdfPig fails, callers may still render via platform PDF without field mapping precision.
        }

        return list;
    }

    private async Task BuildUiAsync(IReadOnlyList<(double W, double H)> pagePts)
    {
        ClearPageUi();

        if (_document is null)
            return;

        var scrollW = PageScroll.Width > 0 ? PageScroll.Width : Width;
        if (scrollW <= 0)
            scrollW = 400;

        var maxPixelW = Math.Max(320, scrollW - 24);

        for (var i = 0; i < _document.PageCount; i++)
        {
            var bmp = await _document.GetPageBitmapAsync(i, maxPixelW);
            if (bmp is null)
                continue;

            var ptsW = i < pagePts.Count ? pagePts[i].W : bmp.Width * 72.0 / 96.0;
            var ptsH = i < pagePts.Count ? pagePts[i].H : bmp.Height * 72.0 / 96.0;

            var dispW = maxPixelW;
            var dispH = maxPixelW * (bmp.Height / (double)bmp.Width);

            var host = new PageHost
            {
                PageIndex = i,
                OriginalPageIndex = i,
                PageWidthPts = ptsW,
                PageHeightPts = ptsH,
                DisplayWidth = dispW,
                DisplayHeight = dispH
            };

            var root = new Border
            {
                StrokeThickness = 1,
                Stroke = Brush.Gray,
                Padding = 0,
                HorizontalOptions = LayoutOptions.Center
            };

            var inner = new Grid { WidthRequest = dispW, HeightRequest = dispH };

            var img = new Image
            {
                Source = bmp.ToImageSource(),
                Aspect = Aspect.AspectFill,
                WidthRequest = dispW,
                HeightRequest = dispH
            };

            var overlay = new AbsoluteLayout
            {
                WidthRequest = dispW,
                HeightRequest = dispH,
                InputTransparent = false
            };

            inner.Add(img);
            inner.Add(overlay);

            host.Overlay = overlay;
            root.Content = inner;

            AddFieldOverlays(host, overlay);
            AttachOverlayPlacementTap(host, overlay);
            PagesLayout.Add(root);
            _pages.Add(host);

            AddThumbnail(i, bmp);
        }

        _fieldsVisible = true;
        ApplyFieldsVisibility();
        UpdateToggleFieldsText();
    }

    private void AddThumbnail(int index, PdfPageBitmap bmp)
    {
        const double tw = 72.0;
        var th = tw * (bmp.Height / (double)bmp.Width);

        var thumbBorder = new Border
        {
            StrokeThickness = 1,
            Stroke = Brush.Gray,
            WidthRequest = tw,
            HeightRequest = th,
            Padding = 0
        };

        var thumb = new Image
        {
            Source = bmp.ToImageSource(),
            Aspect = Aspect.AspectFill
        };

        thumbBorder.Content = thumb;

        var tap = new TapGestureRecognizer();
        var pageIndex = index;
        tap.Tapped += async (_, _) =>
        {
            _selectedPageIndex = pageIndex;
            HighlightSelectedThumbnail();
            await ScrollToPageAsync(pageIndex);
        };
        thumbBorder.GestureRecognizers.Add(tap);

        AttachDragAndDrop(thumbBorder, pageIndex);

        ThumbsLayout.Add(thumbBorder);
    }

    private void AttachDragAndDrop(Border thumbBorder, int pageIndex)
    {
        var dragGesture = new DragGestureRecognizer();
        dragGesture.CanDrag = true;
        dragGesture.DragStarting += (s, e) =>
        {
            e.Data.Properties["PageIndex"] = pageIndex;
            thumbBorder.Opacity = 0.5;
        };
        dragGesture.DropCompleted += (s, e) =>
        {
            thumbBorder.Opacity = 1.0;
        };

        var dropGesture = new DropGestureRecognizer();
        dropGesture.AllowDrop = true;
        dropGesture.DragOver += (s, e) =>
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        };
        dropGesture.Drop += (s, e) =>
        {
            if (e.Data.Properties.TryGetValue("PageIndex", out var srcObj) && srcObj is int sourceIndex)
            {
                if (sourceIndex != pageIndex)
                {
                    MovePage(sourceIndex, pageIndex);
                }
            }
        };

        thumbBorder.GestureRecognizers.Add(dragGesture);
        thumbBorder.GestureRecognizers.Add(dropGesture);
    }

    private int _selectedPageIndex = -1;

    private void HighlightSelectedThumbnail()
    {
        for (int i = 0; i < ThumbsLayout.Children.Count; i++)
        {
            if (ThumbsLayout.Children[i] is Border b)
            {
                b.Stroke = i == _selectedPageIndex ? Colors.Blue : Brush.Gray;
                b.StrokeThickness = i == _selectedPageIndex ? 3 : 1;
            }
        }
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

#if WINDOWS || MACCATALYST
        var deleteAccelerator = new MenuFlyoutItem { Text = "Delete Page" };
        deleteAccelerator.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = "Delete" });
        deleteAccelerator.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = "Backspace" });
        deleteAccelerator.Clicked += async (s, e) =>
        {
            if (_selectedPageIndex >= 0)
            {
                _logger?.LogUserAction("Delete page keyboard shortcut", "Page", _selectedPageIndex + 1);
                bool answer = await DisplayAlertAsync("Confirm Delete", $"Are you sure you want to delete page {_selectedPageIndex + 1}?", "Yes", "No");
                if (answer)
                {
                    DeletePage(_selectedPageIndex);
                    _selectedPageIndex = -1;
                    _logger?.LogInfo("Page deletion confirmed by user");
                }
                else
                {
                    _logger?.LogInfo("Page deletion cancelled by user");
                }
            }
            else
            {
                _logger?.LogWarning("Delete key pressed but no page selected");
            }
        };

        if (this.MenuBarItems.Count == 0)
        {
            var editMenu = new MenuBarItem { Text = "Edit" };
            editMenu.Add(deleteAccelerator);
            this.MenuBarItems.Add(editMenu);
        }
#endif
    }

    private void MovePage(int sourceIndex, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _pages.Count || targetIndex < 0 || targetIndex >= _pages.Count)
        {
            _logger?.LogWarning("Invalid page move indices: source={0}, target={1}, pageCount={2}", sourceIndex, targetIndex, _pages.Count);
            return;
        }

        _logger?.LogUserAction("Move page", "From", sourceIndex, "To", targetIndex);

        var page = _pages[sourceIndex];
        var ui = PagesLayout.Children[sourceIndex];
        var thumb = ThumbsLayout.Children[sourceIndex];

        _pages.RemoveAt(sourceIndex);
        PagesLayout.Children.RemoveAt(sourceIndex);
        ThumbsLayout.Children.RemoveAt(sourceIndex);

        _pages.Insert(targetIndex, page);
        PagesLayout.Children.Insert(targetIndex, ui);
        ThumbsLayout.Children.Insert(targetIndex, thumb);

        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].PageIndex = i;
        }

        RebuildThumbnails();

        if (_selectedPageIndex == sourceIndex)
            _selectedPageIndex = targetIndex;
        else if (sourceIndex < _selectedPageIndex && targetIndex >= _selectedPageIndex)
            _selectedPageIndex--;
        else if (sourceIndex > _selectedPageIndex && targetIndex <= _selectedPageIndex)
            _selectedPageIndex++;

        HighlightSelectedThumbnail();
        _logger?.LogInfo("Page moved successfully, new selected index: {0}", _selectedPageIndex);
    }

    private async void DeletePage(int index)
    {
        if (index < 0 || index >= _pages.Count || _pages.Count <= 1)
        {
            if (_pages.Count <= 1)
            {
                _logger?.LogWarning("Attempted to delete last page");
                await DisplayAlertAsync("Delete", "Cannot delete the last page.", "OK");
            }
            else
            {
                _logger?.LogWarning("Invalid page delete index: {0}, pageCount: {1}", index, _pages.Count);
            }
            return;
        }

        _logger?.LogUserAction("Delete page", "Index", index);

        var host = _pages[index];

        var textsToRemove = _userTexts.Where(t => t.Host == host).ToList();
        _logger?.LogInfo("Removing {0} text overlays from deleted page", textsToRemove.Count);
        
        foreach (var t in textsToRemove)
            _userTexts.Remove(t);

        _pages.RemoveAt(index);
        PagesLayout.Children.RemoveAt(index);

        for (int i = index; i < _pages.Count; i++)
        {
            _pages[i].PageIndex = i;
        }

        RebuildThumbnails();
    }

    private void RebuildThumbnails()
    {
        ThumbsLayout.Clear();
        foreach (var p in _pages)
        {
            if (PagesLayout.Children[p.PageIndex] is Border root &&
                root.Content is Grid inner &&
                inner.Children.FirstOrDefault(c => c is Image) is Image img)
            {
                var tw = 72.0;
                var th = tw * (p.PageHeightPts / p.PageWidthPts);

                var thumbBorder = new Border
                {
                    StrokeThickness = 1,
                    Stroke = Brush.Gray,
                    WidthRequest = tw,
                    HeightRequest = th,
                    Padding = 0
                };

                var thumb = new Image
                {
                    Source = img.Source,
                    Aspect = Aspect.AspectFill
                };

                thumbBorder.Content = thumb;

                var tap = new TapGestureRecognizer();
                var pageIndex = p.PageIndex;
                tap.Tapped += async (_, _) =>
                {
                    _selectedPageIndex = pageIndex;
                    HighlightSelectedThumbnail();
                    await ScrollToPageAsync(pageIndex);
                };
                thumbBorder.GestureRecognizers.Add(tap);

                AttachDragAndDrop(thumbBorder, pageIndex);

                ThumbsLayout.Add(thumbBorder);
            }
        }

        HighlightSelectedThumbnail();
    }

    private async Task ScrollToPageAsync(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PagesLayout.Count)
            return;

        if (PagesLayout[pageIndex] is not VisualElement child)
            return;

        await PageScroll.ScrollToAsync(child, ScrollToPosition.Start, true);
    }

    private void AddFieldOverlays(PageHost host, AbsoluteLayout overlay)
    {
        if (_detectionResult is null)
            return;

        var all = new List<DetectedFormField>();
        all.AddRange(_detectionResult.AcroFormFields);
        all.AddRange(_detectionResult.WidgetFields);
        all.AddRange(_detectionResult.VisualHeuristicFields);

        foreach (var field in all.Where(f => f.PageIndex == host.PageIndex))
        {
            var (relX, relY, relW, relH) = PdfCoordinateMapper.ToRelativeOverlay(
                field.Bounds, host.PageWidthPts, host.PageHeightPts);

            if (relW <= 0 || relH <= 0)
                continue;

            var box = new Border
            {
                BackgroundColor = Color.FromRgba(0, 120, 255, 0.15),
                Stroke = Color.FromRgba(0, 80, 200, 0.6),
                StrokeThickness = 1,
                ZIndex = 0
            };

            AbsoluteLayout.SetLayoutBounds(box, new Rect(relX, relY, relW, relH));
            AbsoluteLayout.SetLayoutFlags(box, AbsoluteLayoutFlags.All);
            overlay.Children.Add(box);
            host.FieldViews.Add(box);
        }
    }

    private void ClearPageUi()
    {
        _logger?.LogInfo("Clearing page UI");
        _placementMode = false;
        PagesLayout.Clear();
        ThumbsLayout.Clear();
        _pages.Clear();
        _userTexts.Clear();
        UpdatePlacementUi();
        _logger?.LogInfo("Page UI cleared, removed {0} pages and {1} text overlays", _pages.Count, _userTexts.Count);
    }

    private async Task DisposeDocumentAsync()
    {
        if (_document is null)
            return;

        try
        {
            _logger?.LogInfo("Disposing document");
            await _document.DisposeAsync();
            _logger?.LogInfo("Document disposed successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to dispose document", ex);
        }

        _document = null;
    }

    private void ApplyFieldsVisibility()
    {
        foreach (var p in _pages)
        {
            foreach (var v in p.FieldViews)
                v.IsVisible = _fieldsVisible;
        }
    }

    private void UpdateToggleFieldsText()
    {
        ToggleFieldsButton.Text = _fieldsVisible ? "Hide fields" : "Show fields";
    }

    private void OnToggleFieldsClicked(object? sender, EventArgs e)
    {
        _fieldsVisible = !_fieldsVisible;
        ApplyFieldsVisibility();
        UpdateToggleFieldsText();
        _logger?.LogUserAction("Toggle fields visibility", "Visible", _fieldsVisible);
    }

    private async void OnAddTextClicked(object? sender, EventArgs e)
    {
        _logger?.LogUserAction("Add text clicked");
        
        await EnsureServicesAsync();
        if (_document is null || _pages.Count == 0)
        {
            _logger?.LogWarning("Add text attempted without PDF loaded");
            await DisplayAlertAsync("Add text", "Open a PDF first.", "OK");
            return;
        }

        _placementMode = !_placementMode;
        UpdatePlacementUi();
        _logger?.LogInfo("Text placement mode {0}", _placementMode ? "enabled" : "disabled");
    }

    private void UpdatePlacementUi()
    {
        AddTextButton.Text = _placementMode ? "Cancel placement" : "Add text";

        if (_placementMode)
        {
            StatusLabel.Text = "Click on a page to place text.";
            return;
        }

        StatusLabel.Text = string.IsNullOrEmpty(_filePath) ? string.Empty : System.IO.Path.GetFileName(_filePath);
    }

    private void AttachOverlayPlacementTap(PageHost host, AbsoluteLayout overlay)
    {
        var tap = new TapGestureRecognizer { Buttons = ButtonsMask.Primary };
        tap.Tapped += (_, e) =>
        {
            if (!_placementMode)
                return;

            var pos = e.GetPosition(overlay);
            if (pos is null)
                return;

            var w = host.DisplayWidth;
            var h = host.DisplayHeight;
            if (w <= 0 || h <= 0)
                return;

            var relX = Math.Clamp(pos.Value.X / w, 0, 0.88);
            var relY = Math.Clamp(pos.Value.Y / h, 0, 0.92);

            _logger?.LogUserAction("Text placed on page", "Page", host.PageIndex, "Position", $"({relX:F2}, {relY:F2})");
            AddUserTextOverlay(host, string.Empty, relX, relY);
            _placementMode = false;
            UpdatePlacementUi();
        };

        overlay.GestureRecognizers.Add(tap);
    }

    private void AddUserTextOverlay(PageHost host, string text, double relX, double relY)
    {
        // Start with a default size and font
        var fontPts = 14.0;
        var approxW = 0.22;
        var approxH = 0.06;

        var container = new AbsoluteLayout
        {
            ZIndex = 2
        };

        var border = new Border
        {
            BackgroundColor = Color.FromRgba(255, 255, 200, 0.92),
            Stroke = Colors.DarkGoldenrod,
            StrokeThickness = 1,
            Padding = new Thickness(26, 0)
        };

        var editor = new Editor
        {
            Text = text,
            FontSize = fontPts,
            TextColor = Colors.Black,
            BackgroundColor = Colors.Transparent,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MaxLength = 2000,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            // 2 pixels padding top and bottom as requested
            Margin = new Thickness(0, 2, 0, 2)
        };

        border.Content = editor;

        var deleteBtn = new Button
        {
            Text = "✕",
            TextColor = Colors.White,
            BackgroundColor = Colors.Red,
            CornerRadius = 12,
            WidthRequest = 24,
            HeightRequest = 24,
            Padding = 0,
            Margin = 0,
            FontSize = 12,
            ZIndex = 3
        };

        var dragHandle = new Border
        {
            BackgroundColor = Colors.DarkBlue,
            WidthRequest = 24,
            HeightRequest = 24,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            ZIndex = 3
        };
        ToolTipProperties.SetText(dragHandle, "Drag to move");

        var resizeHandle = new Border
        {
            BackgroundColor = Colors.DarkGoldenrod,
            WidthRequest = 24,
            HeightRequest = 24,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            ZIndex = 3
        };
        ToolTipProperties.SetText(resizeHandle, "Drag to resize");

        container.Children.Add(border);
        container.Children.Add(deleteBtn);
        container.Children.Add(resizeHandle);
        container.Children.Add(dragHandle);

        // Initially position elements within the container
        AbsoluteLayout.SetLayoutFlags(border, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(border, new Rect(0, 0, 1, 1));

        // Delete button top right
        AbsoluteLayout.SetLayoutFlags(deleteBtn, AbsoluteLayoutFlags.PositionProportional);
        AbsoluteLayout.SetLayoutBounds(deleteBtn, new Rect(1, 0, 24, 24));

        // Resize handle bottom right
        AbsoluteLayout.SetLayoutFlags(resizeHandle, AbsoluteLayoutFlags.PositionProportional);
        AbsoluteLayout.SetLayoutBounds(resizeHandle, new Rect(1, 1, 24, 24));

        // Drag handle top left
        AbsoluteLayout.SetLayoutFlags(dragHandle, AbsoluteLayoutFlags.PositionProportional);
        AbsoluteLayout.SetLayoutBounds(dragHandle, new Rect(0, 0, 24, 24));

        AbsoluteLayout.SetLayoutBounds(container, new Rect(relX, relY, approxW, approxH));
        AbsoluteLayout.SetLayoutFlags(container, AbsoluteLayoutFlags.All);

        host.Overlay.Children.Add(container);

        var placed = new UserPlacedText
        {
            Host = host,
            RelX = relX,
            RelY = relY,
            RelW = approxW,
            RelH = approxH,
            FontSizePts = fontPts,
            Border = border,
            TextEditor = editor
        };

        AttachDrag(placed, host, container, dragHandle);
        AttachResize(placed, host, container, resizeHandle);
        
        deleteBtn.Clicked += (_, _) => 
        {
            host.Overlay.Children.Remove(container);
            _userTexts.Remove(placed);
        };

        _userTexts.Add(placed);

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(10), () => editor.Focus());
    }

    private void AttachResize(UserPlacedText placed, PageHost host, AbsoluteLayout container, View handle)
    {
        var pan = new PanGestureRecognizer();
        double startW = 0;
        double startH = 0;
        double initialFont = 0;

        pan.PanUpdated += (_, args) =>
        {
            if (args.StatusType == GestureStatus.Started)
            {
                startW = placed.RelW;
                startH = placed.RelH;
                initialFont = placed.FontSizePts;
            }
            else if (args.StatusType == GestureStatus.Running)
            {
                var w = host.DisplayWidth;
                var h = host.DisplayHeight;
                if (w <= 0 || h <= 0)
                    return;

                var nw = startW + (args.TotalX / w);
                var nh = startH + (args.TotalY / h);
                
                // Enforce minimum size
                nw = Math.Max(0.05, nw);
                nh = Math.Max(0.02, nh);

                // Enforce bounds
                nw = Math.Min(nw, 1.0 - placed.RelX);
                nh = Math.Min(nh, 1.0 - placed.RelY);

                placed.RelW = nw;
                placed.RelH = nh;
                
                // Scale font size based on height change
                var scaleRatio = nh / startH;
                placed.FontSizePts = Math.Max(6, initialFont * scaleRatio);
                placed.TextEditor.FontSize = placed.FontSizePts;

                AbsoluteLayout.SetLayoutBounds(container, new Rect(placed.RelX, placed.RelY, nw, nh));
            }
        };

        handle.GestureRecognizers.Add(pan);
    }

    private void AttachDrag(UserPlacedText placed, PageHost host, AbsoluteLayout container, View handle)
    {
        var pan = new PanGestureRecognizer();
        double panStartX = 0;
        double panStartY = 0;

        pan.PanUpdated += (_, args) =>
        {
            if (args.StatusType == GestureStatus.Started)
            {
                panStartX = placed.RelX;
                panStartY = placed.RelY;
            }
            else if (args.StatusType == GestureStatus.Running)
            {
                var w = host.DisplayWidth;
                var h = host.DisplayHeight;
                if (w <= 0 || h <= 0)
                    return;

                var nx = panStartX + args.TotalX / w;
                var ny = panStartY + args.TotalY / h;
                
                nx = Math.Clamp(nx, 0, 1.0 - placed.RelW);
                ny = Math.Clamp(ny, 0, 1.0 - placed.RelH);

                placed.RelX = nx;
                placed.RelY = ny;

                AbsoluteLayout.SetLayoutBounds(container, new Rect(nx, ny, placed.RelW, placed.RelH));
            }
        };

        // Attach pan to the dedicated drag handle
        handle.GestureRecognizers.Add(pan);
        
        // Also attach to border so user can drag by the background if the editor doesn't steal focus
        placed.Border.GestureRecognizers.Add(pan);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        _logger?.LogUserAction("Save clicked");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        await EnsureServicesAsync();
        if (_export is null || _document is null || string.IsNullOrEmpty(_filePath))
        {
            _logger?.LogWarning("Save attempted without document or export service");
            await DisplayAlertAsync("Save", "Nothing to save. Open a PDF first.", "OK");
            return;
        }

        StatusLabel.Text = "Exporting…";
        _logger?.LogInfo("Starting PDF export process");

        try
        {
            const double exportMaxW = 2400.0;
            var draws = new List<RasterFormDraw>();
            var totalTextOverlays = 0;

            for (var i = 0; i < _pages.Count; i++)
            {
                var host = _pages[i];
                var bmp = await _document.GetPageBitmapAsync(host.OriginalPageIndex, exportMaxW);
                if (bmp is null)
                {
                    _logger?.LogWarning("Failed to get bitmap for page {0}", i);
                    continue;
                }

                var overlays = _userTexts
                    .Where(t => t.Host == host)
                    .Select(t => new PageTextOverlay
                    {
                        RelX = t.RelX,
                        RelY = t.RelY,
                        RelW = t.RelW,
                        RelH = t.RelH,
                        Text = t.TextEditor.Text ?? string.Empty,
                        RelFontSize = t.FontSizePts / host.DisplayHeight
                    })
                    .ToList();
                
                totalTextOverlays += overlays.Count;

                draws.Add(new RasterFormDraw
                {
                    PageIndex = i,
                    PngBytes = bmp.PngBytes,
                    Width = bmp.Width,
                    Height = bmp.Height,
                    TextOverlays = overlays
                });
            }

            _logger?.LogInfo("Processed {0} pages with {1} text overlays", draws.Count, totalTextOverlays);

            var pdfBytes = PdfOverlayExportService.BuildPdfFromRastersAndOverlays(draws);
            var ok = await _export.SavePdfAsync(pdfBytes, System.IO.Path.GetFileName(_filePath) ?? "export.pdf");
            
            StatusLabel.Text = ok ? "Saved." : "Save cancelled.";
            _logger?.LogExportOperation("PDF", ok, ok ? System.IO.Path.GetFileName(_filePath) : null);
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to save PDF", ex);
            StatusLabel.Text = $"Save failed: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            _logger?.LogPerformance("Save PDF", stopwatch.Elapsed, "Pages", _pages.Count);
        }
    }

    private sealed class PageHost
    {
        public int PageIndex { get; set; }
        public int OriginalPageIndex { get; init; }
        public double PageWidthPts { get; init; }
        public double PageHeightPts { get; init; }
        public double DisplayWidth { get; init; }
        public double DisplayHeight { get; init; }
        public AbsoluteLayout Overlay { get; set; } = null!;
        public List<View> FieldViews { get; } = new();
    }

    private sealed class UserPlacedText
    {
        public PageHost Host { get; init; } = null!;
        public double RelX { get; set; }
        public double RelY { get; set; }
        public double RelW { get; set; }
        public double RelH { get; set; }
        public double FontSizePts { get; set; }
        public Border Border { get; init; } = null!;
        public Editor TextEditor { get; init; } = null!;
    }
}
