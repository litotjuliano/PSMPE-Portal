# Payments Queue Excel Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Staff can download every payment awaiting verification as an `.xlsx`, with each proof embedded as a picture.

**Architecture:** `GET /api/payments/export` calls `PaymentExportService`, which loads the `Submitted` payments, turns each proof into a resized JPEG with `PaymentProofPicture`, and hands plain rows to the pure renderer `PaymentExportWorkbook`. The controller records an audit event and returns the bytes. The queue card gets a button that downloads the blob.

**Tech Stack:** .NET 8, EF Core, ClosedXML 0.105.1, PDFtoImage 4.1.0, SkiaSharp 2.88.8, xUnit, Moq, React, axios.

**Spec:** [proposal.md](proposal.md). Commit to `develop`. If `dotnet build` or `dotnet test` reports a file lock, a local dev server is running: ask the user before killing it.

## File structure

| File | Responsibility |
|---|---|
| Create `src/PSMPE.Portal.Application/Payments/Export/PaymentProofPicture.cs` | Proof stream to resized JPEG, or null if unreadable |
| Create `src/PSMPE.Portal.Application/Payments/Export/PaymentExportWorkbook.cs` | Rows to xlsx bytes. No database or storage |
| Create `src/PSMPE.Portal.Application/Payments/Export/PaymentExportService.cs` | Loads rows and proofs, formats values. Holds `IPaymentExportService` |
| Modify `src/PSMPE.Portal.Application/DependencyInjection.cs` | Register the service |
| Modify `src/PSMPE.Portal.WebAPI/Controllers/PaymentsController.cs` | Endpoint and audit |
| Modify `apps/web/src/core/api/endpoints/paymentApi.ts` | `exportQueue()` |
| Modify `apps/web/src/integrations/template/pages/PaymentsQueueTable.tsx` | Button, download, error |
| Modify `openspecs/payments.md`, `openspecs/system-logs.md` | Docs |

---

### Task 1: Add the packages

**Files:**
- Modify: `src/PSMPE.Portal.Application/PSMPE.Portal.Application.csproj`

- [ ] **Step 1: Add both references** next to the existing `SkiaSharp` lines

```xml
<PackageReference Include="ClosedXML" Version="0.105.1" />
<PackageReference Include="PDFtoImage" Version="4.1.0" />
```

- [ ] **Step 2: Build and confirm SkiaSharp did not move**

Run: `dotnet build src/PSMPE.Portal.Application && dotnet list src/PSMPE.Portal.Application package --include-transitive | grep -iE "skiasharp|pdfium|closedxml"`
Expected: build succeeds; `SkiaSharp` resolves to `2.88.8`. If it resolves higher, stop and report it, because that breaks the reason for the pin.

- [ ] **Step 3: Commit**

```bash
git add src/PSMPE.Portal.Application/PSMPE.Portal.Application.csproj
git commit -m "chore: add ClosedXML and PDFtoImage for the payments export"
```

---

### Task 2: PaymentProofPicture

**Files:**
- Create: `src/PSMPE.Portal.Application/Payments/Export/PaymentProofPicture.cs`
- Create: `tests/PSMPE.Portal.Application.UnitTests/Payments/ProofFixtures.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentProofPictureTests.cs`

- [ ] **Step 1: Write the fixtures**

```csharp
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
```

- [ ] **Step 2: Write the failing tests**

```csharp
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
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter "FullyQualifiedName~PaymentProofPictureTests"`
Expected: FAIL to compile, `PaymentProofPicture` does not exist.

- [ ] **Step 4: Implement**

```csharp
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
                ? Conversion.ToImage(bytes, page: new Index(0), options: new RenderOptions(Dpi: PdfRenderDpi))
                : SKBitmap.Decode(bytes);

            return bitmap is null ? null : Encode(bitmap);
        }
        catch (Exception ex) when (ex is not DllNotFoundException and not TypeInitializationException)
        {
            return null;
        }
    }

    private static ProofPicture Encode(SKBitmap source)
    {
        var scale = Math.Min(1d, (double)MaxLongSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var target = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, new SKRect(0, 0, width, height), paint);
        }

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return new ProofPicture(data.ToArray(), width, height);
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter "FullyQualifiedName~PaymentProofPictureTests"`
Expected: PASS, 5 tests. If `Conversion.ToImage` reports an ambiguous call, keep the named `page:` and `options:` arguments and cast the first to `byte[]`. A CA1416 platform warning is acceptable.

