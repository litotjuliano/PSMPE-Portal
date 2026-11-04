using ClosedXML.Excel;
using Moq;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Payments.Export;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Payments;

public class PaymentExportServiceTests
{
    private const string ImageKey = "dela-cruz-juan-19800101-proof.jpg";
    private const string PdfKey = "proof.pdf";
    private const string CorruptKey = "corrupt.jpg";
    private const string GoneKey = "gone.jpg";

    private static IMemberUploadService Uploads()
    {
        var files = new Dictionary<string, (byte[] Bytes, string ContentType)>
        {
            [ImageKey] = (ProofFixtures.Jpeg(400, 300), "image/jpeg"),
            [PdfKey] = (ProofFixtures.Pdf(), "application/pdf"),
            [CorruptKey] = ([1, 2, 3], "image/jpeg"),
        };

        var uploads = new Mock<IMemberUploadService>();
        uploads.Setup(u => u.OpenByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken _) =>
            {
                (Stream Content, string ContentType)? file = null;
                if (files.TryGetValue(key, out var found))
                {
                    file = (new MemoryStream(found.Bytes), found.ContentType);
                }

                return Task.FromResult(file);
            });
        return uploads.Object;
    }

    private static async Task<Member> SeedMemberAsync(TestDbContext db, Action<Member>? configure = null)
    {
        var user = new ApplicationUser { UserName = "juan@example.com", Email = "juan@example.com" };
        db.Add(user);
        var member = new Member
        {
            UserId = user.Id,
            User = user,
            FirstName = "Juan",
            LastName = "Dela Cruz",
            ChapterId = TestChapters.Ncr,
            MemberType = MemberTypes.Regular,
            Status = MembershipStatus.Pending,
        };
        configure?.Invoke(member);
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    private static async Task<Payment> SeedPaymentAsync(
        TestDbContext db, Member member, string? proofKey = null, PaymentStatus status = PaymentStatus.Submitted,
        PaymentKind kind = PaymentKind.NewMembership, DateTimeOffset? createdAt = null, EventRegistration? registration = null)
    {
        var payment = new Payment
        {
            MemberId = member.Id,
            Kind = kind,
            Amount = 1700m,
            PaidOn = new DateOnly(2026, 8, 29),
            ProofStorageKey = proofKey,
            Status = status,
            EventRegistrationId = registration?.Id,
        };
        if (createdAt is not null)
        {
            payment.CreatedAt = createdAt.Value;
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private static async Task<(IXLWorksheet Sheet, int RowCount)> ExportAsync(TestDbContext db)
    {
        var export = await new PaymentExportService(db, Uploads()).BuildQueueExportAsync();
        return (new XLWorkbook(new MemoryStream(export.Content)).Worksheet(1), export.RowCount);
    }

    [Fact]
    public async Task Export_IncludesOnlySubmittedPayments()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db);
        await SeedPaymentAsync(db, member, status: PaymentStatus.Submitted);
        await SeedPaymentAsync(db, member, status: PaymentStatus.Verified);
        await SeedPaymentAsync(db, member, status: PaymentStatus.Rejected);

        var (_, rowCount) = await ExportAsync(db);

        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task Export_IsNotLimitedToTheQueuePageSize()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db);
        for (var i = 0; i < 25; i++)
        {
            await SeedPaymentAsync(db, member);
        }

        var (sheet, rowCount) = await ExportAsync(db);

        Assert.Equal(25, rowCount);
        Assert.Equal("Juan Dela Cruz", sheet.Cell(26, 1).GetString());
    }

    [Fact]
    public async Task Export_OrdersOldestFirst()
    {
        using var db = TestDbContext.CreateInMemory();
        var newer = await SeedMemberAsync(db, m => m.FirstName = "Newer");
        var older = await SeedMemberAsync(db, m => m.FirstName = "Older");
        await SeedPaymentAsync(db, newer, createdAt: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        await SeedPaymentAsync(db, older, createdAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        var (sheet, _) = await ExportAsync(db);

        Assert.StartsWith("Older", sheet.Cell(2, 1).GetString());
        Assert.StartsWith("Newer", sheet.Cell(3, 1).GetString());
    }

    [Fact]
    public async Task Export_FormatsFullNameAddressContactDetails()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db, m =>
        {
            m.MiddleName = "Santos";
            m.Suffix = "Jr.";
            m.MobileNumber = "09170000001";
            m.HouseNo = "12";
            m.Street = "Sample St.";
            m.Barangay = "Brgy. Example";
            m.CityMunicipality = "Sample City";
            m.Province = "Sample Province";
            m.ZipCode = "1000";
            m.Country = "Philippines";
        });
        await SeedPaymentAsync(db, member);

        var (sheet, _) = await ExportAsync(db);

        Assert.Equal("Juan Santos Dela Cruz Jr.", sheet.Cell(2, 1).GetString());
        Assert.Equal("juan@example.com", sheet.Cell(2, 2).GetString());
        Assert.Equal(
            "12 Sample St., Brgy. Example, Sample City, Sample Province 1000, Philippines",
            sheet.Cell(2, 3).GetString());
        Assert.Equal("09170000001", sheet.Cell(2, 4).GetString());
    }

    [Fact]
    public async Task Export_SkipsBlankAddressParts()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db, m =>
        {
            m.Barangay = "Brgy. Example";
            m.Province = "Sample Province";
        });
        await SeedPaymentAsync(db, member);

        var (sheet, _) = await ExportAsync(db);

        Assert.Equal("Brgy. Example, Sample Province", sheet.Cell(2, 3).GetString());
    }

    [Theory]
    [InlineData(PaymentKind.NewMembership, "New membership")]
    [InlineData(PaymentKind.Renewal, "Renewal")]
    [InlineData(PaymentKind.PortalAccessOnly, "Portal access")]
    public async Task Export_LabelsPaymentKind(PaymentKind kind, string label)
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db);
        await SeedPaymentAsync(db, member, kind: kind);

        var (sheet, _) = await ExportAsync(db);

        Assert.Equal(label, sheet.Cell(2, 9).GetString());
    }

    [Fact]
    public async Task Export_EventPaymentShowsTheEventTitle()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db);
        var @event = new Event { Title = "Seminar", StartsAt = DateTimeOffset.UtcNow.AddDays(5), EndsAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(4), FeeOnsite = 500m, FeeOnline = 200m };
        db.Events.Add(@event);
        var registration = new EventRegistration { EventId = @event.Id, Event = @event, MemberId = member.Id, Member = member, Mode = EventMode.Onsite };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync();
        await SeedPaymentAsync(db, member, kind: PaymentKind.EventRegistration, registration: registration);

        var (sheet, _) = await ExportAsync(db);

        Assert.Equal("Event registration — Seminar", sheet.Cell(2, 9).GetString());
    }

    [Fact]
    public async Task Export_HandlesEveryProofOutcomeWithoutFailing()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db);
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedPaymentAsync(db, member, ImageKey, createdAt: start);
        await SeedPaymentAsync(db, member, PdfKey, createdAt: start.AddDays(1));
        await SeedPaymentAsync(db, member, proofKey: null, createdAt: start.AddDays(2));
        await SeedPaymentAsync(db, member, GoneKey, createdAt: start.AddDays(3));
        await SeedPaymentAsync(db, member, CorruptKey, createdAt: start.AddDays(4));

        var (sheet, rowCount) = await ExportAsync(db);

        Assert.Equal(5, rowCount);
        Assert.Equal(2, sheet.Pictures.Count);
        Assert.Equal("No proof on file", sheet.Cell(4, 8).GetString());
        Assert.Equal("File missing – ask member to resubmit", sheet.Cell(5, 8).GetString());
        Assert.Equal("Could not read file – open it in the portal", sheet.Cell(6, 8).GetString());
    }

    [Fact]
    public async Task Export_NeverWritesTheStorageKey()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedMemberAsync(db);
        await SeedPaymentAsync(db, member, ImageKey);

        var (sheet, _) = await ExportAsync(db);

        Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.GetString().Contains(ImageKey));
    }
}
