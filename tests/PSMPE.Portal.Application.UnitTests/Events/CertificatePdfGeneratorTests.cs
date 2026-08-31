using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class CertificatePdfGeneratorTests
{
    // QuestPDF's Community license must be set once before any rendering call, or GeneratePdf()
    // throws at runtime. Program.cs sets this for the WebAPI process, but this test project never
    // runs Program.cs, so it needs its own one-time set. A static constructor runs exactly once
    // per test-class instantiation in this process - there's no existing assembly-level test setup
    // convention in this project to hook into instead.
    static CertificatePdfGeneratorTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    [Fact]
    public void Generate_ProducesNonEmptyPdfBytes()
    {
        var data = new CertificateDataDto(
            "Juan Dela Cruz", "Water Sanitation Workshop",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            "Onsite", ["Day 1: Opening", "Day 1: Cross-Connection Control"], 4m,
            "Seminar", 8m, "PRC-CPD-2026-001");

        var bytes = CertificatePdfGenerator.Generate(data);

        Assert.NotEmpty(bytes);
        // %PDF is the standard PDF magic number - a cheap sanity check that QuestPDF actually
        // produced a PDF and not, say, an exception swallowed somewhere.
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Generate_ProducesNonEmptyPdfBytes_WhenOptionalFieldsAreNull()
    {
        // EventType/Hours/CpdCode are all independently nullable (Event.Type/Event.Hours/
        // Event.CpdCodeOnsite/Event.CpdCodeOnline are all optional columns), so the generator must
        // not throw when none of them were ever set.
        var data = new CertificateDataDto(
            "Juan Dela Cruz", "Water Sanitation Workshop",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            "Online", ["Day 1: Opening"], 4m,
            null, null, null);

        var bytes = CertificatePdfGenerator.Generate(data);

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
