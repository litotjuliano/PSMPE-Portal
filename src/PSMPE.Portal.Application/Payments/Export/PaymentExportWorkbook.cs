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
/// ReceiptGenerator. The first sheet lists every payment with a link to its proof; each payment
/// then gets its own sheet showing the proof large, linked both ways. Text always goes in as a cell value, never as a formula, and text a spreadsheet
/// would read as one gets a leading apostrophe, so member-entered
/// text starting with "=" can't execute in Excel.
/// </summary>
public static partial class PaymentExportWorkbook
{
    private const string SheetName = "Awaiting Verification";
    private const string HeaderColor = "#0E4C92";
    private const int ProofColumn = 8;
    private const double DataRowHeightPoints = 48;
    private const string ProofLinkText = "Proof link";
    private const string AmountFormat = "\"₱\"#,##0.00";
    private const string DateFormat = "mm/dd/yyyy";
    private const int MaxSheetNameLength = 31;
    private const int RecordFirstDetailRow = 3;
    private const int RecordPictureColumn = 4;
    private const double RecordWrappedRowHeightPoints = 48;

    private static readonly (string Header, double Width)[] Columns =
    [
        ("Full Name", 26), ("Email", 30), ("Address", 40), ("CP Number", 16), ("Amount", 13),
        ("Reference", 16), ("Paid On", 12), ("Proof of Payment", 18), ("For", 26),
    ];

    public static byte[] Build(IReadOnlyList<PaymentExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        WriteHeader(sheet);
        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 2;
            WriteRow(sheet, rowNumber, rows[index]);

            var record = workbook.Worksheets.Add(RecordSheetName(index + 1, rows[index].FullName));
            WriteRecordSheet(record, rowNumber, rows[index]);
            LinkTo(sheet.Cell(rowNumber, 1), record.Name, "A1");
            if (rows[index].Picture is not null)
            {
                LinkTo(sheet.Cell(rowNumber, ProofColumn), record.Name, "A1");
            }
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
        amount.Style.NumberFormat.Format = AmountFormat;

        sheet.Cell(rowNumber, 6).Value = NeutraliseFormula(row.ReferenceNo ?? string.Empty);

        var paidOn = sheet.Cell(rowNumber, 7);
        paidOn.Value = row.PaidOn.ToDateTime(TimeOnly.MinValue);
        paidOn.Style.NumberFormat.Format = DateFormat;

        sheet.Cell(rowNumber, 9).Value = NeutraliseFormula(row.For);

        sheet.Cell(rowNumber, ProofColumn).Value = row.Picture is null ? row.ProofNote ?? string.Empty : ProofLinkText;
    }

    /// <summary>
    /// The sequence number keeps names unique, including for a member with two payments, and non-empty
    /// when nothing in the name survives. Names are member-entered, so only known-safe characters
    /// stay, which also means the name never needs escaping inside a link.
    /// </summary>
    private static string RecordSheetName(int sequence, string fullName)
    {
        var name = $"{sequence} {UnsafeSheetNameCharacters().Replace(fullName, string.Empty)}";
        return name[..Math.Min(name.Length, MaxSheetNameLength)].Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{M}\p{N} .\-]")]
    private static partial Regex UnsafeSheetNameCharacters();

    private static void LinkTo(IXLCell cell, string sheetName, string address) =>
        cell.SetHyperlink(new XLHyperlink($"'{sheetName}'!{address}"));

    private static void WriteRecordSheet(IXLWorksheet sheet, int listRowNumber, PaymentExportRow row)
    {
        sheet.Column(1).Width = 18;
        sheet.Column(2).Width = 45;
        sheet.Column(3).Width = 3;

        var back = sheet.Cell(1, 1);
        back.Value = "Back to list";
        LinkTo(back, SheetName, $"A{listRowNumber}");

        var rowNumber = RecordFirstDetailRow;
        WriteDetail(sheet, rowNumber++, "Full Name", NeutraliseFormula(row.FullName));
        WriteDetail(sheet, rowNumber++, "Email", NeutraliseFormula(row.Email ?? string.Empty));
        WriteDetail(sheet, rowNumber, "Address", NeutraliseFormula(row.Address));
        sheet.Row(rowNumber++).Height = RecordWrappedRowHeightPoints;

        WriteDetail(sheet, rowNumber, "CP Number", string.Empty);
        sheet.Cell(rowNumber, 2).Style.NumberFormat.Format = "@";
        sheet.Cell(rowNumber++, 2).Value = NeutraliseFormula(row.MobileNumber ?? string.Empty);

        WriteDetail(sheet, rowNumber, "Amount", row.Amount);
        sheet.Cell(rowNumber++, 2).Style.NumberFormat.Format = AmountFormat;

        WriteDetail(sheet, rowNumber++, "Reference", NeutraliseFormula(row.ReferenceNo ?? string.Empty));

        WriteDetail(sheet, rowNumber, "Paid On", row.PaidOn.ToDateTime(TimeOnly.MinValue));
        sheet.Cell(rowNumber++, 2).Style.NumberFormat.Format = DateFormat;

        WriteDetail(sheet, rowNumber, "For", NeutraliseFormula(row.For));
        sheet.Row(rowNumber++).Height = RecordWrappedRowHeightPoints;

        if (row.Picture is not null)
        {
            sheet.AddPicture(new MemoryStream(row.Picture.Jpeg), XLPictureFormat.Jpeg, "Proof")
                .MoveTo(sheet.Cell(RecordFirstDetailRow, RecordPictureColumn))
                .WithSize(row.Picture.Width, row.Picture.Height);
        }
        else
        {
            WriteDetail(sheet, rowNumber, "Proof of Payment", row.ProofNote ?? string.Empty);
        }
    }

    private static void WriteDetail(IXLWorksheet sheet, int rowNumber, string label, XLCellValue value)
    {
        var labelCell = sheet.Cell(rowNumber, 1);
        labelCell.Value = label;
        labelCell.Style.Font.Bold = true;
        labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var valueCell = sheet.Cell(rowNumber, 2);
        valueCell.Value = value;
        valueCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        valueCell.Style.Alignment.WrapText = true;
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
}
