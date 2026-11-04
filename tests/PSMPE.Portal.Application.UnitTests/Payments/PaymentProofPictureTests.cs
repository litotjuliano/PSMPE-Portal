using PSMPE.Portal.Application.Payments.Export;
using SkiaSharp;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Payments;

public class PaymentProofPictureTests
{
    private static ProofPicture? Create(byte[] bytes, string contentType) =>
        PaymentProofPicture.TryCreate(new MemoryStream(bytes), contentType);

    [Fact]
    public void TryCreate_LargeImage_ShrinksLongSideTo1000AndKeepsShape()
    {
        var picture = Create(ProofFixtures.Jpeg(1600, 800), "image/jpeg");

        Assert.NotNull(picture);
        Assert.Equal(1000, picture.Width);
        Assert.Equal(500, picture.Height);
        Assert.NotEmpty(picture.Jpeg);
    }

    [Fact]
    public void TryCreate_SmallImage_IsNotEnlarged()
    {
        var picture = Create(ProofFixtures.Jpeg(300, 200), "image/jpeg");

        Assert.NotNull(picture);
        Assert.Equal(300, picture.Width);
        Assert.Equal(200, picture.Height);
    }

    [Fact]
    public void TryCreate_Pdf_RendersFirstPageToAPicture()
    {
        var picture = Create(ProofFixtures.Pdf(), "application/pdf");

        Assert.NotNull(picture);
        Assert.Equal(1000, Math.Max(picture.Width, picture.Height));
        Assert.InRange(Math.Min(picture.Width, picture.Height), 700, 714);

        using var decoded = SKBitmap.Decode(picture.Jpeg);
        var hasInk = false;
        for (var x = 0; x < decoded.Width && !hasInk; x++)
        for (var y = 0; y < decoded.Height && !hasInk; y++)
        {
            var c = decoded.GetPixel(x, y);
            hasInk = c.Red < 200 || c.Green < 200 || c.Blue < 200;
        }

        Assert.True(hasInk, "rendered page is blank");
    }

    [Fact]
    public void TryCreate_HugePdfPage_IsRenderedAtBoundedResolution()
    {
        var picture = Create(ProofFixtures.PdfWithPageSize(14400, 7200), "application/pdf");

        Assert.NotNull(picture);
        Assert.Equal(1000, picture.Width);
        Assert.Equal(500, picture.Height);
    }

    [Theory]
    [InlineData(612, 612, 118)]
    [InlineData(14400, 7200, 5)]
    [InlineData(100, 100, 150)]
    [InlineData(0, 0, 150)]
    public void PdfDpiFor_ScalesDownLargePagesOnly(double width, double height, int expected)
    {
        Assert.Equal(expected, PaymentProofPicture.PdfDpiFor(width, height));
    }

    [Fact]
    public void PdfDpiFor_A4_StaysUnderMaxAndStillReaches1000()
    {
        var dpi = PaymentProofPicture.PdfDpiFor(595, 842);

        Assert.InRange(dpi, 1, 149);
        Assert.True(842 * dpi / 72d >= 1000);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public void TryCreate_CorruptFile_ReturnsNull(string contentType)
    {
        Assert.Null(Create([1, 2, 3, 4], contentType));
    }
}
