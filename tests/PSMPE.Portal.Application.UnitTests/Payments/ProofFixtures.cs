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

    /// <summary>A minimal hand-written one-page PDF (no xref table) declaring the given page size.</summary>
    public static byte[] PdfWithPageSize(int widthPoints, int heightPoints)
    {
        var pdf = "%PDF-1.4\n" +
                  "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
                  "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
                  $"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 {widthPoints} {heightPoints}]>>endobj\n" +
                  "trailer<</Root 1 0 R>>\n%%EOF";
        return System.Text.Encoding.ASCII.GetBytes(pdf);
    }
}
