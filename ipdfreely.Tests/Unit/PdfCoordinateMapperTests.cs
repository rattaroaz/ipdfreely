using Xunit;
using FluentAssertions;
using ipdfreely.Services;
using UglyToad.PdfPig.Core;

namespace ipdfreely.Tests.Unit;

public class PdfCoordinateMapperTests
{
    private const double T = 0.0001; // tolerance

    // ── ToRelativeOverlay: full-page coverage ───────────────────

    [Theory]
    [InlineData(612, 792)]   // US Letter
    [InlineData(595, 842)]   // A4
    [InlineData(612, 1008)]  // US Legal
    public void ToRelativeOverlay_FullPage_Returns1x1(double pw, double ph)
    {
        var rect = new PdfRectangle(0, 0, pw, ph);
        var (relX, relY, relW, relH) = PdfCoordinateMapper.ToRelativeOverlay(rect, pw, ph);

        relX.Should().BeApproximately(0, T);
        relY.Should().BeApproximately(0, T);
        relW.Should().BeApproximately(1, T);
        relH.Should().BeApproximately(1, T);
    }

    // ── ToRelativeOverlay: known positions ──────────────────────

    [Fact]
    public void ToRelativeOverlay_CenteredRect_ReturnsMidpoint()
    {
        // 200×100 centered on 600×800 page
        var rect = new PdfRectangle(200, 350, 400, 450);
        var (relX, relY, relW, relH) = PdfCoordinateMapper.ToRelativeOverlay(rect, 600, 800);

        relX.Should().BeApproximately(200.0 / 600, T);
        relY.Should().BeApproximately((800 - 450.0) / 800, T);
        relW.Should().BeApproximately(200.0 / 600, T);
        relH.Should().BeApproximately(100.0 / 800, T);
    }

    [Fact]
    public void ToRelativeOverlay_TopRightCorner()
    {
        var rect = new PdfRectangle(512, 742, 612, 792);
        var (relX, relY, _, _) = PdfCoordinateMapper.ToRelativeOverlay(rect, 612, 792);

        relX.Should().BeApproximately(512.0 / 612, T);
        relY.Should().BeApproximately(0, T); // top of page
    }

    [Fact]
    public void ToRelativeOverlay_BottomLeftCorner_FlipsY()
    {
        var rect = new PdfRectangle(0, 0, 100, 50);
        var (relX, relY, _, relH) = PdfCoordinateMapper.ToRelativeOverlay(rect, 612, 792);

        relX.Should().BeApproximately(0, T);
        relY.Should().BeApproximately((792 - 50.0) / 792, T);
        relH.Should().BeApproximately(50.0 / 792, T);
    }

    // ── ToRelativeOverlay: tiny rect ────────────────────────────

    [Fact]
    public void ToRelativeOverlay_TinyRect_ProducesSmallButPositiveValues()
    {
        var rect = new PdfRectangle(100, 100, 100.5, 100.5);
        var (_, _, relW, relH) = PdfCoordinateMapper.ToRelativeOverlay(rect, 612, 792);

        relW.Should().BeGreaterThan(0).And.BeLessThan(0.01);
        relH.Should().BeGreaterThan(0).And.BeLessThan(0.01);
    }

    // ── ToRelativeOverlay: guard clauses ────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 792)]
    [InlineData(612, -1)]
    [InlineData(0, 792)]
    [InlineData(612, 0)]
    public void ToRelativeOverlay_InvalidPageSize_ReturnsAllZeros(double pw, double ph)
    {
        var rect = new PdfRectangle(10, 10, 50, 50);
        var (relX, relY, relW, relH) = PdfCoordinateMapper.ToRelativeOverlay(rect, pw, ph);

        relX.Should().Be(0);
        relY.Should().Be(0);
        relW.Should().Be(0);
        relH.Should().Be(0);
    }

    // ── ToRelativeOverlay: clamping ─────────────────────────────

    [Fact]
    public void ToRelativeOverlay_RectExceedsPage_ClampedTo01()
    {
        var rect = new PdfRectangle(-50, -50, 700, 900);
        var (relX, relY, relW, relH) = PdfCoordinateMapper.ToRelativeOverlay(rect, 612, 792);

        relX.Should().BeInRange(0, 1);
        relY.Should().BeInRange(0, 1);
        relW.Should().BeInRange(0, 1);
        relH.Should().BeInRange(0, 1);
    }

    [Fact]
    public void ToRelativeOverlay_NegativeLeft_ClampedToZero()
    {
        var rect = new PdfRectangle(-10, 0, 100, 50);
        var (relX, _, _, _) = PdfCoordinateMapper.ToRelativeOverlay(rect, 612, 792);
        relX.Should().Be(0);
    }

    // ── ToRelativeOverlay: parameterized with known answers ─────

    [Theory]
    [InlineData(0,   0,   306, 396, 612, 792,  0,     0.5,   0.5,   0.5)]
    [InlineData(306, 396, 612, 792, 612, 792,  0.5,   0,     0.5,   0.5)]
    public void ToRelativeOverlay_ParameterizedKnownAnswers(
        double l, double b, double r, double t,
        double pw, double ph,
        double expX, double expY, double expW, double expH)
    {
        var rect = new PdfRectangle(l, b, r, t);
        var (relX, relY, relW, relH) = PdfCoordinateMapper.ToRelativeOverlay(rect, pw, ph);

        relX.Should().BeApproximately(expX, T);
        relY.Should().BeApproximately(expY, T);
        relW.Should().BeApproximately(expW, T);
        relH.Should().BeApproximately(expH, T);
    }

    // ── AdjustPositionForProportionalSize ───────────────────────

    [Theory]
    [InlineData(0,   0,   800, 600,   0,   0)]
    [InlineData(1,   1,   800, 600, 800, 600)]
    [InlineData(0.5, 0.25, 800, 600, 400, 150)]
    [InlineData(0.5, 0.5, 1024, 768, 512, 384)]
    public void AdjustPosition_Parameterized(
        double relX, double relY, double lw, double lh,
        double expX, double expY)
    {
        var pt = PdfCoordinateMapper.AdjustPositionForProportionalSize(relX, relY, lw, lh);

        pt.X.Should().BeApproximately(expX, T);
        pt.Y.Should().BeApproximately(expY, T);
    }

    [Fact]
    public void AdjustPosition_ZeroLayout_ReturnsOrigin()
    {
        var pt = PdfCoordinateMapper.AdjustPositionForProportionalSize(0.5, 0.5, 0, 0);
        pt.X.Should().Be(0);
        pt.Y.Should().Be(0);
    }
}
