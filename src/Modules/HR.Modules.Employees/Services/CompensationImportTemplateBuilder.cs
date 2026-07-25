using ClosedXML.Excel;

namespace HR.Modules.Employees.Services;

internal sealed record CompensationImportTemplateRow(
    string EmployeeNumber,
    string EmployeeName,
    decimal? CurrentSalary,
    string? SalaryFrequency);

/// <summary>
/// Builds the downloadable compensation-import .xlsx template: a main sheet pre-populated with
/// read-only reference data (Employee Number/Name/Current Salary/Salary Frequency) plus blank
/// entry columns (New Salary/Effective Date/Reason/Notes), and a second Instructions sheet.
/// </summary>
internal static class CompensationImportTemplateBuilder
{
    public const string SheetName = "Compensation Import";
    public const string InstructionsSheetName = "Instructions";

    public static readonly string[] Headers =
    [
        "Employee Number", "Employee Name", "Current Salary", "Salary Frequency",
        "New Salary", "Effective Date", "Reason", "Notes"
    ];

    private static readonly XLColor HeaderBackground = XLColor.FromHtml("#2F5496");

    public static byte[] Build(IReadOnlyList<CompensationImportTemplateRow> rows)
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add(SheetName);

        for (var col = 0; col < Headers.Length; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = Headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBackground;
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.EmployeeNumber;
            sheet.Cell(rowIndex, 2).Value = row.EmployeeName;

            if (row.CurrentSalary.HasValue)
                sheet.Cell(rowIndex, 3).Value = row.CurrentSalary.Value;

            if (row.SalaryFrequency is not null)
                sheet.Cell(rowIndex, 4).Value = row.SalaryFrequency;

            // Columns 5-8 (New Salary, Effective Date, Reason, Notes) are intentionally left
            // blank for HR to fill in.
            rowIndex++;
        }

        var lastDataRow = Math.Max(rowIndex - 1, 1);

        sheet.Columns(1, Headers.Length).AdjustToContents();

        // ClosedXML cells are locked by default; sheet protection only takes effect once
        // explicitly enabled below, and the entry columns must be explicitly unlocked so they
        // remain editable while the reference columns (1-4, including the header row) stay locked.
        sheet.Range(1, 1, lastDataRow, 4).Style.Protection.SetLocked(true);

        if (lastDataRow >= 2)
            sheet.Range(2, 5, lastDataRow, Headers.Length).Style.Protection.SetLocked(false);

        sheet.Range(1, 5, 1, Headers.Length).Style.Protection.SetLocked(true);

        sheet.Protect()
            .AllowElement(XLSheetProtectionElements.SelectLockedCells)
            .AllowElement(XLSheetProtectionElements.SelectUnlockedCells);

        AddInstructionsSheet(workbook);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddInstructionsSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add(InstructionsSheetName);

        var title = sheet.Cell(1, 1);
        title.Value = "Compensation Import — Instructions";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;

        string[] instructions =
        [
            "1. Do not edit the Employee Number, Employee Name, Current Salary or Salary Frequency columns — these are reference data and are locked.",
            "2. Fill in New Salary with the employee's new gross salary. It must be greater than 0.",
            "3. Fill in Effective Date using the format yyyy-mm-dd — the date the new salary takes effect.",
            "4. Fill in Reason using one of: NewHire, AnnualReview, Promotion, MarketAdjustment, RoleChange, Correction, Other.",
            "5. Salary Frequency shown is the employee's current pay frequency and is for reference only — it cannot be changed via bulk import. The new salary amount always uses this same frequency.",
            "6. Notes is optional free text.",
            "7. Each employee can only appear once per import file, and the Effective Date must not overlap an existing compensation record for that employee."
        ];

        var row = 3;
        foreach (var instruction in instructions)
        {
            sheet.Cell(row, 1).Value = instruction;
            row++;
        }

        row += 1;
        var exampleTitle = sheet.Cell(row, 1);
        exampleTitle.Value = "Example rows:";
        exampleTitle.Style.Font.Bold = true;
        row++;

        for (var col = 0; col < Headers.Length; col++)
        {
            var cell = sheet.Cell(row, col + 1);
            cell.Value = Headers[col];
            cell.Style.Font.Bold = true;
        }

        row++;
        sheet.Cell(row, 1).Value = "ACME-003";
        sheet.Cell(row, 2).Value = "Priya Sharma";
        sheet.Cell(row, 3).Value = 52000;
        sheet.Cell(row, 4).Value = "Annual";
        sheet.Cell(row, 5).Value = 56000;
        sheet.Cell(row, 6).Value = "2027-01-01";
        sheet.Cell(row, 7).Value = "AnnualReview";
        sheet.Cell(row, 8).Value = "Annual pay review";

        row++;
        sheet.Cell(row, 1).Value = "ACME-004";
        sheet.Cell(row, 2).Value = "Tom Williams";
        sheet.Cell(row, 3).Value = 34000;
        sheet.Cell(row, 4).Value = "Annual";
        sheet.Cell(row, 5).Value = 36500;
        sheet.Cell(row, 6).Value = "2027-01-01";
        sheet.Cell(row, 7).Value = "Promotion";
        sheet.Cell(row, 8).Value = "";

        sheet.Columns(1, Headers.Length).AdjustToContents();
    }
}
