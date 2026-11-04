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
    // The memory bound is guarded by the PdfPixelSizeFor tests below; Encode gives this size regardless.
    [Fact]
    public void TryCreate_HugePdfPage_ReturnsResizedPicture()
    {
        var picture = Create(ProofFixtures.PdfWithPageSize(1000000, 500000), "application/pdf");

        Assert.NotNull(picture);
        Assert.Equal(1000, picture.Width);
        Assert.Equal(500, picture.Height);
    }

    [Theory]
    [InlineData(1000000, 1000000, 1000, 1000)]
    [InlineData(1600000, 800000, 1000, 500)]
    [InlineData(14400, 7200, 1000, 500)]
    [InlineData(595, 842, 707, 1000)]
    [InlineData(300, 200, 625, 417)]
    [InlineData(0, 0, 1, 1)]
    public void PdfPixelSizeFor_CapsLongSideAndNeverExceeds150Dpi(double w, double h, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), PaymentProofPicture.PdfPixelSizeFor(w, h));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public void TryCreate_CorruptFile_ReturnsNull(string contentType)
    {
        Assert.Null(Create([1, 2, 3, 4], contentType));
    }
}
