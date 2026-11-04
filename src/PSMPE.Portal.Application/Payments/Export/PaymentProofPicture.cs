using PDFtoImage;
using SkiaSharp;

namespace PSMPE.Portal.Application.Payments.Export;

public sealed record ProofPicture(byte[] Jpeg, int Width, int Height);

/// <summary>
/// Turns a stored proof into the picture embedded in the export. Proofs are JPEG or PDF - see
/// MemberUploadService.StoreFileAsync. Only page 1 of a PDF is rendered.
/// </summary>
public static class PaymentProofPicture
{
    private const int MaxLongSide = 1000;
    private const int JpegQuality = 80;
    private const int PdfRenderDpi = 150;

    /// <summary>
    /// Null when the file can't be decoded, so one bad proof never fails the export. A missing
    /// native library is deliberately not caught: it means a broken deployment, and reporting
    /// every PDF as unreadable would hide that.
    /// </summary>
    public static ProofPicture? TryCreate(Stream content, string contentType)
    {
        try
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            var bytes = buffer.ToArray();

            using var bitmap = contentType == "application/pdf"
                ? RenderPdfFirstPage(bytes)
                : SKBitmap.Decode(bytes);

            return bitmap is null ? null : Encode(bitmap);
        }
        catch (Exception ex) when (ex is not DllNotFoundException and not TypeInitializationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Render DPI for a page of the given size in points: 150 normally, lowered so the long side
    /// comes out near <see cref="MaxLongSide"/> pixels. Stored PDFs are unvalidated, so without
    /// this a tiny file declaring a huge page would be rasterised at full size first.
    /// </summary>
    public static int PdfDpiFor(double pageWidthPoints, double pageHeightPoints)
    {
        var longestSide = Math.Max(pageWidthPoints, pageHeightPoints);
        if (longestSide <= 0)
        {
            return PdfRenderDpi;
        }

        var dpi = (int)Math.Ceiling(Math.Min(PdfRenderDpi, MaxLongSide * 72d / longestSide));
        return Math.Max(1, dpi);
    }

    private static SKBitmap RenderPdfFirstPage(byte[] pdf)
    {
        var size = Conversion.GetPageSize(pdf, page: new Index(0));
        return Conversion.ToImage(pdf, page: new Index(0), options: new RenderOptions(Dpi: PdfDpiFor(size.Width, size.Height)));
    }

    private static ProofPicture Encode(SKBitmap source)
    {
        var scale = Math.Min(1d, (double)MaxLongSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var target = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, new SKRect(0, 0, width, height), paint);
        }

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return new ProofPicture(data.ToArray(), width, height);
    }
}