- [ ] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.Application/Payments/Export tests/PSMPE.Portal.Application.UnitTests/Payments
git commit -m "feat: turn payment proofs into export pictures, including PDF page 1"
```

---

### Task 3: PaymentExportWorkbook

**Files:**
- Create: `src/PSMPE.Portal.Application/Payments/Export/PaymentExportWorkbook.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentExportWorkbookTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClosedXML.Excel;
using PSMPE.Portal.Application.Payments.Export;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Payments;

public class PaymentExportWorkbookTests
{
    private static PaymentExportRow Row(
        string fullName = "Juan Dela Cruz", string? mobile = "09170000001", ProofPicture? picture = null, string? note = "No proof on file") =>
        new(fullName, "juan@example.com", "12 Sample St., Sample City", mobile, 1700m, "REF-1",
            new DateOnly(2026, 8, 29), picture, note, "New membership");

    private static IXLWorksheet Open(params PaymentExportRow[] rows) =>
        new XLWorkbook(new MemoryStream(PaymentExportWorkbook.Build(rows))).Worksheet("Awaiting Verification");

    [Fact]
    public void Build_WritesHeadersInTheAgreedOrderAndFreezesThem()
    {
        var sheet = Open();

        var headers = Enumerable.Range(1, 9).Select(c => sheet.Cell(1, c).GetString());
        Assert.Equal(
            ["Full Name", "Email", "Address", "CP Number", "Amount", "Reference", "Paid On", "Proof of Payment", "For"],
            headers);
        Assert.Equal(1, sheet.SheetView.SplitRow);
    }

