using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Application.Payments.Dtos;
using SkiaSharp;

namespace PSMPE.Portal.Application.Members;

/// <summary>
/// Renders a simple official-looking JPEG receipt once a membership application is approved
/// (MembersController.Approve) - system-generated, not a re-serve of the member's own uploaded
/// Proof of Payment. Fee amounts mirror the fixed schedule already shown in the application
/// wizard's Payment Details step (Membership Fee + Shipping Fee due now; Annual Dues deferred to
/// year two) - there's no per-member stored payment amount to draw from instead.
///
/// Requires an actual font to be installed wherever this runs (SkiaSharp on Linux needs
/// fontconfig + a real font file, unlike Windows dev machines which always have one) - see the
/// WebAPI Dockerfile's fontconfig/fonts-dejavu-core install.
/// </summary>
public static class ReceiptGenerator
{
    private const int Width = 1000;
    private const int Height = 1300;

    /// <param name="fees">PSMPE's configured fees. Passed in rather than read here so this stays a
    /// pure renderer with no database dependency - the caller resolves them from SystemConfig.</param>
    public static byte[] Generate(MemberDto member, MembershipFeesDto fees)
    {
        using var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var titlePaint = new SKPaint
        {
            Color = new SKColor(0x1E, 0x3A, 0x5F), TextSize = 42, IsAntialias = true, TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold),
        };
        using var subtitlePaint = new SKPaint { Color = SKColors.Gray, TextSize = 22, IsAntialias = true, TextAlign = SKTextAlign.Center };
        using var sectionPaint = new SKPaint
        {
            Color = SKColors.Black, TextSize = 26, IsAntialias = true, Typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold),
        };
        using var labelPaint = new SKPaint { Color = SKColors.Gray, TextSize = 24, IsAntialias = true };
        using var valuePaint = new SKPaint
        {
            Color = SKColors.Black, TextSize = 26, IsAntialias = true, TextAlign = SKTextAlign.Right,
            Typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold),
        };
        using var footerPaint = new SKPaint { Color = SKColors.Gray, TextSize = 18, IsAntialias = true, TextAlign = SKTextAlign.Center };
        using var linePaint = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD), StrokeWidth = 2 };

        const float marginX = 60;
        var y = 90f;

        canvas.DrawText("PSMPE", Width / 2f, y, titlePaint);
        y += 36;
        canvas.DrawText("Official Membership Receipt", Width / 2f, y, subtitlePaint);
        y += 50;
        canvas.DrawLine(marginX, y, Width - marginX, y, linePaint);
        y += 60;

        void DrawRow(string label, string value)
        {
            canvas.DrawText(label, marginX, y, labelPaint);
            canvas.DrawText(value, Width - marginX, y, valuePaint);
            y += 46;
        }

        // Approval always assigns one, so the fallback is defensive only - the DTO is nullable
        // because applicants awaiting approval have no number yet.
        DrawRow("Membership No.", member.MembershipNo ?? "-");
        DrawRow("Name", $"{member.FirstName} {member.LastName}");
        DrawRow("Chapter", member.Chapter);
        DrawRow("Member Type", member.MemberType);
        DrawRow("Date Approved", (member.ApprovedAt ?? DateTimeOffset.UtcNow).ToString("MMMM d, yyyy"));

        y += 20;
        canvas.DrawLine(marginX, y, Width - marginX, y, linePaint);
        y += 56;

        canvas.DrawText("Payment Summary", marginX, y, sectionPaint);
        y += 50;

        DrawRow("Membership Fee", $"₱{fees.MembershipFee:N2}");
        DrawRow("Shipping Fee", $"₱{fees.ShippingFee:N2}");
        y += 10;
        canvas.DrawLine(marginX, y, Width - marginX, y, linePaint);
        y += 46;
        DrawRow("Total Paid", $"₱{fees.RegistrationTotal:N2}");

        y += 40;
        canvas.DrawText($"Annual Dues of ₱{fees.AnnualDues:N2} are payable one year after registration.", marginX, y, labelPaint);

        canvas.DrawText($"Generated {DateTimeOffset.UtcNow:MMMM d, yyyy}", Width / 2f, Height - 40, footerPaint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
