using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace ipdfreely.Services;

#pragma warning disable CS0618 // PdfPig: ExperimentalAccess pending upstream alignment in some builds

public sealed class PdfContentDetectionService
{
    private readonly ILoggingService? _logger;
    
    public PdfContentDetectionService(ILoggingService? logger = null)
    {
        _logger = logger;
    }
    
    public const int HeuristicSkipWhenFieldCountOnPage = 18;

    public PdfContentDetectionResult Analyze(string filePath)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger?.LogPdfOperation("Content detection started", null, filePath);
        
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger?.LogWarning("Content detection failed: invalid file path {0}", filePath);
            return new PdfContentDetectionResult();
        }

        List<global::UglyToad.PdfPig.Content.Page>? pages = null;
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(filePath);
            pages = document.GetPages().ToList();
            _logger?.LogInfo("Content detection: opened PDF with {0} pages", pages.Count);

            AcroForm? form = null;
            document.TryGetForm(out form);
            _logger?.LogInfo("Content detection: {0}", form is not null ? "AcroForm found" : "No AcroForm");

            var widgetLookup = BuildAllPageWidgetLookups(pages);
            var fontSizeLookup = BuildWidgetFontSizeLookup(pages);

            var acroFields = new List<DetectedFormField>();
            var widgetFields = new List<DetectedFormField>();
            var visualFields = new List<DetectedFormField>();
            var textLines = new List<DetectedTextRegion>();

        foreach (global::UglyToad.PdfPig.Content.Page pdfPage in pages)
        {
            textLines.AddRange(ExtractEmbeddedTextLines(pdfPage));

            var pageIndex = pdfPage.Number - 1;
            var acroOnPage = 0;

            if (form is not null)
            {
                foreach (var root in form.GetFieldsForPage(pdfPage.Number))
                {
                    WalkTopLevelFields(root, leaf =>
                    {
                        if (leaf is AcroNonTerminalField)
                            return;

                        if (leaf.PageNumber != pdfPage.Number)
                            return;

                        acroOnPage++;

                        var bounds = TryResolveFieldBounds(leaf, widgetLookup);
                        if (bounds is null)
                            return;

                        acroFields.Add(new DetectedFormField
                        {
                            PageIndex = pageIndex,
                            Name = BuildFieldName(leaf),
                            Kind = MapDetectedKind(leaf),
                            Bounds = bounds.Value,
                            Value = TryGetFieldValue(leaf),
                            FontSizePts = TryExtractFieldFontSize(leaf, fontSizeLookup),
                            Source = "AcroForm"
                        });
                    });
                }
            }

            widgetFields.AddRange(CollectWidgetAnnotations(pdfPage));

            visualFields.AddRange(CollectVisualInputRectangles(pdfPage, acroOnPage));
        }

        var result = new PdfContentDetectionResult
        {
            AcroFormFields = acroFields,
            WidgetFields = widgetFields,
            VisualHeuristicFields = visualFields,
            EmbeddedTextLines = textLines
        };
        
        _logger?.LogInfo("Content detection completed: AcroForm={0}, Widget={1}, Visual={2}, TextLines={3}", 
            acroFields.Count, widgetFields.Count, visualFields.Count, textLines.Count);
        
        return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Content detection failed", ex);
            return new PdfContentDetectionResult();
        }
        finally
        {
            stopwatch.Stop();
            _logger?.LogPerformance("Content detection", stopwatch.Elapsed, "Pages", pages?.Count ?? 0);
        }
    }

    internal static void WalkTopLevelFields(AcroFieldBase field, Action<AcroFieldBase> visit)
    {
        visit(field);
        if (field is AcroNonTerminalField nt)
        {
            foreach (var child in nt.Children)
                WalkTopLevelFields(child, visit);
        }
    }

    internal static ILookup<string, double> BuildWidgetFontSizeLookup(
        IReadOnlyList<global::UglyToad.PdfPig.Content.Page> pages)
    {
        var pairs = new List<(string Name, double Size)>();
        foreach (global::UglyToad.PdfPig.Content.Page pdfPage in pages)
        {
            foreach (var a in pdfPage.ExperimentalAccess.GetAnnotations())
            {
                if (a.Type != AnnotationType.Widget)
                    continue;

                var name = GetAnnotationFieldName(a);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var fontSize = TryExtractAnnotationFontSize(a);
                if (fontSize is > 0)
                    pairs.Add((name, fontSize.Value));
            }
        }

        return pairs.ToLookup(p => p.Name, p => p.Size);
    }

    internal static ILookup<string, PdfRectangle> BuildAllPageWidgetLookups(
        IReadOnlyList<global::UglyToad.PdfPig.Content.Page> pages)
    {
        var pairs = new List<(string Name, PdfRectangle Rect)>();
        foreach (global::UglyToad.PdfPig.Content.Page pdfPage in pages)
        {
            foreach (var a in pdfPage.ExperimentalAccess.GetAnnotations())
            {
                if (a.Type != AnnotationType.Widget)
                    continue;

                var name = GetAnnotationFieldName(a);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                pairs.Add((name, a.Rectangle));
            }
        }

        return pairs.ToLookup(p => p.Name, p => p.Rect);
    }

    internal static PdfRectangle? TryResolveFieldBounds(AcroFieldBase field, ILookup<string, PdfRectangle> widgetLookup)
    {
        if (field.Bounds is { } direct)
            return direct;

        var partial = field.Information.PartialName;
        if (!string.IsNullOrEmpty(partial) && widgetLookup.Contains(partial))
            return widgetLookup[partial].FirstOrDefault();

        var alt = field.Information.AlternateName;
        if (!string.IsNullOrEmpty(alt) && widgetLookup.Contains(alt))
            return widgetLookup[alt].FirstOrDefault();

        return null;
    }

    internal static IReadOnlyList<DetectedFormField> CollectWidgetAnnotations(global::UglyToad.PdfPig.Content.Page page)
    {
        var list = new List<DetectedFormField>();
        foreach (var a in page.ExperimentalAccess.GetAnnotations())
        {
            if (a.Type != AnnotationType.Widget)
                continue;

            list.Add(new DetectedFormField
            {
                PageIndex = page.Number - 1,
                Name = GetAnnotationFieldName(a) ?? string.Empty,
                Kind = DetectedFieldKind.WidgetAnnotation,
                Bounds = a.Rectangle,
                Value = a.Content,
                FontSizePts = TryExtractAnnotationFontSize(a),
                Source = "WidgetAnnotation"
            });
        }

        return list;
    }

    internal static IReadOnlyList<DetectedFormField> CollectVisualInputRectangles(
        global::UglyToad.PdfPig.Content.Page page,
        int acroFieldCountOnPage)
    {
        if (acroFieldCountOnPage >= HeuristicSkipWhenFieldCountOnPage)
            return Array.Empty<DetectedFormField>();

        var list = new List<DetectedFormField>();
        foreach (var a in page.ExperimentalAccess.GetAnnotations())
        {
            if (a.Type == AnnotationType.Underline)
            {
                var r = a.Rectangle;
                if (r.Width > 24 && r.Height <= 6)
                {
                    var padY = Math.Max(10, r.Height * 3);
                    var bl = new PdfPoint(r.Left, r.Bottom - padY);
                    var tr = new PdfPoint(r.Right, r.Top + 2);
                    var guess = new PdfRectangle(bl, tr);

                    list.Add(new DetectedFormField
                    {
                        PageIndex = page.Number - 1,
                        Name = string.Empty,
                        Kind = DetectedFieldKind.VisualUnderline,
                        Bounds = guess,
                        Value = a.Content,
                        Source = "UnderlineHeuristic"
                    });
                }
            }
            else if (a.Type == AnnotationType.Square)
            {
                var r = a.Rectangle;
                if (r.Width > 28 && r.Height is > 6 and < 80 && r.Height < r.Width * 0.55)
                {
                    list.Add(new DetectedFormField
                    {
                        PageIndex = page.Number - 1,
                        Name = string.Empty,
                        Kind = DetectedFieldKind.VisualSquare,
                        Bounds = r,
                        Value = a.Content,
                        Source = "SquareAnnotation"
                    });
                }
            }
        }

        return list;
    }

    private static IReadOnlyList<DetectedTextRegion> ExtractEmbeddedTextLines(global::UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
            return Array.Empty<DetectedTextRegion>();

        const double yTol = 2.5;
        var ordered = words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();
        var lines = new List<List<global::UglyToad.PdfPig.Content.Word>>();
        foreach (var w in ordered)
        {
            var placed = false;
            foreach (var line in lines)
            {
                var refY = line[0].BoundingBox.Bottom;
                if (Math.Abs(w.BoundingBox.Bottom - refY) <= yTol)
                {
                    line.Add(w);
                    placed = true;
                    break;
                }
            }

            if (!placed)
                lines.Add(new List<global::UglyToad.PdfPig.Content.Word> { w });
        }

        var regions = new List<DetectedTextRegion>();
        foreach (var line in lines)
        {
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            var text = string.Join(" ", line.Select(x => x.Text));
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var minX = line.Min(x => x.BoundingBox.Left);
            var maxX = line.Max(x => x.BoundingBox.Right);
            var minY = line.Min(x => x.BoundingBox.Bottom);
            var maxY = line.Max(x => x.BoundingBox.Top);
            var bounds = new PdfRectangle(new PdfPoint(minX, minY), new PdfPoint(maxX, maxY));

            regions.Add(new DetectedTextRegion
            {
                PageIndex = page.Number - 1,
                LineText = text.Trim(),
                Bounds = bounds
            });
        }

        return regions;
    }

    private static string? GetAnnotationFieldName(Annotation annotation)
    {
        if (!annotation.AnnotationDictionary.TryGet(NameToken.T, out var token))
            return null;

        return token switch
        {
            StringToken s => s.Data,
            _ => token.ToString()
        };
    }

    private static string BuildFieldName(AcroFieldBase field)
    {
        if (!string.IsNullOrEmpty(field.Information.PartialName))
            return field.Information.PartialName!;
        if (!string.IsNullOrEmpty(field.Information.AlternateName))
            return field.Information.AlternateName!;
        if (!string.IsNullOrEmpty(field.Information.MappingName))
            return field.Information.MappingName!;
        return field.FieldType.ToString();
    }

    private static DetectedFieldKind MapDetectedKind(AcroFieldBase field)
    {
        if (field is AcroNonTerminalField)
            return DetectedFieldKind.NonTerminal;

        return field.FieldType switch
        {
            AcroFieldType.Text => DetectedFieldKind.Text,
            AcroFieldType.ComboBox => DetectedFieldKind.ComboBox,
            AcroFieldType.ListBox => DetectedFieldKind.ListBox,
            AcroFieldType.PushButton => DetectedFieldKind.PushButton,
            AcroFieldType.Checkbox => DetectedFieldKind.Checkbox,
            AcroFieldType.Checkboxes => DetectedFieldKind.Checkbox,
            AcroFieldType.RadioButton => DetectedFieldKind.RadioGroup,
            AcroFieldType.RadioButtons => DetectedFieldKind.RadioGroup,
            AcroFieldType.Signature => DetectedFieldKind.Signature,
            _ => DetectedFieldKind.Unknown
        };
    }

    internal static double? TryExtractFieldFontSize(AcroFieldBase field, ILookup<string, double>? fontSizeLookup)
    {
        if (fontSizeLookup is null)
            return null;

        // Match by partial name or alternate name against the widget annotation font sizes
        var partial = field.Information?.PartialName;
        if (!string.IsNullOrEmpty(partial) && fontSizeLookup.Contains(partial))
            return fontSizeLookup[partial].FirstOrDefault();

        var alt = field.Information?.AlternateName;
        if (!string.IsNullOrEmpty(alt) && fontSizeLookup.Contains(alt))
            return fontSizeLookup[alt].FirstOrDefault();

        return null;
    }

    internal static double? TryExtractAnnotationFontSize(Annotation annotation)
    {
        try
        {
            if (annotation.AnnotationDictionary.TryGet(NameToken.Create("DA"), out var daToken))
                return ParseDaFontSize(daToken);
        }
        catch { }

        return null;
    }

    internal static double? ParseDaFontSize(IToken? token)
    {
        if (token is null)
            return null;

        var da = token switch
        {
            StringToken s => s.Data,
            _ => token.ToString()
        };

        if (string.IsNullOrWhiteSpace(da))
            return null;

        // DA format: "/FontName size Tf ..."  e.g. "/Helv 12 Tf 0 g"
        var match = Regex.Match(da, @"(\d+\.?\d*)\s+Tf\b");
        if (match.Success &&
            double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
            size > 0)
        {
            return size;
        }

        return null;
    }

    private static string? TryGetFieldValue(AcroFieldBase field) => field switch
    {
        AcroTextField t => t.Value,
        AcroCheckboxField c => c.IsChecked ? "Yes" : "Off",
        AcroComboBoxField c => string.Join(",", c.SelectedOptions.Select(o => o?.ToString() ?? string.Empty)),
        AcroListBoxField l => string.Join(",", l.SelectedOptions.Select(o => o?.ToString() ?? string.Empty)),
        _ => null
    };
}