    [Fact]
    public void Build_WritesEachValueToItsColumnWithTheRightType()
    {
        var sheet = Open(Row());

        Assert.Equal("Juan Dela Cruz", sheet.Cell(2, 1).GetString());
        Assert.Equal("juan@example.com", sheet.Cell(2, 2).GetString());
        Assert.Equal("12 Sample St., Sample City", sheet.Cell(2, 3).GetString());
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 5).DataType);
        Assert.Equal(1700d, sheet.Cell(2, 5).GetDouble());
        Assert.Equal("REF-1", sheet.Cell(2, 6).GetString());
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 7).DataType);
        Assert.Equal(new DateTime(2026, 8, 29), sheet.Cell(2, 7).GetDateTime());
        Assert.Equal("New membership", sheet.Cell(2, 9).GetString());
    }

    [Theory]
    [InlineData("09170000001")]
    [InlineData("+639170000001")]
    public void Build_KeepsMobileNumbersAsText(string mobile)
    {
        var cell = Open(Row(mobile: mobile)).Cell(2, 4);

        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(mobile, cell.GetString());
    }

    [Fact]
    public void Build_NeverTurnsMemberTextIntoAFormula()
    {
        var cell = Open(Row(fullName: "=1+1")).Cell(2, 1);

        Assert.False(cell.HasFormula);
        Assert.Equal("=1+1", cell.GetString());
    }

    [Fact]
    public void Build_EmbedsPictureInTheProofColumnOfItsRow()
    {
        var picture = new ProofPicture(ProofFixtures.Jpeg(1000, 500), 1000, 500);

        var sheet = Open(Row(), Row(picture: picture, note: null));

        var embedded = Assert.Single(sheet.Pictures);
        Assert.Equal(3, embedded.TopLeftCell.Address.RowNumber);
        Assert.Equal(8, embedded.TopLeftCell.Address.ColumnNumber);
        Assert.Equal("No proof on file", sheet.Cell(2, 8).GetString());
        Assert.Equal(string.Empty, sheet.Cell(3, 8).GetString());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter "FullyQualifiedName~PaymentExportWorkbookTests"`
Expected: FAIL to compile, `PaymentExportWorkbook` does not exist.

- [ ] **Step 3: Implement**

```csharp
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;

namespace PSMPE.Portal.Application.Payments.Export;

/// <summary>One sheet row. Picture and ProofNote are alternatives: a note explains a missing picture.</summary>
public sealed record PaymentExportRow(
    string FullName, string? Email, string Address, string? MobileNumber, decimal Amount,
    string? ReferenceNo, DateOnly PaidOn, ProofPicture? Picture, string? ProofNote, string For);

/// <summary>
/// Pure renderer from rows to xlsx bytes, with no database or storage dependency, like
/// ReceiptGenerator. Text always goes in as a cell value, never as a formula, so member-entered
/// text starting with "=" can't execute in Excel.
/// </summary>
public static class PaymentExportWorkbook
{
    private const string SheetName = "Awaiting Verification";
    private const string HeaderColor = "#0E4C92";
    private const int ProofColumn = 8;
    private const double DataRowHeightPoints = 90;
    private const int PictureBoxWidthPx = 160;
    private const int PictureBoxHeightPx = 110;
    private const int PictureOffsetPx = 4;

    private static readonly (string Header, double Width)[] Columns =
    [
        ("Full Name", 26), ("Email", 30), ("Address", 40), ("CP Number", 16), ("Amount", 13),
        ("Reference", 16), ("Paid On", 12), ("Proof of Payment", 25), ("For", 26),
    ];

    public static byte[] Build(IReadOnlyList<PaymentExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        WriteHeader(sheet);
        for (var index = 0; index < rows.Count; index++)
        {
            WriteRow(sheet, index + 2, rows[index]);
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static void WriteHeader(IXLWorksheet sheet)
    {
        for (var column = 1; column <= Columns.Length; column++)
        {
            var cell = sheet.Cell(1, column);
            cell.Value = Columns[column - 1].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderColor);
            sheet.Column(column).Width = Columns[column - 1].Width;
        }

        sheet.SheetView.FreezeRows(1);
    }

    private static void WriteRow(IXLWorksheet sheet, int rowNumber, PaymentExportRow row)
    {
        var sheetRow = sheet.Row(rowNumber);
        sheetRow.Height = DataRowHeightPoints;
        sheetRow.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheetRow.Style.Alignment.WrapText = true;

        sheet.Cell(rowNumber, 1).Value = row.FullName;
        sheet.Cell(rowNumber, 2).Value = row.Email ?? string.Empty;
        sheet.Cell(rowNumber, 3).Value = row.Address;

        var mobile = sheet.Cell(rowNumber, 4);
        mobile.Style.NumberFormat.Format = "@";
        mobile.Value = row.MobileNumber ?? string.Empty;

        var amount = sheet.Cell(rowNumber, 5);
        amount.Value = row.Amount;
        amount.Style.NumberFormat.Format = "\"₱\"#,##0.00";

        sheet.Cell(rowNumber, 6).Value = row.ReferenceNo ?? string.Empty;

        var paidOn = sheet.Cell(rowNumber, 7);
        paidOn.Value = row.PaidOn.ToDateTime(TimeOnly.MinValue);
        paidOn.Style.NumberFormat.Format = "mm/dd/yyyy";

        sheet.Cell(rowNumber, 9).Value = row.For;

        if (row.Picture is not null)
        {
            AddPicture(sheet, rowNumber, row.Picture);
        }
        else
        {
            sheet.Cell(rowNumber, ProofColumn).Value = row.ProofNote ?? string.Empty;
        }
    }

    private static void AddPicture(IXLWorksheet sheet, int rowNumber, ProofPicture picture)
    {
        var scale = Math.Min((double)PictureBoxWidthPx / picture.Width, (double)PictureBoxHeightPx / picture.Height);

        sheet.AddPicture(new MemoryStream(picture.Jpeg), XLPictureFormat.Jpeg, $"Proof{rowNumber}")
            .MoveTo(sheet.Cell(rowNumber, ProofColumn), PictureOffsetPx, PictureOffsetPx)
            .WithSize((int)(picture.Width * scale), (int)(picture.Height * scale));
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter "FullyQualifiedName~PaymentExportWorkbookTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.Application/Payments/Export/PaymentExportWorkbook.cs tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentExportWorkbookTests.cs
git commit -m "feat: render the payments export workbook"
```

---

### Task 4: PaymentExportService

**Files:**
- Create: `src/PSMPE.Portal.Application/Payments/Export/PaymentExportService.cs`
- Modify: `src/PSMPE.Portal.Application/DependencyInjection.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentExportServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter "FullyQualifiedName~PaymentExportServiceTests"`
Expected: FAIL to compile, `PaymentExportService` does not exist.

- [ ] **Step 3: Implement**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments.Export;

public sealed record PaymentExport(byte[] Content, int RowCount);

public interface IPaymentExportService
{
    /// <summary>Every payment awaiting verification, oldest first, as an xlsx workbook.</summary>
    Task<PaymentExport> BuildQueueExportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the Excel export of the payments queue. Proof storage keys stay inside this class: they
/// embed the member's name and birthdate. See openspecs/payments.md.
/// </summary>
public class PaymentExportService(IApplicationDbContext db, IMemberUploadService uploads) : IPaymentExportService
{
    private const string NoProofNote = "No proof on file";
    private const string MissingFileNote = "File missing – ask member to resubmit";
    private const string UnreadableFileNote = "Could not read file – open it in the portal";

    public async Task<PaymentExport> BuildQueueExportAsync(CancellationToken cancellationToken = default)
    {
        var payments = await db.Payments.AsNoTracking()
            .Include(p => p.Member).ThenInclude(m => m.User)
            .Include(p => p.EventRegistration).ThenInclude(er => er!.Event)
            .Where(p => p.Status == PaymentStatus.Submitted)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var rows = new List<PaymentExportRow>(payments.Count);
        foreach (var payment in payments)
        {
            var (picture, note) = await LoadProofAsync(payment.ProofStorageKey, cancellationToken);
            rows.Add(new PaymentExportRow(
                FullName(payment.Member), payment.Member.User.Email, Address(payment.Member),
                payment.Member.MobileNumber, payment.Amount, payment.ReferenceNo, payment.PaidOn,
                picture, note, ForLabel(payment)));
        }

        return new PaymentExport(PaymentExportWorkbook.Build(rows), rows.Count);
    }

    private async Task<(ProofPicture? Picture, string? Note)> LoadProofAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (storageKey is null)
        {
            return (null, NoProofNote);
        }

        var file = await uploads.OpenByKeyAsync(storageKey, cancellationToken);
        if (file is null)
        {
            return (null, MissingFileNote);
        }

        await using var content = file.Value.Content;
        var picture = PaymentProofPicture.TryCreate(content, file.Value.ContentType);
        return picture is null ? (null, UnreadableFileNote) : (picture, null);
    }

    private static string FullName(Member member) =>
        Join(" ", member.FirstName, member.MiddleName, member.LastName, member.Suffix);

    private static string Address(Member member) =>
        Join(", ",
            Join(" ", member.HouseNo, member.Street),
            member.Barangay,
            member.CityMunicipality,
            Join(" ", member.Province, member.ZipCode),
            member.Country);

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    // Same labels as KIND_LABELS in PaymentsQueueTable.tsx.
    private static string ForLabel(Payment payment) => payment.Kind switch
    {
        PaymentKind.NewMembership => "New membership",
        PaymentKind.Renewal => "Renewal",
        PaymentKind.PortalAccessOnly => "Portal access",
        PaymentKind.EventRegistration when payment.EventRegistration is not null =>
            $"Event registration — {payment.EventRegistration.Event.Title}",
        PaymentKind.EventRegistration => "Event registration",
        _ => payment.Kind.ToString(),
    };
}
```

- [ ] **Step 4: Register it** in `src/PSMPE.Portal.Application/DependencyInjection.cs`

Add `using PSMPE.Portal.Application.Payments.Export;` and, after the `IPaymentService` line:

```csharp
services.AddScoped<IPaymentExportService, PaymentExportService>();
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter "FullyQualifiedName~PaymentExport"`
Expected: PASS for the service and workbook classes. If `SeedMemberAsync` throws a duplicate-user error in `Export_OrdersOldestFirst`, give the user a unique `UserName` by adding a `Guid` and keep `Email` as is.

- [ ] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.Application tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentExportServiceTests.cs
git commit -m "feat: build the payments queue export"
```

---

### Task 5: Endpoint and audit

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/PaymentsController.cs:1-24` and before `reports/summary`
- Modify: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Payments/PaymentsControllerTests.cs:53`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Payments/PaymentsExportTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Payments;

public class PaymentsExportTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string Url = "/api/payments/export";
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PaymentsExportTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid UserId, string Token)> UserAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await _client.CreatePrivilegedUserAsync(userManager, role);
    }

    private Task<HttpResponseMessage> GetAsync(string token) =>
        _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url).WithBearer(token));

    [Fact]
    public async Task Export_Anonymous_Returns401()
    {
        var response = await _client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_WithoutMembersView_Returns403()
    {
        var (_, token) = await UserAsync(RoleNames.Member);

        var response = await GetAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_WithMembersView_ReturnsWorkbookAndRecordsAudit()
    {
        var (userId, token) = await UserAsync(RoleNames.Approval);

        var response = await GetAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Full Name", workbook.Worksheet(1).Cell(1, 1).GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.EventType == "payments.exported" && a.ActorUserId == userId);
        Assert.Contains("\"rows\":", audit.Metadata);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter "FullyQualifiedName~PaymentsExportTests"`
Expected: the `WithMembersView` test FAILS with 404 or 405. The route falls through to no match.

- [ ] **Step 3: Implement the endpoint**

In `PaymentsController.cs` add these usings:

```csharp
using System.Text.Json;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Payments.Export;
```

Replace the constructor parameters:

```csharp
public class PaymentsController(
    IPaymentService paymentService,
    IMemberService memberService,
    IMemberUploadService memberUploadService,
    IPaymentExportService paymentExportService,
    IAuditLogService auditLogService) : ControllerBase
```

Add above the `reports/summary` action:

```csharp
    /// <summary>The queue as an Excel workbook with proofs embedded. members:view, the same access
    /// the queue and GET /{id}/proof already give. Audited because the file carries personal data
    /// out of the portal.</summary>
    [HttpGet("export")]
    [RequirePermission(Permissions.Members.View)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var export = await paymentExportService.BuildQueueExportAsync(cancellationToken);

        await auditLogService.RecordAsync(
            "payments.exported", CurrentUserId, HttpContext.Connection.RemoteIpAddress?.ToString(),
            targetType: null, targetId: null, JsonSerializer.Serialize(new { rows = export.RowCount }), cancellationToken);

        return File(
            export.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "PSMPE-Payments-Awaiting-Verification.xlsx");
    }
```

- [ ] **Step 4: Fix the existing test's constructor call** at `PaymentsControllerTests.cs:53`

```csharp
return new PaymentsController(
    _paymentService, _memberService, _memberUploadService,
    _scope.ServiceProvider.GetRequiredService<PSMPE.Portal.Application.Payments.Export.IPaymentExportService>(),
    _scope.ServiceProvider.GetRequiredService<PSMPE.Portal.Application.Common.Interfaces.IAuditLogService>())
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter "FullyQualifiedName~Payments"`
Expected: PASS, including the existing `PaymentsControllerTests`.

- [ ] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Controllers/PaymentsController.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/Payments
git commit -m "feat: GET /api/payments/export with audit entry"
```

---

### Task 6: Export button

**Files:**
- Modify: `apps/web/src/core/api/endpoints/paymentApi.ts` (after `fetchProofUrl`)
- Modify: `apps/web/src/integrations/template/pages/PaymentsQueueTable.tsx`

The web app has no unit test runner, so this task is checked by type-check, lint and the browser run in Task 8.

- [ ] **Step 1: Add the API call** after `fetchProofUrl` in `paymentApi.ts`

```ts
  /** Fetched as a blob so the request carries the bearer token, same as fetchProofUrl. */
  exportQueue: async (): Promise<Blob> => {
    const response = await apiClient.get('/api/payments/export', { responseType: 'blob' })
    return response.data
  },
```

- [ ] **Step 2: Update imports** in `PaymentsQueueTable.tsx`

```tsx
import { LuCheck, LuDownload, LuEye, LuTriangleAlert, LuX } from 'react-icons/lu'
import { describeError } from '../../../core/utils/apiError'
```

- [ ] **Step 3: Add state and the handler** after the `fees` state and `handleReject`

```tsx
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const handleExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      const blob = await paymentApi.exportQueue()
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      // en-CA formats as YYYY-MM-DD in the viewer's local time zone.
      link.download = `PSMPE-Payments-Awaiting-Verification-${new Date().toLocaleDateString('en-CA')}.xlsx`
      link.click()
      URL.revokeObjectURL(url)
    } catch (err) {
      setExportError(describeError(err, 'Could not export the queue. Please try again.'))
    } finally {
      setExporting(false)
    }
  }
```

- [ ] **Step 4: Replace the card header** at `PaymentsQueueTable.tsx:83-85`

```tsx
      <div className="card-header flex items-center justify-between gap-3">
        <h6 className="card-title">Payments Awaiting Verification</h6>
        <StandardButton
          variant="on-primary"
          size="sm"
          icon={LuDownload}
          loading={exporting}
          loadingLabel="Exporting…"
          disabled={totalCount === 0}
          onClick={() => void handleExport()}
        >
          Export to Excel
        </StandardButton>
      </div>

      {exportError && <p className="text-sm text-danger px-5 pt-4">{exportError}</p>}
```

- [ ] **Step 5: Type-check and lint**

Run: `cd apps/web && npm run build && npx eslint src/integrations/template/pages/PaymentsQueueTable.tsx src/core/api/endpoints/paymentApi.ts`
Expected: build succeeds with no type errors; eslint reports nothing for these files.

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/core/api/endpoints/paymentApi.ts apps/web/src/integrations/template/pages/PaymentsQueueTable.tsx
git commit -m "feat: Export to Excel button on the payments queue"
```

---

### Task 7: Docs

**Files:**
- Modify: `openspecs/payments.md`
- Modify: `openspecs/system-logs.md`
- Modify: `openspec/changes/add-payments-queue-export/tasks.md`

- [ ] **Step 1: Add the endpoint** to the list in `openspecs/payments.md`, after `GET /reports/summary`

```markdown
- `GET /export` — `members:view`. The queue as an `.xlsx`. See "Queue export" below.
```

- [ ] **Step 2: Add the section** to `openspecs/payments.md`, before `## Event registration payments`

```markdown
## Queue export

`GET /api/payments/export` returns every `Submitted` payment, oldest first and unpaged, as one sheet:
Full Name, Email, Address, CP Number, Amount, Reference, Paid On, Proof of Payment, For. The
**Export to Excel** button on `PaymentsQueueTable.tsx` calls it.

- **Proofs are embedded, not linked**, so the file stands alone. Images are resized to at most 1000px
  on the long side; a PDF proof has page 1 rendered to a picture (`PaymentProofPicture`).
- **One bad proof never fails the export.** The cell says why instead: no proof attached, file missing
  from storage, or file unreadable. A missing native library does fail it, since that is a broken
  deployment rather than a bad file.
- **Audited** as `payments.exported`, because the file carries personal data out of the portal.
- **PDFtoImage is pinned to 4.1.0.** Later versions need SkiaSharp 4.x. See
  `openspec/changes/add-payments-queue-export/proposal.md` for the trade-off.
- `PaymentExportWorkbook` is a pure renderer, like `ReceiptGenerator`. Storage keys stay inside
  `PaymentExportService`.
```

- [ ] **Step 3: Update `openspecs/system-logs.md`**

Add after the `membership.approved` bullet:

```markdown
- **`payments.exported`** — written by `PaymentsController.Export` each time the payments queue is
  downloaded as Excel, with the actor, IP and row count in `Metadata`. No member data is stored.
```

In the "Not audited" bullet, change `This table only covers the four event types above` to `This table only covers the five event types above`.

- [ ] **Step 4: Tick the completed items** in `openspec/changes/add-payments-queue-export/tasks.md`, leaving the "Verify on staging" items open, and change the Status line in `proposal.md` to `**Implemented** <date>. Not yet verified on staging.`

- [ ] **Step 5: Commit**

```bash
git add openspecs openspec/changes/add-payments-queue-export
git commit -m "docs: payments queue export"
```

---

### Task 8: Verify locally

- [ ] **Step 1: Run every test suite**

Run: `dotnet test`
Expected: all three test projects pass. Report the counts. Any failure stops the work here.

- [ ] **Step 2: Try it in the browser** using the `run` skill

Log in as a staff user, open Members → Payments, and click **Export to Excel** with a local queue holding an image proof and a PDF proof. Confirm: the button shows `Exporting…`, the file downloads with today's date, it opens in Excel, columns are in the agreed order, and both pictures sit in the right rows. Then empty the queue and confirm the button is disabled.

- [ ] **Step 3: Check the audit entry**

Open `/admin/system-logs` as Super Admin and confirm a `payments.exported` row with the right actor and row count.

---

### Task 9: Verify on staging

Pushing and deploying is outward-facing, so **ask the user before starting this task**. Follow `BRANCHING.md`, and the tag and CHANGELOG scheme on any staging or main merge.

- [ ] **Step 1: Deploy to staging** once the user approves
- [ ] **Step 2: Export a queue with an image proof and a PDF proof** from the staging site, and confirm the PDF row shows a picture rather than a text note. This proves PDFium and Skia load in the Linux container
- [ ] **Step 3: Regression check** a member receipt and a proof upload on staging, which both use SkiaSharp
- [ ] **Step 4: Tick the staging items** in `tasks.md`, update the Status line, and commit
