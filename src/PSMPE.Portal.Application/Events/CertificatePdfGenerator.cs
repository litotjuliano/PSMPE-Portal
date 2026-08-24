using PSMPE.Portal.Application.Events.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PSMPE.Portal.Application.Events;

/// <summary>
/// Renders a certificate on demand - never cached, never pre-generated (see
/// add-events-cpd-tracker/proposal.md). Called once per download request from
/// EventsController.GetCertificate, so a unit value corrected after the fact is reflected the very
/// next time someone downloads.
/// </summary>
public static class CertificatePdfGenerator
{
    public static byte[] Generate(CertificateDataDto data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(14));

                page.Content().Column(column =>
                {
                    column.Item().AlignCenter().Text("Certificate of Completion").FontSize(28).Bold();
                    column.Item().PaddingTop(20).AlignCenter().Text($"This certifies that {data.MemberName}").FontSize(16);
                    column.Item().AlignCenter().Text($"attended {data.EventTitle}").FontSize(16);
                    column.Item().AlignCenter().Text(
                        $"{data.EventStartsAt:MMMM d, yyyy} - {data.EventEndsAt:MMMM d, yyyy} ({data.Mode})").FontSize(12);

                    column.Item().PaddingTop(20).Text("Sessions attended:").Bold();
                    foreach (var title in data.AttendedSessionTitles)
                    {
                        column.Item().Text($"- {title}");
                    }

                    column.Item().PaddingTop(20).AlignCenter().Text($"CPD Units Earned: {data.CreditUnits}").FontSize(16).Bold();
                });
            });
        });

        return document.GeneratePdf();
    }
}
