using ClosedXML.Excel;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests;

public class CompensationImportFileParserTests
{
    [Fact]
    public void Parse_Reads_Rows_By_Header_Name_Regardless_Of_Column_Order()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");

        // Deliberately out-of-order columns relative to the template, to prove header-name lookup.
        sheet.Cell(1, 1).Value = "Reason";
        sheet.Cell(1, 2).Value = "Employee Number";
        sheet.Cell(1, 3).Value = "New Salary";
        sheet.Cell(1, 4).Value = "Salary Frequency";
        sheet.Cell(1, 5).Value = "Effective Date";
        sheet.Cell(1, 6).Value = "Notes";

        sheet.Cell(2, 1).Value = "AnnualReview";
        sheet.Cell(2, 2).Value = "EMP-001";
        sheet.Cell(2, 3).Value = "46000";
        sheet.Cell(2, 4).Value = "Annual";
        sheet.Cell(2, 5).Value = new DateTime(2027, 1, 1);
        sheet.Cell(2, 6).Value = "Annual pay review";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var rows = CompensationImportFileParser.Parse(stream);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("EMP-001", row.EmployeeNumber);
        Assert.Equal("46000", row.NewSalary);
        Assert.Equal("Annual", row.SalaryFrequency);
        Assert.Equal(new DateOnly(2027, 1, 1), row.EffectiveDate);
        Assert.Equal("AnnualReview", row.Reason);
        Assert.Equal("Annual pay review", row.Notes);
    }

    [Fact]
    public void Parse_Skips_Blank_Rows()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");

        sheet.Cell(1, 1).Value = "Employee Number";
        sheet.Cell(1, 2).Value = "New Salary";
        sheet.Cell(1, 3).Value = "Salary Frequency";
        sheet.Cell(1, 4).Value = "Effective Date";
        sheet.Cell(1, 5).Value = "Reason";
        sheet.Cell(1, 6).Value = "Notes";

        sheet.Cell(2, 1).Value = "EMP-001";
        sheet.Cell(2, 2).Value = "46000";
        sheet.Cell(2, 3).Value = "Annual";
        sheet.Cell(2, 4).Value = new DateTime(2027, 1, 1);
        sheet.Cell(2, 5).Value = "AnnualReview";

        // Row 3 is entirely blank (a stray formatted row, common in exported spreadsheets).

        sheet.Cell(4, 1).Value = "EMP-002";
        sheet.Cell(4, 2).Value = "38000";
        sheet.Cell(4, 3).Value = "Annual";
        sheet.Cell(4, 4).Value = new DateTime(2027, 2, 1);
        sheet.Cell(4, 5).Value = "Promotion";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var rows = CompensationImportFileParser.Parse(stream);

        Assert.Equal(2, rows.Count);
        Assert.Equal("EMP-001", rows[0].EmployeeNumber);
        Assert.Equal(4, rows[1].RowNumber);
        Assert.Equal("EMP-002", rows[1].EmployeeNumber);
    }

    [Fact]
    public void Parse_Returns_Empty_List_For_Sheet_With_No_Used_Range()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Sheet1");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var rows = CompensationImportFileParser.Parse(stream);

        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_Reads_Rows_Produced_By_The_Import_Template_Builder()
    {
        var rows = new[] { new CompensationImportTemplateRow("EMP-001", "Alice Smith", 45000m, "Annual") };
        var templateBytes = CompensationImportTemplateBuilder.Build(rows);

        using var stream = new MemoryStream(templateBytes);
        var parsedRows = CompensationImportFileParser.Parse(stream);

        var row = Assert.Single(parsedRows);
        Assert.Equal("EMP-001", row.EmployeeNumber);
        // New Salary / Effective Date / Reason / Notes columns are intentionally blank in the template.
        Assert.Null(row.NewSalary);
        Assert.Null(row.EffectiveDate);
        Assert.Null(row.Reason);
    }
}
