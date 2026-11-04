using PDFtoImage;
using SkiaSharp;

namespace PSMPE.Portal.Application.Payments.Export;

public sealed record ProofPicture(byte[] Jpeg, int Width, int Height);

/// <summary>
/// Makes a smaller copy of a stored proof to embed in the export; the stored file is only read. Proofs are JPEG or PDF - see
/// MemberUploadService.StoreFileAsync. Only page 1 of a PDF is rendered.
/// </summary>
public static class PaymentProofPicture
{
    private const int MaxLongSide = 800;
    private const int JpegQuality = 70;
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
    /// Pixel size to rasterise a PDF page of the given size in points: 150 DPI at most, and never
    /// more than <see cref="MaxLongSide"/> on the long side. Stored PDFs are unvalidated, so the
    /// render size is capped by computing pixels from the page size, whatever the PDF declares.
    /// </summary>
    internal static (int Width, int Height) PdfPixelSizeFor(double pageWidthPoints, double pageHeightPoints)
    {
        var scale = Math.Min(PdfRenderDpi / 72d, MaxLongSide / Math.Max(pageWidthPoints, pageHeightPoints));
        var width = Math.Max(1, (int)Math.Round(pageWidthPoints * scale));
        var height = Math.Max(1, (int)Math.Round(pageHeightPoints * scale));
        return (width, height);
    }

    private static SKBitmap RenderPdfFirstPage(byte[] pdf)
    {
        var size = Conversion.GetPageSize(pdf, page: new Index(0));
        var (width, height) = PdfPixelSizeFor(size.Width, size.Height);
        return Conversion.ToImage(pdf, page: new Index(0), options: new RenderOptions(Width: width, Height: height));
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
