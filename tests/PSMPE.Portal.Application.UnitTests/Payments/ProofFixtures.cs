using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace PSMPE.Portal.Application.UnitTests.Payments;

internal static class ProofFixtures
{
    public static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    public static byte[] Pdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container => container.Page(page => page.Content().Text("Deposit slip"))).GeneratePdf();
    }
}
