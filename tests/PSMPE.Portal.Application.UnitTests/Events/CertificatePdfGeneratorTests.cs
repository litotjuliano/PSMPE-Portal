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
            "Onsite", ["Day 1: Opening", "Day 1: Cross-Connection Control"], 4m);

        var bytes = CertificatePdfGenerator.Generate(data);

        Assert.NotEmpty(bytes);
        // %PDF is the standard PDF magic number - a cheap sanity check that QuestPDF actually
        // produced a PDF and not, say, an exception swallowed somewhere.
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
