using PSMPE.Portal.Application.Payments.Export;
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
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public void TryCreate_CorruptFile_ReturnsNull(string contentType)
    {
        Assert.Null(Create([1, 2, 3, 4], contentType));
    }
}
