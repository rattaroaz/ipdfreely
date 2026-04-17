using Xunit;
using FluentAssertions;
using ipdfreely.Services;

namespace ipdfreely.Tests.Unit;

public class ModelTests
{
    // ── PageTextOverlay defaults ─────────────────────────────────

    [Fact]
    public void PageTextOverlay_Defaults_AreZeroAndEmpty()
    {
        var o = new PageTextOverlay();

        o.RelX.Should().Be(0);
        o.RelY.Should().Be(0);
        o.RelW.Should().Be(0);
        o.RelH.Should().Be(0);
        o.Text.Should().BeEmpty();
        o.RelFontSize.Should().Be(0);
        o.FontFamily.Should().Be("OpenSans");
        o.IsFieldEdit.Should().BeFalse();
    }

    [Fact]
    public void PageTextOverlay_InitProperties_AreSetCorrectly()
    {
        var o = new PageTextOverlay
        {
            RelX = 0.1, RelY = 0.2, RelW = 0.3, RelH = 0.4,
            Text = "Hello", RelFontSize = 0.02,
            FontFamily = "Arial", IsFieldEdit = true
        };

        o.RelX.Should().Be(0.1);
        o.RelY.Should().Be(0.2);
        o.RelW.Should().Be(0.3);
        o.RelH.Should().Be(0.4);
        o.Text.Should().Be("Hello");
        o.RelFontSize.Should().Be(0.02);
        o.FontFamily.Should().Be("Arial");
        o.IsFieldEdit.Should().BeTrue();
    }

    // ── PdfContentDetectionResult defaults ──────────────────────

    [Fact]
    public void PdfContentDetectionResult_Defaults_AllCollectionsEmpty()
    {
        var r = new PdfContentDetectionResult();

        r.AcroFormFields.Should().NotBeNull().And.BeEmpty();
        r.WidgetFields.Should().NotBeNull().And.BeEmpty();
        r.VisualHeuristicFields.Should().NotBeNull().And.BeEmpty();
        r.EmbeddedTextLines.Should().NotBeNull().And.BeEmpty();
    }

    // ── DetectedFormField defaults ──────────────────────────────

    [Fact]
    public void DetectedFormField_Defaults()
    {
        var f = new DetectedFormField();

        f.PageIndex.Should().Be(0);
        f.Name.Should().BeEmpty();
        f.Kind.Should().Be(DetectedFieldKind.Unknown);
        f.Value.Should().BeNull();
        f.FontSizePts.Should().BeNull();
        f.Source.Should().BeEmpty();
    }

    [Fact]
    public void DetectedFormField_InitProperties_AreSetCorrectly()
    {
        var f = new DetectedFormField
        {
            PageIndex = 2,
            Name = "field1",
            Kind = DetectedFieldKind.Text,
            Value = "hello",
            FontSizePts = 12.0,
            Source = "AcroForm"
        };

        f.PageIndex.Should().Be(2);
        f.Name.Should().Be("field1");
        f.Kind.Should().Be(DetectedFieldKind.Text);
        f.Value.Should().Be("hello");
        f.FontSizePts.Should().Be(12.0);
        f.Source.Should().Be("AcroForm");
    }

    // ── DetectedTextRegion defaults ─────────────────────────────

    [Fact]
    public void DetectedTextRegion_Defaults()
    {
        var t = new DetectedTextRegion();

        t.PageIndex.Should().Be(0);
        t.LineText.Should().BeEmpty();
    }

    // ── RasterFormDraw defaults ─────────────────────────────────

    [Fact]
    public void RasterFormDraw_Defaults()
    {
        var d = new RasterFormDraw();

        d.PageIndex.Should().Be(0);
        d.PngBytes.Should().NotBeNull().And.BeEmpty();
        d.Width.Should().Be(0);
        d.Height.Should().Be(0);
        d.OriginalPageWidthPts.Should().Be(0);
        d.OriginalPageHeightPts.Should().Be(0);
        d.TextOverlays.Should().NotBeNull().And.BeEmpty();
    }

    // ── DetectedFieldKind enum coverage ─────────────────────────

    [Theory]
    [InlineData(DetectedFieldKind.Unknown)]
    [InlineData(DetectedFieldKind.Text)]
    [InlineData(DetectedFieldKind.Checkbox)]
    [InlineData(DetectedFieldKind.ComboBox)]
    [InlineData(DetectedFieldKind.ListBox)]
    [InlineData(DetectedFieldKind.PushButton)]
    [InlineData(DetectedFieldKind.RadioGroup)]
    [InlineData(DetectedFieldKind.Signature)]
    [InlineData(DetectedFieldKind.NonTerminal)]
    [InlineData(DetectedFieldKind.VisualUnderline)]
    [InlineData(DetectedFieldKind.VisualSquare)]
    [InlineData(DetectedFieldKind.WidgetAnnotation)]
    public void DetectedFieldKind_AllValuesAreDefined(DetectedFieldKind kind)
    {
        Enum.IsDefined(typeof(DetectedFieldKind), kind).Should().BeTrue();
    }

    // ── LogLevel enum ───────────────────────────────────────────

    [Fact]
    public void LogLevel_HasCorrectOrdering()
    {
        ((int)LogLevel.Debug).Should().BeLessThan((int)LogLevel.Info);
        ((int)LogLevel.Info).Should().BeLessThan((int)LogLevel.Warning);
        ((int)LogLevel.Warning).Should().BeLessThan((int)LogLevel.Error);
    }
}
