using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using System.Text.RegularExpressions;

namespace PSMPE.Portal.Application.Payments.Export;

/// <summary>One sheet row. Picture and ProofNote are alternatives: a note explains a missing picture.</summary>
public sealed record PaymentExportRow(
    string FullName, string? Email, string Address, string? MobileNumber, decimal Amount,
    string? ReferenceNo, DateOnly PaidOn, ProofPicture? Picture, string? ProofNote, string For);

/// <summary>
/// Pure renderer from rows to xlsx bytes, with no database or storage dependency, like
/// ReceiptGenerator. Text always goes in as a cell value, never as a formula, and text a spreadsheet
/// would read as one gets a leading apostrophe, so member-entered
/// text starting with "=" can't execute in Excel.
/// </summary>
public static partial class PaymentExportWorkbook
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

        sheet.Cell(rowNumber, 1).Value = NeutraliseFormula(row.FullName);
        sheet.Cell(rowNumber, 2).Value = NeutraliseFormula(row.Email ?? string.Empty);
        sheet.Cell(rowNumber, 3).Value = NeutraliseFormula(row.Address);

        var mobile = sheet.Cell(rowNumber, 4);
        mobile.Style.NumberFormat.Format = "@";
        mobile.Value = NeutraliseFormula(row.MobileNumber ?? string.Empty);

        var amount = sheet.Cell(rowNumber, 5);
        amount.Value = row.Amount;
        amount.Style.NumberFormat.Format = "\"₱\"#,##0.00";

        sheet.Cell(rowNumber, 6).Value = NeutraliseFormula(row.ReferenceNo ?? string.Empty);

        var paidOn = sheet.Cell(rowNumber, 7);
        paidOn.Value = row.PaidOn.ToDateTime(TimeOnly.MinValue);
        paidOn.Style.NumberFormat.Format = "mm/dd/yyyy";

        sheet.Cell(rowNumber, 9).Value = NeutraliseFormula(row.For);

        if (row.Picture is not null)
        {
            AddPicture(sheet, rowNumber, row.Picture);
        }
        else
        {
            sheet.Cell(rowNumber, ProofColumn).Value = row.ProofNote ?? string.Empty;
        }
    }

    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@'];

    /// <summary>
    /// Prefixes an apostrophe to text a spreadsheet would read as a formula. The cell type protects
    /// the xlsx, but not a CSV re-save. Plain phone numbers and single characters are left alone.
    /// </summary>
    private static string NeutraliseFormula(string value)
    {
        // ClosedXML strips one leading apostrophe, so double it to keep the member's text exact.
        if (value.StartsWith('\''))
        {
            return "'" + value;
        }

        var trimmed = value.TrimStart();
        if (value.Length < 2 || trimmed.Length == 0 || Array.IndexOf(FormulaTriggers, trimmed[0]) < 0 || PlainNumber().IsMatch(value))
        {
            return value;
        }

        // ClosedXML treats one leading apostrophe as Excel's hidden quote prefix and drops it, so
        // a second one is needed for the apostrophe to be real cell text that survives a CSV save.
        return "''" + value;
    }

    [GeneratedRegex(@"^\+?\d+$")]
    private static partial Regex PlainNumber();

    private static void AddPicture(IXLWorksheet sheet, int rowNumber, ProofPicture picture)
    {
        var scale = Math.Min((double)PictureBoxWidthPx / picture.Width, (double)PictureBoxHeightPx / picture.Height);

        sheet.AddPicture(new MemoryStream(picture.Jpeg), XLPictureFormat.Jpeg, $"Proof{rowNumber}")
            .MoveTo(sheet.Cell(rowNumber, ProofColumn), PictureOffsetPx, PictureOffsetPx)
            .WithSize(Math.Max(1, (int)(picture.Width * scale)), Math.Max(1, (int)(picture.Height * scale)));
    }
}
